using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Video.Pages;

namespace MauiMultimedia.Viewers.Video;

public class VideoViewer : IFileViewer
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".m4v",
        ".3gp", ".ogv", ".mpg", ".mpeg", ".ts", ".mts"
    };

    public string DisplayName => "视频播放器";
    public Type ComponentType => typeof(VideoPage);

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));
}
