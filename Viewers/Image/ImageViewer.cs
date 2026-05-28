using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Image.Pages;

namespace MauiMultimedia.Viewers.Image;

public class ImageViewer : IFileViewer
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".ico", ".tiff", ".tif", ".svg", ".avif"
    };

    public string DisplayName => "图片查看器";
    public Type ComponentType => typeof(ImagePage);

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));
}
