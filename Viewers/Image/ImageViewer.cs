using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Image.Pages;

namespace MauiMultimedia.Viewers.Image;

public class ImageViewer : IFileViewer
{
    public string DisplayName => "图片查看器";
    public Type ComponentType => typeof(ImagePage);

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && ImageConstants.AllExts.Contains(Path.GetExtension(item.Name));
}
