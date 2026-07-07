using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Video;

public class VideoProvider : IViewProvider
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".m4v",
        ".3gp", ".ogv", ".mpg", ".mpeg", ".ts", ".mts"
    };

    private static readonly FileScanCategory _scanCategory = new("视频", Exts.ToArray(), "\U0001F3AC");

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));

    public string? GetItemCssClass(FileSystemItem item) => "is-video-file";
    public string? GetIcon(FileSystemItem item) => "\U0001F3AC";
    public string? GetItemSnapshot(FileSystemItem item) => null;

    /// <summary>
    /// 视频文件可生成首帧缩略图。取帧由各平台原生实现（Viewers/Video/Platforms/*）自包含于本查看器，
    /// 通过 VideoSnapshotGenerator.Extractor 桥接；快照由本程序集的 generateSnapshot 暴露给 Shell 网格。
    /// </summary>
    public bool CanProvideSnapshot(FileSystemItem item) => CanHandle(item);
    public void RequestItemSnapshot(FileSystemItem item) { }
    public event Action? SnapshotsUpdated { add { } remove { } }
    public FileScanCategory? ScanCategory => _scanCategory;

    // 快照 JSInvokable 在本查看器程序集内暴露，标识符 generateVideoSnapshot
    // 与图片查看器的 generateSnapshot 区分，避免同名 JSInvokable 注册冲突。
    public string SnapshotAssembly => "MauiMultimedia.Viewers.Video";
    public string SnapshotMethod => "generateVideoSnapshot";
}
