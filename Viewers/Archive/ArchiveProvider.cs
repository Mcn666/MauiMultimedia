using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Archive;

public class ArchiveProvider : IItemPresenter, ISnapshotProvider
{
    public bool CanHandle(FileSystemItem item)
    {
        if (item.IsFolder) return false;
        var ext = Path.GetExtension(item.Name);
        // ArchiveConstants.Exts 包含 .tar.gz，但 Path.GetExtension(".tar.gz") 返回 .gz，
        // 所以额外用 EndsWith 检查 .tar.gz
        return ArchiveConstants.Exts.Contains(ext) || item.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    public string? GetItemCssClass(FileSystemItem item) => "is-archive-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F4E6";
    public string? GetItemSnapshot(FileSystemItem item) => null;
    public bool CanProvideSnapshot(FileSystemItem item) => false;
    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => new("压缩包", ArchiveConstants.Exts.ToArray(), "\U0001F4E6");
}
