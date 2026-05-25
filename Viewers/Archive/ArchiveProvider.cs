using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Archive;

public class ArchiveProvider : IViewProvider
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".tgz", ".tar.gz",
        ".rar", ".7z", ".zst", ".xz", ".bz2"
    };

    public bool CanHandle(FileSystemItem item)
    {
        if (item.IsFolder) return false;
        var ext = Path.GetExtension(item.Name);
        return Exts.Contains(ext) || item.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetItemCssClass(FileSystemItem item) => "is-archive-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F4E6";
    public string? GetItemSnapshot(FileSystemItem item) => null;
    public bool CanProvideSnapshot(FileSystemItem item) => false;
    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => new("压缩包", Exts.ToArray(), "\U0001F4E6");
}
