using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Image;

public class ImageProvider : IItemPresenter, ISnapshotProvider
{
    private static readonly FileScanCategory _scanCategory = new("图片", ImageConstants.AllExts.ToArray(), "\U0001F5BC");

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && ImageConstants.AllExts.Contains(Path.GetExtension(item.Name));

    public string? GetItemCssClass(FileSystemItem item) => "is-image-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F5BC";
    public string? GetItemSnapshot(FileSystemItem item) => null;

    /// <summary>
    /// 图片文件均可生成缩略图快照。SVG 曾因 SKCodec 无编解码器被排除，
    /// 现在 GenerateThumbnail 对 SVG 返回浏览器原生 data URI（见
    /// GenerateBrowserNativeThumbnail），不再需要排除。
    /// </summary>
    public bool CanProvideSnapshot(FileSystemItem item) => CanHandle(item);

    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => _scanCategory;

    // 快照 JSInvokable 在本查看器程序集内暴露，方法名与视频查看器一致（按程序集区分）。
    public string SnapshotAssembly => "MauiMultimedia.Viewers.Image";
    public string SnapshotMethod => "generateSnapshot";
}
