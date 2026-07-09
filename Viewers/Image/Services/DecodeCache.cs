using System.Collections.Concurrent;

namespace MauiMultimedia.Viewers.Image.Services;

/// <summary>
/// LRU 解码缓存，存储图片源（data:URI 或 FileServer URL）+ 尺寸。
/// 预加载和前后翻页共享同一缓存。
///
/// 淘汰策略由「条目数上限」改为「字节预算」LRU：大图（尤其是仍以 data:URI
/// 缓存的慢速路径产物）单个就能几十 MB，按条数上限（20）会轻松撑到数百 MB
/// 乃至 OOM；改为按累计字节预算淘汰后，配合 P1 的 FileServer URL（仅几十字节）
/// 可缓存成百上千张而不占内存，慢速路径的数据也始终受预算约束。
/// </summary>
public static class DecodeCache
{
    // 384 MB 预算。data:URI 按字符串长度×2（UTF-16）估算；FileServer URL 极小。
    private const long MaxBytes = 384L * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, Entry> Store = new();
    private static readonly LinkedList<string> AccessOrder = new();
    private static readonly object Sync = new();
    private static long _totalBytes;

    public readonly record struct Entry(string DataUri, int Width, int Height, long Size, bool IsDirectServe = false);

    public static Entry? Get(string path)
    {
        if (Store.TryGetValue(path, out var entry))
        {
            Touch(path);
            return entry;
        }
        return null;
    }

    public static void Set(string path, string dataUri, int width, int height)
    {
        long size = dataUri.Length * 2L; // UTF-16 char = 2 bytes (over-estimate is safe)
        lock (Sync)
        {
            if (Store.TryGetValue(path, out var old))
                _totalBytes -= old.Size;
            _totalBytes += size;
        }
        Store[path] = new Entry(dataUri, width, height, size);
        Touch(path);
        Evict();
    }

    /// <summary>
    /// 直出（FileServer）条目的缓存：只存尺寸 + 标志位，绝不存 token URL。
    /// token 在页面生命周期内有效、dispose 时吊销，因此把带 token 的 URL 写进
    /// 这个跨页面常驻缓存会导致二次进入同一张图拿到已吊销的 URL（403）。
    /// 真正加载时再经 ServedUrl() 重新签发 token，URL 不入库 → 内存占用 0。
    /// </summary>
    public static void SetDirectServe(string path, int width, int height)
    {
        long size = 0; // URL 不入库，零内存占用（P1 的省内存要点）
        lock (Sync)
        {
            if (Store.TryGetValue(path, out var old))
                _totalBytes -= old.Size;
            _totalBytes += size;
        }
        Store[path] = new Entry("", width, height, size, true);
        Touch(path);
        Evict();
    }

    private static void Touch(string path)
    {
        lock (Sync)
        {
            AccessOrder.Remove(path);
            AccessOrder.AddLast(path);
        }
    }

    private static void Evict()
    {
        while (_totalBytes > MaxBytes && AccessOrder.Count > 0)
        {
            string? oldest;
            lock (Sync)
            {
                oldest = AccessOrder.First?.Value;
                if (oldest == null) break;
                AccessOrder.RemoveFirst();
            }
            if (oldest != null && Store.TryRemove(oldest, out var removed))
                _totalBytes -= removed.Size;
        }
    }
}
