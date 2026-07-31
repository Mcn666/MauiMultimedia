namespace MauiMultimedia.Viewers.Archive;

/// <summary>
/// 压缩包查看器支持的扩展名常量。
/// </summary>
public static class ArchiveConstants
{
    public static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".tgz", ".rar", ".7z", ".bz2",
        // 以下扩展名需注意：EntryPoint 中通过 item.Name 匹配无法命中复合后缀，Viewer 额外有
        // EndsWith(".tar.gz") 逻辑；zst 和 xz 由 SharpCompress 原生支持。
        ".tar.gz", ".zst", ".xz"
    };
}
