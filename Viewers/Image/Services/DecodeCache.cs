using System.Collections.Concurrent;

namespace MauiMultimedia.Viewers.Image.Services;

/// <summary>
/// LRU 解码缓存，存储 data:URI + 图片尺寸。
/// 预加载和前后翻页共享同一缓存。
/// </summary>
public static class DecodeCache
{
    private const int MaxEntries = 20;

    private static readonly ConcurrentDictionary<string, Entry> Store = new();
    private static readonly LinkedList<string> AccessOrder = new();
    private static readonly object Sync = new();

    public readonly record struct Entry(string DataUri, int Width, int Height);

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
        Store[path] = new Entry(dataUri, width, height);
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
        while (Store.Count > MaxEntries)
        {
            string? oldest;
            lock (Sync)
            {
                oldest = AccessOrder.First?.Value;
                if (oldest == null) break;
                AccessOrder.RemoveFirst();
            }
            if (oldest != null) Store.TryRemove(oldest, out _);
        }
    }
}
