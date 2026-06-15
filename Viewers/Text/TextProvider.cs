using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Text;

public class TextProvider : IViewProvider
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".csv",
        ".xml", ".json", ".yaml", ".yml",
        ".html", ".htm", ".css", ".js",
        ".py", ".cs",
        ".sh", ".bat", ".ps1",
        ".ini", ".cfg", ".conf", ".env", ".gitignore"
    };

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));

    public string? GetItemCssClass(FileSystemItem item) => "is-text-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F4DD";
    public string? GetItemSnapshot(FileSystemItem item) => null;
    public bool CanProvideSnapshot(FileSystemItem item) => false;
    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => new("文档", Exts.ToArray(), "\U0001F4DD");
}
