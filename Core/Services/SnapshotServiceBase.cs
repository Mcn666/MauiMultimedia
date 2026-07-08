using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MauiMultimedia.Core.Services;

/// <summary>
/// 缩略图快照共享基类：封装通用的「缓存 + 并发限流 + 文件检查 + 双重检查」骨架，
/// 子类只需实现 <see cref="GenerateAsync"/> 提供“真正生成一张图”的差异逻辑。
///
/// 设计要点：
/// - 缓存用 LRU（上限 <see cref="MaxCacheEntries"/>）的 Dictionary + LinkedList，避免浏览大量文件后内存无界增长。
/// - 限流用 SemaphoreSlim(2)，最多 2 个并发生成，防止大图/视频取帧拖垮 UI 线程。
/// - 静态 [JSInvokable] 入口由各查看器以薄外观类持有本类的单例并委托（JSInvokable 必须落在静态方法上）。
/// - 抽象方法返回最终要缓存的 data:URI 字符串（失败返回 null）；具体子类自行决定如何生成（图片解码 / 视频取帧 + base64 包裹）。
/// </summary>
public abstract class SnapshotServiceBase
{
    // 缩略图缓存（LRU 上限，避免内存无界增长）
    private const int MaxCacheEntries = 400;
    private readonly object _cacheLock = new();
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, string> _cache = new();

    // 限流：最多 2 个并发生成
    private readonly SemaphoreSlim _throttle = new(2);

    /// <summary>
    /// 生成缩略图并缓存。供 JS IntersectionObserver 在可视区内调用。
    /// </summary>
    public async Task<string?> GetSnapshotAsync(string filePath)
    {
        // 缓存命中直接返回
        var cached = TryGet(filePath);
        if (cached != null) return cached;

        // 文件检查
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        await _throttle.WaitAsync();
        try
        {
            // 双重检查
            cached = TryGet(filePath);
            if (cached != null) return cached;

            var thumb = await GenerateAsync(filePath);
            if (!string.IsNullOrEmpty(thumb))
                Set(filePath, thumb);
            return thumb;
        }
        catch
        {
            Debug.WriteLine($"[Snap] failed: {filePath}");
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private string? TryGet(string key)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var v))
            {
                _lru.Remove(key);
                _lru.AddLast(key);
                return v;
            }
            return null;
        }
    }

    private void Set(string key, string value)
    {
        lock (_cacheLock)
        {
            if (_cache.ContainsKey(key))
            {
                _lru.Remove(key);
            }
            else if (_cache.Count >= MaxCacheEntries)
            {
                // 淘汰最久未使用项
                var oldest = _lru.First;
                if (oldest != null)
                {
                    _cache.Remove(oldest.Value);
                    _lru.RemoveFirst();
                }
            }

            _cache[key] = value;
            _lru.AddLast(key);
        }
    }

    /// <summary>
    /// 生成缩略图 data:URI；失败返回 null。具体差异由子类实现。
    /// </summary>
    protected abstract Task<string?> GenerateAsync(string filePath);
}
