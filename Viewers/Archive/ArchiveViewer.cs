using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Archive.Pages;

namespace MauiMultimedia.Viewers.Archive;

public class ArchiveViewer : IFileViewer
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".tgz",
        ".rar", ".7z", ".zst", ".xz", ".bz2"
    };

    public string DisplayName => "压缩文件查看器";
    public Type ComponentType => typeof(ArchivePage);

    public bool CanHandle(FileSystemItem item)
    {
        if (item == null || item.IsFolder) return false;
        var ext = Path.GetExtension(item.Name);
        return Exts.Contains(ext) || item.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }
}
