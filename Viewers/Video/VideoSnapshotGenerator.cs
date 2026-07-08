using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MauiMultimedia.Viewers.Video;
using Microsoft.JSInterop;

namespace MauiMultimedia.Viewers.Video;

/// <summary>
/// 视频快照 JS 互调入口，完全自包含于 Video 查看器（零侵入 Shell）。
/// 实际取帧由各平台原生实现（Viewers/Video.Native/Platforms/* 的 VideoFrameExtractor，
/// 实现 IVideoFrameExtractor）提供。
///
/// 注册方式（关键）：不再依赖跨程序集的 [ModuleInitializer] 设置静态字段——那会把
/// Extractor 设到“加载 Video.Native 时解析到的 Video 程序集实例”，而本 JSInvokable 实际跑在
/// Blazor 互操作上下文里使用的另一个 Video 实例上，两套 Extractor 不是同一个，导致取帧永远 null。
/// 改为：首次需要取帧时，从已加载的查看器程序集中“发现” IVideoFrameExtractor 实现并就地注册，
/// 确保注册发生在被本方法使用的同一个 Video 实例上下文中。
///
/// 缓存已生成结果避免重复解码；限流最多 2 个并发取帧。
/// 与图片快照（MauiMultimedia.Viewers.Image 的 generateSnapshot）流程一致，
/// 方法名刻意区分（generateVideoSnapshot）以避免同名 JSInvokable 互相覆盖。
/// </summary>
public static class VideoSnapshotGenerator
{
    // 平台原生取帧实现注册点：由 EnsureExtractorRegistered 从已加载程序集发现并填充。
    // 未注册（不支持的平台 / 未发现实现）时为 null，调用方回退 🎬 图标。
    public static Func<string, Task<byte[]?>>? Extractor { get; set; }

    // 文件路径 → data:URI（缩略图缓存）
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    // 限流：最多 2 个并发取帧
    private static readonly SemaphoreSlim Throttle = new(2);

    // 注册发现仅执行一次
    private static readonly object RegLock = new();
    private static bool _regSearched;

    /// <summary>
    /// 生成视频缩略图并缓存。由 Shell index.html 的 IntersectionObserver
    /// 在可视区内、按本查看器程序集以 generateVideoSnapshot 调用。
    /// </summary>
    [JSInvokable("generateVideoSnapshot")]
    public static async Task<string?> GenerateVideoSnapshot(string filePath)
    {
        // 缓存命中直接返回
        if (Cache.TryGetValue(filePath, out var cached))
            return cached;

        // 文件检查
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        // 确保取帧实现已注册（首次使用时从已加载程序集发现）
        EnsureExtractorRegistered();

        if (Extractor is null)
        {
            Debug.WriteLine($"[VideoSnap] Extractor 未注册（未发现 IVideoFrameExtractor 实现）path={filePath}");
            return null;
        }

        await Throttle.WaitAsync();
        try
        {
            // 双重检查
            if (Cache.TryGetValue(filePath, out cached))
                return cached;

            var bytes = await Extractor(filePath);
            if (bytes is { Length: > 0 })
            {
                var uri = "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
                Cache[filePath] = uri;
                return uri;
            }
            Debug.WriteLine($"[VideoSnap] 取帧返回空 bytes（平台不支持/编码不支持）path={filePath}");
            return null;
        }
        catch
        {
            Debug.WriteLine($"[VideoSnap] failed: {filePath}");
            return null;
        }
        finally
        {
            Throttle.Release();
        }
    }

    /// <summary>
    /// 从已加载的查看器程序集中发现 IVideoFrameExtractor 实现并就地注册。
    /// 双检锁保证只执行一次；找不到时兜底显式加载 Video.Native 程序集再试。
    /// </summary>
    private static void EnsureExtractorRegistered()
    {
        if (Extractor is not null) return;
        lock (RegLock)
        {
            if (Extractor is not null) return;
            if (_regSearched) return;
            _regSearched = true;

            // 1. 扫描已加载的查看器程序集（Video.Native 已由 viewer_assemblies.txt 自动加载）
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var n = asm.GetName().Name;
                if (n == null || !n.StartsWith("MauiMultimedia.Viewers.", StringComparison.Ordinal))
                    continue;
                if (TryFindAndRegister(asm)) return;
            }

            // 2. 兜底：即使尚未加载，也显式加载 companion 程序集后重试
            try
            {
                var native = Assembly.Load(new AssemblyName("MauiMultimedia.Viewers.Video.Native"));
                if (TryFindAndRegister(native)) return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoSnap] 显式加载 Video.Native 失败: {ex.GetType().Name}: {ex.Message}");
            }

            Debug.WriteLine("[VideoSnap] 未在任何查看器程序集中找到 IVideoFrameExtractor 实现");
        }
    }

    private static bool TryFindAndRegister(Assembly asm)
    {
        try
        {
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (typeof(IVideoFrameExtractor).IsAssignableFrom(t))
                {
                    var inst = (IVideoFrameExtractor?)Activator.CreateInstance(t);
                    if (inst is null) continue;
                    Extractor = inst.TryExtractAsync;
                    Debug.WriteLine($"[VideoSnap] 已注册取帧实现: {t.FullName}（来自 {asm.GetName().Name}）");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoSnap] 扫描 {asm.GetName().Name} 失败: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }
}
