using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Html;

public class HtmlProvider : IItemPresenter, ISnapshotProvider
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".mht", ".mhtml"
    };

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));

    public string? GetItemCssClass(FileSystemItem item) => "is-html-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F310";
    public string? GetItemSnapshot(FileSystemItem item) => null;
    public bool CanProvideSnapshot(FileSystemItem item) => false;
    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => new("网页", Exts.ToArray(), "\U0001F310");
}
