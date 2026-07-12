using MauiMultimedia.Core.Models;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
    private int[]? _scanCategoryCounts;
    private bool _scanCountsLoading;
    private string? _activeFilePath;
    // 多级父目录滚动位置栈：进入子文件夹时入栈，返回时出栈恢复
    private readonly Stack<double> _parentScrollStack = new();
    private bool _skipRender;
    // 导航重入锁：防止快速连点导致多个 LoadItemsAsync 并发改写共享 items 列表（状态撕裂）。
    private readonly SemaphoreSlim _navLock = new(1, 1);

    private enum WindowsQuickAccess
    {
        Desktop, Downloads, Documents, Pictures, Music, Videos
    }

    private async Task NavigateToQuickAccess(WindowsQuickAccess qa)
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
        await LoadItemsAsync();
    }

    private string GetItemIcon(FileSystemItem item)
    {
        if (item.IsFolder) return "\U0001F4C1";
        foreach (var p in ItemPresenters)
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
            "var el=document.querySelector('.alias-input');if(el)el.focus()");
    }

    private async Task SaveParentState()
    {
        try
        {
            var scrollY = await JS.InvokeAsync<double>("eval", "window.scrollY");
            _parentScrollStack.Push(scrollY);
        }
        catch { }
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
        foreach (var p in SnapshotProviders)
            if (p.CanHandle(item)) return p.GetItemSnapshot(item);
        return null;
    }

    private bool CanProvideSnapshot(FileSystemItem item)
    {
        foreach (var p in SnapshotProviders)
            if (p.CanHandle(item) && p.CanProvideSnapshot(item)) return true;
        return false;
    }

    /// <summary>
    /// 解析负责该文件快照的查看器程序集与方法名，供网格数据驱动地触发快照，
    /// 使 Shell 无需硬编码扩展名→程序集路由（零侵入）。
    /// </summary>
    private (string Assembly, string Method) GetSnapshotInvocation(FileSystemItem item)
    {
        foreach (var p in SnapshotProviders)
            if (p.CanHandle(item) && p.CanProvideSnapshot(item))
                return (p.SnapshotAssembly, p.SnapshotMethod);
        return (string.Empty, string.Empty);
    }

    private string GetItemRowClass(FileSystemItem item)
    {
        var cls = item.IsFolder ? "folder" : "file";
        foreach (var p in ItemPresenters)
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
        scanCategories = ItemPresenters
            .Select(p => p.ScanCategory)
            .Where(c => c != null)
            .Select(c => c!)
            .ToList();

        // 在加载前恢复排序和视图模式（JS interop 在 MAUI 中 OnInitializedAsync 可用）
        await RestoreSortAsync();
        await RestoreViewModeAsync();

        await LoadItemsAsync();
    }

    protected override bool ShouldRender()
    {
        if (_skipRender) { _skipRender = false; return false; }
        return true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // 1. 从 localStorage 恢复用户保存的模式（"light"/"dark"/"system"）
            await RestoreThemeModeAsync();

            // 2. 根据用户模式应用主题：
            //    "system" → 用 Application.Current.RequestedTheme
            //    "light"/"dark" → 强制使用用户的选择
            bool isDark = themeMode == "system"
                ? Application.Current?.RequestedTheme == AppTheme.Dark
                : themeMode == "dark";
            await ApplyThemeDirect(isDark);

            // 3. 订阅运行时系统主题切换
            if (Application.Current != null)
                Application.Current.RequestedThemeChanged += OnSystemThemeChanged;

            StateHasChanged();

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
                    ? items.OrderBy(i => i.Name, MauiMultimedia.Core.Utils.NaturalSortComparer.Instance).ToList()
                    : items.OrderByDescending(i => i.Name, MauiMultimedia.Core.Utils.NaturalSortComparer.Instance).ToList();
                break;
            case "type":
                items = (sortDirection == "asc")
                    ? items.OrderBy(i => i.IsFolder ? 0 : 1)
                        .ThenBy(i => i.IsFolder ? "" : Path.GetExtension(i.Name), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.Name, MauiMultimedia.Core.Utils.NaturalSortComparer.Instance)
                        .ToList()
                    : items.OrderBy(i => i.IsFolder ? 1 : 0)
                        .ThenBy(i => Path.GetExtension(i.Name), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.Name, MauiMultimedia.Core.Utils.NaturalSortComparer.Instance)
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

    // ────────── 主题管理（基于文章方案：Blazor 直接读 Application.Current.RequestedTheme） ──────────

    /// <summary>直接应用暗/亮主题到 HTML 和状态栏（不经过 mode 判断）</summary>
    private async Task ApplyThemeDirect(bool isDark)
    {
        await JS.InvokeVoidAsync("eval",
            $"document.documentElement.setAttribute('data-theme','{(isDark ? "dark" : "light")}');" +
            $"document.documentElement.style.background='{(isDark ? "#1e1e1e" : "#ffffff")}';" +
            $"localStorage.setItem('filebrowser-theme-resolved','{(isDark ? "dark" : "light")}');");

        // 同步 Android 状态栏
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is MauiMultimedia.Shell.MainActivity ma)
            ma.SetStatusBarStyle(isDark);
#endif
    }

    /// <summary>从 localStorage 恢复用户选中的模式（仅用于下拉菜单显示）</summary>
    private async Task RestoreThemeModeAsync()
    {
        var saved = await JS.InvokeAsync<string>("eval", "localStorage.getItem('filebrowser-theme')");
        themeMode = (saved == "light" || saved == "dark" || saved == "system") ? saved : "system";
    }

    /// <summary>运行时系统主题切换</summary>
    private void OnSystemThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        // 只在"跟随系统"模式下响应，不覆盖用户的显式选择
        if (themeMode != "system") return;

        var isDark = e.RequestedTheme == AppTheme.Dark;
        _ = InvokeAsync(async () =>
        {
            await ApplyThemeDirect(isDark);
        });
    }

    /// <summary>用户从下拉菜单选择浅色/深色/跟随系统</summary>
    private async Task SetTheme(string mode)
    {
        themeMode = mode;

        bool isDark = mode == "system"
            ? Application.Current?.RequestedTheme == AppTheme.Dark  // "跟随系统" → 用原生 API
            : mode == "dark";

        await ApplyThemeDirect(isDark);
        await JS.InvokeVoidAsync("eval", $"localStorage.setItem('filebrowser-theme','{mode}')");
        // 同步写入 Preferences：原生侧(MainPage/ViewerPageFactory 启动背景、状态栏)以 Preferences 为唯一主题来源，
        // 必须与前端 localStorage 保持同一"应用主题"，否则会被 App 启动时的系统主题覆盖而冲突。
        Preferences.Set("filebrowser-theme", mode);
    }

    private Task SetThemeLight() => SetTheme("light");
    private Task SetThemeDark() => SetTheme("dark");
    private Task SetThemeSystem() => SetTheme("system");

    private async Task LoadItemsAsync()
    {
        await _navLock.WaitAsync();
        try
        {
        isLoading = true;
        errorMessage = null;
        isRoot = FileSystemService.IsRootPath(currentPath);

        try
        {
            // Phase 1：加载目录（快速，<50ms）→ 立即显示
            var dirs = await FileSystemService.ListDirItemsAsync(currentPath);
            await InvokeAsync(() =>
            {
                items = dirs;
                isLoading = false;       // 先释放加载状态，用户看到目录列表
                StateHasChanged();
            });

            // Phase 2：加载文件（慢，1-3 秒）→ 追加到目录后，排序后显示
            var files = await FileSystemService.ListFileItemsAsync(currentPath);
            await InvokeAsync(() =>
            {
                items.AddRange(files);
                ApplySort();
                StateHasChanged();
            });

            // 重置滚动位置（子文件夹/新导航都滚到顶部，父目录恢复由 GoBack 负责）
            await JS.InvokeVoidAsync("eval",
                "requestAnimationFrame(() => window.scrollTo(0,0))");

            // Phase 3：子文件夹计数（后台不阻塞）
            _ = LoadChildCountsAsync(items);
        }
        catch (Exception ex)
        {
            await InvokeAsync(() =>
            {
                errorMessage = $"加载目录失败：{ex.Message}";
                items = new List<FileSystemItem>();
                isLoading = false;
                StateHasChanged();
            });
        }
        finally
        {
            await InvokeAsync(() =>
            {
                isLoading = false;
                StateHasChanged();
            });
        }
        }
        finally
        {
            _navLock.Release();
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

    private async Task OnItemClick(FileSystemItem item)
    {
        if (item.IsFolder)
        {
            await SaveParentState();
            currentPath = item.FullPath;
            await LoadItemsAsync();
            return;
        }

        var viewers = FileViewers.Where(v => v.CanHandle(item)).ToList();

        if (viewers.Count == 1)
        {
            await NavigateToViewer(viewers[0], item);
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
        {
            await JS.InvokeVoidAsync("eval",
                "document.querySelector('.browser-main').classList.add('panel-open')");
            // 在后台计算各类别的文件数量（一次遍历）
            _ = RefreshScanCountsAsync();
        }
    }
    private async Task CloseScanPanel()
    {
        showScanPanel = false;
        _scanCategoryCounts = null;
        _scanCountsLoading = false;
        await JS.InvokeVoidAsync("eval",
            "document.querySelector('.browser-main').classList.remove('panel-open')");
    }

    /// <summary>
    /// 单次递归遍历当前目录，为每个筛选类别计算匹配的文件数量。
    /// </summary>
    private async Task RefreshScanCountsAsync()
    {
        if (scanCategories.Count == 0) return;

        _scanCountsLoading = true;
        _scanCategoryCounts = null;
        try
        {
            var extensionGroups = scanCategories.Select(c => c.Extensions).ToArray();
            var counts = await FileSystemService.CountFilesByTypesAsync(currentPath, extensionGroups);
            _scanCategoryCounts = counts;
        }
        catch
        {
            // 静默失败，面板继续显示格式计数即可
        }
        finally
        {
            _scanCountsLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ExitScan()
    {
        isScanned = false;
        isScanning = false;
        _scanCategoryCounts = null;
        _scanCountsLoading = false;
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

            // 出栈恢复父目录滚动位置
            if (_parentScrollStack.TryPop(out var scrollY))
            {
                await JS.InvokeVoidAsync("eval",
                    $"requestAnimationFrame(() => window.scrollTo(0, {scrollY}))");
            }
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
        _skipRender = true;
        viewMode = (viewMode == "list") ? "grid" : "list";
        await JS.InvokeVoidAsync("eval", $@"
            var g = document.querySelector('.browser-grid');
            var l = document.querySelector('.browser-items');
            if (g) g.style.display = {(viewMode == "grid" ? "''" : "'none'")};
            if (l) l.style.display = {(viewMode == "list" ? "''" : "'none'")};
            localStorage.setItem('filebrowser-view', '{(viewMode == "grid" ? "grid" : "list")}');
        ");
    }

    // ────────── 运行时系统主题变更 ──────────

}

