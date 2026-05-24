using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Html;

public class HtmlViewer : IFileViewer
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".mht", ".mhtml"
    };

    public string DisplayName => "网页查看器";

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));

    public string GetViewerRoute(FileSystemItem item) =>
        $"/htmlviewer?path={Uri.EscapeDataString(item.FullPath)}";
}
