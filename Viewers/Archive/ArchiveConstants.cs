namespace MauiMultimedia.Viewers.Archive;

/// <summary>
/// 压缩包查看器支持的扩展名常量。
/// </summary>
public static class ArchiveConstants
{
    /// <summary>
    /// 压缩包查看器支持的扩展名。
    /// 注意：bz2/xz/zst 已被移除——SharpCompress 的 ArchiveFactory.OpenArchive 只支持
    /// Zip/Rar/Tar/GZip/7Zip 归档格式，单文件压缩流（bzip2/xz/zstd）打开会抛
    /// "Cannot determine compressed stream type"（经 TestSamples 解码测试确认）。
    /// .tar.gz 需额外 EndsWith 匹配（Path.GetExtension 只返回 .gz）。
    /// </summary>
    public static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".tgz", ".rar", ".7z", ".tar.gz"
    };
}
