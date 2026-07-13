namespace MauiMultimedia.Shell;

public partial class MainPage : ContentPage
{
    // 当前主题背景色(hex)，构造解析，供 WebView 原生背景复用（跟随应用主题，不写死）
    private string _bg = "#1e1e1e";

    // Windows WebView2 虚拟主机映射，供 Blazor 组件（如 Model3DPage）调用
#if WINDOWS
    private static Microsoft.Web.WebView2.Core.CoreWebView2? _coreWv2;
    private static int _hostCounter;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _hostMappings = new();
#endif

    public MainPage()
    {
        InitializeComponent();

        // 读取用户保存的主题色（和 Blazor 写 localStorage 同步到 Preferences 的值）
        var saved = Preferences.Get("filebrowser-theme", "");
        bool dark;
        if (saved == "dark") dark = true;
        else if (saved == "light") dark = false;
        else dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        _bg = dark ? "#1e1e1e" : "#ffffff";
        BackgroundColor = Color.FromArgb(_bg);

        // WebView Handler 就绪时设置原生背景色 + Windows 虚拟主机映射
        blazorWebView.HandlerChanged += OnBlazorWebViewHandlerChanged;
    }

    private void OnBlazorWebViewHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView nativeWv)
        {
            nativeWv.SetBackgroundColor(Android.Graphics.Color.ParseColor(_bg));
        }
#elif WINDOWS
        if (blazorWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
        {
            _ = InitializeHostMappingAsync(wv2);
        }
#endif
    }

#if WINDOWS
    private static async Task InitializeHostMappingAsync(Microsoft.UI.Xaml.Controls.WebView2 wv2)
    {
        try
        {
            await wv2.EnsureCoreWebView2Async();
            _coreWv2 = wv2.CoreWebView2;
        }
        catch { }
    }

    /// <summary>
    /// 将本地目录映射为虚拟主机名称，返回 https://{hostname}/ 基 URL。
    /// Blazor 页面本身是 HTTPS，映射后的资源也是 HTTPS → 无 Mixed Content。
    /// 调用方应在 Dispose 时调用 UnmapVirtualHost 注销。
    /// </summary>
    public static string? MapDirectoryToVirtualHost(string directoryPath)
    {
        if (_coreWv2 == null || !Directory.Exists(directoryPath)) return null;
        var host = $"models-{Interlocked.Increment(ref _hostCounter)}.local";
        try
        {
            _coreWv2.SetVirtualHostNameToFolderMapping(
                host,
                directoryPath,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            _hostMappings[host] = directoryPath;
            return $"https://{host}/";
        }
        catch { return null; }
    }

    public static void UnmapVirtualHost(string host)
    {
        if (_coreWv2 == null || !_hostMappings.TryRemove(host, out _)) return;
        try { _coreWv2.ClearVirtualHostNameToFolderMapping(host); }
        catch { }
    }
#endif

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.MauiContext?.Services != null)
        {
            var (top, bottom) = NavigationInsets.GetSystemBarInsetsDp();
            if (top > 0 || bottom > 0)
                Padding = new Thickness(0, top, 0, bottom);
        }
#endif
    }
}
