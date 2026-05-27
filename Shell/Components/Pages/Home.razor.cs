using System.IO;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MauiMultimedia.Shell.Components.Pages;

public partial class Home
{
    private string currentPath = "";
    private List<FileSystemItem> items = new();
    private bool isLoading = true;
    private bool isRoot = false;
    private string? errorMessage;
    private string themeMode = "system"; // "light" | "dark" | "system"
    private bool showThemeMenu;
    private string viewMode = "list";

    private string sortColumn = "name";
    private string sortDirection = "asc";

    private bool showViewerPicker = false;
    private FileSystemItem? pendingItem;
    private List<IFileViewer> availableViewers = new();
    private bool permissionDenied;
    private bool showScanPanel;
    private bool isScanning;
    private bool isScanned;
    private List<FileSystemItem> scanResults = new();
    private string? scanTypeLabel;
    private CancellationTokenSource? scanCts;
    private List<FileScanCategory> scanCategories = new();

    private enum WindowsQuickAccess
    {
        Desktop, Downloads, Documents, Pictures, Music, Videos
    }

    private void NavigateToQuickAccess(WindowsQuickAccess qa)
    {
        var folder = qa switch
        {
            WindowsQuickAccess.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            WindowsQuickAccess.Downloads => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
            WindowsQuickAccess.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            WindowsQuickAccess.Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            WindowsQuickAccess.Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            WindowsQuickAccess.Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        currentPath = folder;
        _ = LoadItemsAsync();
    }

    private string GetItemIcon(FileSystemItem item)
    {
        if (item.IsFolder) return "\U0001F4C1";
        foreach (var p in ViewProviders)
            if (p.CanHandle(item)) { var i = p.GetIcon(item); if (i != null) return i; }
        return "\U0001F4C4";
    }

    private string? GetItemSnapshot(FileSystemItem item)
    {
        foreach (var p in ViewProviders)
            if (p.CanHandle(item)) return p.GetItemSnapshot(item);
        return null;
    }

    private bool CanProvideSnapshot(FileSystemItem item)
    {
        foreach (var p in ViewProviders)
            if (p.CanHandle(item) && p.CanProvideSnapshot(item)) return true;
        return false;
    }

    private string GetItemRowClass(FileSystemItem item)
    {
        var cls = item.IsFolder ? "folder" : "file";
        foreach (var p in ViewProviders)
            if (p.CanHandle(item)) { var e = p.GetItemCssClass(item); if (e != null) cls += " " + e; }
        return cls;
    }

    protected override async Task OnInitializedAsync()
    {
        // Android 存储权限检查
        if (!await FileSystemService.CheckStoragePermissionAsync())
        {
            permissionDenied = true;
            isLoading = false;
            await JS.InvokeVoidAsync("eval", @"
                if (!window.__permListener) {
                    window.__permListener = true;
                    document.addEventListener('visibilitychange', function() {
                        if (!document.hidden) {
                            location.reload();
                        }
                    });
                }
            ");
            return;
        }

        await JS.InvokeVoidAsync("eval",
            "document.documentElement.style.overflowY='scroll'");

        if (BrowserState.CurrentPath != null)
        {
            currentPath = BrowserState.CurrentPath;
            if (BrowserState.SortColumn != null) sortColumn = BrowserState.SortColumn;
            if (BrowserState.SortDirection != null) sortDirection = BrowserState.SortDirection;
            if (BrowserState.ViewMode != null) viewMode = BrowserState.ViewMode;
            BrowserState.Clear();
        }
        else
        {
            currentPath = FileSystemService.GetDefaultPath();
        }

        // 聚合各支持库的文件扫描分类（不合并相同标签）
        scanCategories = ViewProviders
            .Select(p => p.ScanCategory)
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();

        // 在加载前恢复排序和视图模式（JS interop 在 MAUI 中 OnInitializedAsync 可用）
        await RestoreSortAsync();
        await RestoreViewModeAsync();

        await LoadItemsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RestoreThemeAsync();
            StateHasChanged();

            // 首次渲染完成后淡入显示页面内容
            await JS.InvokeVoidAsync("eval",
                "document.getElementById('app').style.opacity = '1'");
        }
    }

    private string GetSortArrow(string col)
    {
        if (sortColumn != col) return "";
        return sortDirection == "asc" ? "▲" : "▼";
    }

    private string GetSortClass(string col)
    {
        return sortColumn == col ? "active" : "";
    }

    private Task OnSortByName() => OnSortClick("name");
    private Task OnSortByType() => OnSortClick("type");
    private Task OnSortByCount() => OnSortClick("count");
    private Task OnSortByDate() => OnSortClick("date");

    private async Task OnSortClick(string col)
    {
        if (sortColumn == col)
        {
            sortDirection = (sortDirection == "asc") ? "desc" : "asc";
        }
        else
        {
            sortColumn = col;
            sortDirection = "asc";
        }
        ApplySort();
        await SaveSortAsync();
    }

    private void ApplySort()
    {
        switch (sortColumn)
        {
            case "name":
                items = (sortDirection == "asc")
                    ? items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList()
                    : items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                break;
            case "type":
                items = (sortDirection == "asc")
                    ? items.OrderBy(i => i.IsFolder ? 0 : 1)
                        .ThenBy(i => i.IsFolder ? "" : Path.GetExtension(i.Name), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : items.OrderBy(i => i.IsFolder ? 1 : 0)
                        .ThenBy(i => Path.GetExtension(i.Name), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                break;
            case "count":
                items = (sortDirection == "asc")
                    ? items.OrderBy(i => i.IsFolder ? (i.ChildCount ?? 0) : int.MaxValue).ToList()
                    : items.OrderByDescending(i => i.IsFolder ? (i.ChildCount ?? 0) : int.MinValue).ToList();
                break;
            case "date":
                items = (sortDirection == "asc")
                    ? items.OrderBy(i => i.LastModified ?? DateTime.MinValue).ToList()
                    : items.OrderByDescending(i => i.LastModified ?? DateTime.MinValue).ToList();
                break;
        }
    }

    private async Task RestoreSortAsync()
    {
        var saved = await JS.InvokeAsync<string>("eval", "localStorage.getItem('filebrowser-sort')");
        if (!string.IsNullOrEmpty(saved))
        {
            var parts = saved.Split('|');
            if (parts.Length == 2)
            {
                sortColumn = parts[0];
                sortDirection = parts[1];
                ApplySort();
            }
        }
    }

    private async Task SaveSortAsync()
    {
        await JS.InvokeVoidAsync("eval", $"localStorage.setItem('filebrowser-sort','{sortColumn}|{sortDirection}')");
    }

    private async Task RestoreThemeAsync()
    {
        var saved = await JS.InvokeAsync<string>("eval", "localStorage.getItem('filebrowser-theme')");
        themeMode = (saved == "light" || saved == "dark" || saved == "system") ? saved : "system";
        await ApplyThemeAsync(themeMode);
        Preferences.Set("filebrowser-theme", themeMode);
    }

    private async Task ApplyThemeAsync(string mode)
    {
        bool dark;
        if (mode == "system")
            dark = await JS.InvokeAsync<bool>("eval", "window.matchMedia('(prefers-color-scheme: dark)').matches");
        else
            dark = mode == "dark";
        await JS.InvokeVoidAsync("eval", $"document.documentElement.setAttribute('data-theme','{(dark ? "dark" : "light")}')");

        // 同步 Android 状态栏
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is MauiMultimedia.Shell.MainActivity ma)
            ma.SetStatusBarStyle(dark);
#endif
    }

    private async Task SetTheme(string mode)
    {
        themeMode = mode;
        await ApplyThemeAsync(mode);
        await JS.InvokeVoidAsync("eval", $"localStorage.setItem('filebrowser-theme','{mode}')");
        Preferences.Set("filebrowser-theme", mode);
    }

    private Task SetThemeLight() => SetTheme("light");
    private Task SetThemeDark() => SetTheme("dark");
    private Task SetThemeSystem() => SetTheme("system");

    private async Task LoadItemsAsync()
    {
        isLoading = true;
        errorMessage = null;
        isRoot = FileSystemService.IsRootPath(currentPath);

        try
        {
            items = await FileSystemService.ListItemsAsync(currentPath);
            ApplySort();
            _ = LoadChildCountsAsync(items);
        }
        catch (Exception ex)
        {
            errorMessage = $"加载目录失败：{ex.Message}";
            items = new List<FileSystemItem>();
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadChildCountsAsync(List<FileSystemItem> folderItems)
    {
        var folders = folderItems.Where(i => i.IsFolder).ToList();
        var semaphore = new SemaphoreSlim(4);
        var tasks = folders.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                var count = await Task.Run(() => FileSystemService.TryGetChildCount(item.FullPath));
                item.ChildCount = count;
            }
            catch
            {
                item.ChildCount = -1;
            }
            finally
            {
                semaphore.Release();
            }
            await InvokeAsync(() =>
            {
                if (sortColumn == "count") ApplySort();
                StateHasChanged();
            });
        });
        await Task.WhenAll(tasks);
    }

    private void OnItemClick(FileSystemItem item)
    {
        if (item.IsFolder)
        {
            currentPath = item.FullPath;
            _ = LoadItemsAsync();
            return;
        }

        var viewers = FileViewers.Where(v => v.CanHandle(item)).ToList();

        if (viewers.Count == 1)
        {
            SaveBrowserState();
            Navigation.NavigateTo(viewers[0].GetViewerRoute(item));
        }
        else if (viewers.Count > 1)
        {
            pendingItem = item;
            availableViewers = viewers;
            showViewerPicker = true;
        }
    }

    private void CloseViewerPicker()
    {
        showViewerPicker = false;
        pendingItem = null;
        availableViewers = new();
    }

    private void OpenWithViewer(IFileViewer viewer)
    {
        if (pendingItem != null)
        {
            var route = viewer.GetViewerRoute(pendingItem);
            CloseViewerPicker();
            SaveBrowserState();
            Navigation.NavigateTo(route);
        }
    }

    private void SaveBrowserState()
    {
        BrowserState.CurrentPath = currentPath;
        BrowserState.SortColumn = sortColumn;
        BrowserState.SortDirection = sortDirection;
        BrowserState.ViewMode = viewMode;
        BrowserState.CurrentDirectoryFiles = items
            .Where(i => !i.IsFolder)
            .Select(i => i.FullPath)
            .ToList();
    }

    private static string GetViewerDisplayName(IFileViewer viewer)
    {
        return viewer.DisplayName;
    }

    private void RequestStorageAccess()
    {
        FileSystemService.RequestStoragePermission();
    }

    private async Task ToggleScanPanel()
    {
        showScanPanel = !showScanPanel;
        if (showScanPanel)
            await JS.InvokeVoidAsync("eval",
                "document.querySelector('.browser-main').classList.add('panel-open')");
    }
    private async Task CloseScanPanel()
    {
        showScanPanel = false;
        await JS.InvokeVoidAsync("eval",
            "document.querySelector('.browser-main').classList.remove('panel-open')");
    }

    private void ExitScan()
    {
        isScanned = false;
        isScanning = false;
        scanCts?.Cancel();
        scanCts = null;
        scanResults = new();
        _ = LoadItemsAsync();
    }

    private async Task StartScan(int typeIndex)
    {
        showScanPanel = false;
        scanCts?.Cancel();
        scanCts = new CancellationTokenSource();
        var ct = scanCts.Token;
        isScanning = true;
        scanResults = new();

        string[] exts;
        if (typeIndex > 0 && typeIndex <= scanCategories.Count)
        {
            var cat = scanCategories[typeIndex - 1];
            scanTypeLabel = cat.Label;
            exts = cat.Extensions;
        }
        else { isScanning = false; return; }

        try
        {
            var results = await FileSystemService.ScanFilesByTypeAsync(currentPath, exts, ct);
            scanResults = results;
            items = scanResults;
            isScanned = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            errorMessage = $"扫描失败：{ex.Message}";
        }
        finally
        {
            isScanning = false;
            isLoading = false;
        }
    }

    private async Task GoBack()
    {
        if (isScanned)
        {
            ExitScan();
            return;
        }
        var parent = FileSystemService.GetParentPath(currentPath);
        if (parent != null)
        {
            currentPath = parent;
            await LoadItemsAsync();
        }
    }

    private async Task RestoreViewModeAsync()
    {
        var saved = await JS.InvokeAsync<string>("eval", "localStorage.getItem('filebrowser-view')");
        if (saved == "grid" || saved == "list")
        {
            viewMode = saved;
        }
    }

    private async Task ToggleViewMode()
    {
        viewMode = (viewMode == "list") ? "grid" : "list";
        await JS.InvokeVoidAsync("eval", $"localStorage.setItem('filebrowser-view','{viewMode}')");
    }
}
