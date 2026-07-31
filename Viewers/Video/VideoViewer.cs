using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Video.Pages;

namespace MauiMultimedia.Viewers.Video;

public class VideoViewer : IFileViewer
{
    public string DisplayName => "视频播放器";
    public Type ComponentType => typeof(VideoPage);

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && VideoConstants.Exts.Contains(Path.GetExtension(item.Name));
}
