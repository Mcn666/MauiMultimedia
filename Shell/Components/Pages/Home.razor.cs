using MauiMultimedia.Core.Models;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace MauiMultimedia.Shell.Components.Pages;

public partial class Home : IDisposable
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
    private bool _showAliasDialog;
    private FileSystemItem? _aliasTarget;
    private string _aliasText = "";
    private bool permissionDenied;
    private bool showScanPanel;
    private bool isScanning;
    private bool isScanned;
    private List<FileSystemItem> scanResults = new();
    private string? scanTypeLabel;
    private CancellationTokenSource? scanCts;
    private List<FileScanCategory> scanCategories = new();
    private string? _activeFilePath;
    private HashSet<string> _lockedFiles = new(StringComparer.OrdinalIgnoreCase);

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

    private string GetDisplayName(FileSystemItem item)
    {
        var alias = Alias.GetAlias(item.FullPath);
        return alias ?? item.Name;
    }

    private async Task OnItemRightClick(FileSystemItem item)
    {
        _aliasTarget = item;
        _aliasText = Alias.GetAlias(item.FullPath) ?? "";
        _showAliasDialog = true;
        StateHasChanged();
        // 延迟聚焦输入框
        await Task.Delay(100);
        await JS.InvokeVoidAsync("eval",
            "document.querySelector('.alias-input')?.focus()");
    }

    private bool IsFileLocked(FileSystemItem item)
        => _lockedFiles.Contains(item.FullPath);

    private async Task OnLockClick(FileSystemItem item)
    {
        try
        {
            if (_lockedFiles.Contains(item.FullPath))
            {
                await FileLockService.UnlockAsync(item.FullPath);
                _lockedFiles.Remove(item.FullPath);
            }
            else
            {
                await FileLockService.LockAsync(item.FullPath);
                _lockedFiles.Add(item.FullPath);
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"锁定操作失败：{ex.Message}";
        }
        StateHasChanged();
    }

    private void RefreshLockStatus()
    {
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!item.IsFolder && FileLockService.IsLocked(item.FullPath))
                locked.Add(item.FullPath);
        }
        _lockedFiles = locked;
    }

    private void ConfirmAlias()
    {
        if (_aliasTarget == null) return;
        Alias.SetAlias(_aliasTarget.FullPath, _aliasText);
        _showAliasDialog = false;
        StateHasChanged();
    }

    private void CloseAliasDialog()
    {
        _showAliasDialog = false;
        StateHasChanged();
    }

    private async Task OnAliasKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") { ConfirmAlias(); }
        else if (e.Key == "Escape") { CloseAliasDialog(); }
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
        if (_activeFilePath != null && string.Equals(item.FullPath, _activeFilePath, StringComparison.OrdinalIgnoreCase))
            cls += " active-file";
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

        // 订阅运行时系统主题变更（App.OnRequestedThemeChanged → ApplyTheme → ThemeEvents）
        ThemeEvents.SystemThemeChanged += OnSystemThemeChanged;
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
        // localStorage 保存原始 mode，ApplyThemeAsync 已将解析值写入 Preferences
    }

    private async Task ApplyThemeAsync(string mode)
    {
        bool dark;
        if (mode == "system")
        {
            // 从 Preferences 读取已由原生代码（App.CreateWindow / App.ApplyTheme）解析好的值
            // 避免依赖 Android WebView 中不可靠的 window.matchMedia
            var saved = Preferences.Get("filebrowser-theme", "");
            dark = saved == "dark";
        }
        else
        {
            dark = mode == "dark";
        }

        await JS.InvokeVoidAsync("eval", $"document.documentElement.setAttribute('data-theme','{(dark ? "dark" : "light")}')");

        // 将解析后的暗/亮值写入 Preferences（供原生代码直接使用）
        Preferences.Set("filebrowser-theme", dark ? "dark" : "light");

        // 写入 localStorage 解析值，供内联 <script> 在 Blazor 加载前直接读取
        // 避免 Android WebView 中 window.matchMedia 不可靠导致的白色闪烁
        await JS.InvokeVoidAsync("eval", $"localStorage.setItem('filebrowser-theme-resolved','{(dark ? "dark" : "light")}')");

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
        // localStorage 保存原始 mode，ApplyThemeAsync 已将解析值写入 Preferences
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
            RefreshLockStatus();
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
            _ = NavigateToViewer(viewers[0], item);
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
            var item = pendingItem;
            CloseViewerPicker();
            _ = NavigateToViewer(viewer, item);
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

    private async Task NavigateToViewer(IFileViewer viewer, FileSystemItem item)
    {
        _activeFilePath = item.FullPath;
        SaveBrowserState();
        BrowserState.CurrentFilePath = item.FullPath;

        var page = PageFactory.CreatePage(viewer, item);
        await Application.Current?.Windows[0]?.Page?.Navigation.PushAsync(page)!;
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
        await JS.InvokeVoidAsync("eval",
            "document.querySelector('.browser-main').classList.remove('panel-open')");
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
            RefreshLockStatus();
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

    // ────────── 运行时系统主题变更 ──────────

    /// <summary>
    /// 系统主题在运行时切换时（如 Android 设置中切换暗/亮），
    /// App.OnRequestedThemeChanged → ApplyTheme → ThemeEvents 触发此方法。
    /// 直接通过 JS 更新 HTML data-theme，仅在 "system" 模式下响应。
    /// </summary>
    private void OnSystemThemeChanged(bool isDark)
    {
        // 只在"跟随系统"模式下响应，不覆盖用户的显式选择
        if (themeMode != "system") return;

        _ = InvokeAsync(async () =>
        {
            await JS.InvokeVoidAsync("eval",
                $"document.documentElement.setAttribute('data-theme','{(isDark ? "dark" : "light")}');" +
                $"document.documentElement.style.background='{(isDark ? "#1a1a1a" : "#ffffff")}';");
        });
    }

    public void Dispose()
    {
        ThemeEvents.SystemThemeChanged -= OnSystemThemeChanged;
    }
}
