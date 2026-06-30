using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Video;

public class VideoProvider : IViewProvider
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".m4v",
        ".3gp", ".ogv", ".mpg", ".mpeg", ".ts", ".mts"
    };

    private static readonly FileScanCategory _scanCategory = new("视频", Exts.ToArray(), "\U0001F3AC");

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));

    public string? GetItemCssClass(FileSystemItem item) => "is-video-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F3AC";
    public string? GetItemSnapshot(FileSystemItem item) => null;
    public bool CanProvideSnapshot(FileSystemItem item) => false;
    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => _scanCategory;
}
