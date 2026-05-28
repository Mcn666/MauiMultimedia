using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Shell.Services;

public class MauiNavigationService : IMauiNavigation
{
    private readonly ViewerPageFactory _pageFactory;

    public MauiNavigationService(ViewerPageFactory pageFactory)
    {
        _pageFactory = pageFactory;
    }

    public async Task GoBackAsync()
    {
        var page = Application.Current?.Windows[0]?.Page;
        if (page != null)
            await page.Navigation.PopAsync();
    }

    public async Task NavigateToViewerAsync(IFileViewer viewer, FileSystemItem item)
    {
        var page = _pageFactory.CreatePage(viewer, item);
        await Application.Current?.Windows[0]?.Page?.Navigation.PushAsync(page)!;
    }
}
