using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Image;

public class ImageProvider : IViewProvider
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".ico", ".tiff", ".tif", ".svg", ".avif"
    };

    /// <summary>
    /// SVG 不参与 SkiaSharp 缩略图生成
    /// </summary>
    private static readonly HashSet<string> NoSnapshotExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".svg"
    };

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));

    public string? GetItemCssClass(FileSystemItem item) => "is-image-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F5BC";
    public string? GetItemSnapshot(FileSystemItem item) => null;

    /// <summary>
    /// 图片文件可以生成缩略图快照（SVG 除外）
    /// </summary>
    public bool CanProvideSnapshot(FileSystemItem item) =>
        CanHandle(item) && !NoSnapshotExts.Contains(Path.GetExtension(item.Name));

    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
}
