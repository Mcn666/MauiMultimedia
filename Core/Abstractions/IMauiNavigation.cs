using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Core.Abstractions;

public interface IMauiNavigation
{
    Task GoBackAsync();
    Task NavigateToViewerAsync(IFileViewer viewer, FileSystemItem item);
}
