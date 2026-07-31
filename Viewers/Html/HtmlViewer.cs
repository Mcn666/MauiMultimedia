using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Html.Pages;

namespace MauiMultimedia.Viewers.Html;

public class HtmlViewer : IFileViewer
{
    public string DisplayName => "网页查看器";
    public Type ComponentType => typeof(HtmlPage);

    public bool CanHandle(FileSystemItem item) =>
        item != null && !item.IsFolder && HtmlConstants.Exts.Contains(Path.GetExtension(item.Name));
}
