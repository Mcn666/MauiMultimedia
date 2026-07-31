using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Archive.Pages;

namespace MauiMultimedia.Viewers.Archive;

public class ArchiveViewer : IFileViewer
{
    public string DisplayName => "压缩文件查看器";
    public Type ComponentType => typeof(ArchivePage);

    public bool CanHandle(FileSystemItem item)
    {
        if (item == null || item.IsFolder) return false;
        var ext = Path.GetExtension(item.Name);
        // ArchiveConstants.Exts 包含 .tar.gz，但 Path.GetExtension(".tar.gz") 返回 .gz，
        // 所以额外用 EndsWith 检查 .tar.gz
        return ArchiveConstants.Exts.Contains(ext) || item.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }
}
