namespace MauiMultimedia.Viewers.Video;

/// <summary>
/// 视频查看器支持的扩展名常量。
/// </summary>
public static class VideoConstants
{
    public static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv", ".flv",
        ".m4v", ".3gp", ".ogv", ".mpg", ".mpeg",
        ".ts", ".mts", ".m2ts"
    };
}
