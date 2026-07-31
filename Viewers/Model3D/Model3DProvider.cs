using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Model3D;

public class Model3DProvider : IItemPresenter, ISnapshotProvider
{
    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Model3DConstants.Exts.Contains(Path.GetExtension(item.Name));

    public string? GetItemCssClass(FileSystemItem item) => "is-model-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F4F9";
    public string? GetItemSnapshot(FileSystemItem item) => null;
    public bool CanProvideSnapshot(FileSystemItem item) => false;
    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => new("3D 模型", Model3DConstants.Exts.ToArray(), "\U0001F4F9");
}
