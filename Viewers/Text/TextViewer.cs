using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Text.Pages;

namespace MauiMultimedia.Viewers.Text;

public class TextViewer : IFileViewer
{
    public string DisplayName => "文本查看器";
    public Type ComponentType => typeof(TextPage);

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && TextConstants.Exts.Contains(Path.GetExtension(item.Name));
}
