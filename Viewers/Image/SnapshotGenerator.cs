using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;
using MauiMultimedia.Viewers.Image.Services;

[assembly: InternalsVisibleTo("MauiMultimedia.Viewers.Image.Tests")]

namespace MauiMultimedia.Viewers.Image;

/// <summary>
/// JS 互调入口：供 IntersectionObserver 调用的缩略图生成。
/// 缓存已生成的缩略图，避免重复解码。
/// </summary>
public static class SnapshotGenerator
{
    // 文件路径 → data:URI（缩略图缓存）
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    // 限流：最多 2 个并发解码
    private static readonly SemaphoreSlim Throttle = new(2);

    /// <summary>
    /// 生成缩略图并缓存。由 JS IntersectionObserver 在可视区内调用。
    /// </summary>
    [JSInvokable("generateSnapshot")]
    public static async Task<string?> GenerateSnapshot(string filePath)
    {
        // 缓存命中直接返回
        if (Cache.TryGetValue(filePath, out var cached))
            return cached;

        // 文件检查
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        await Throttle.WaitAsync();
        try
        {
            // 双重检查
            if (Cache.TryGetValue(filePath, out cached))
                return cached;

            var thumb = await Task.Run(() => ImageProcessingService.GenerateThumbnail(filePath));
            if (!string.IsNullOrEmpty(thumb))
            {
                Cache[filePath] = thumb;
            }
            return thumb;
        }
        catch
        {
            Debug.WriteLine($"[Snap] Thumbnail failed: {filePath}");
            return null;
        }
        finally
        {
            Throttle.Release();
        }
    }
}
