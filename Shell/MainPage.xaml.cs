namespace MauiMultimedia.Shell;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        // 读取用户保存的主题色（和 Blazor 写 localStorage 同步到 Preferences 的值）
        var saved = Preferences.Get("filebrowser-theme", "");
        bool dark;
        if (saved == "dark") dark = true;
        else if (saved == "light") dark = false;
        else dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        var bg = dark ? "#1e1e1e" : "#ffffff";
        BackgroundColor = Color.FromArgb(bg);

        // WebView Handler 就绪时立即设置原生背景色（比 Page.OnHandlerChanged 更早）
#if ANDROID
        blazorWebView.HandlerChanged += OnBlazorWebViewHandlerChanged;
#endif
    }

#if ANDROID
    private void OnBlazorWebViewHandlerChanged(object? sender, EventArgs e)
    {
        if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView nativeWv)
        {
            // 强制设置暗色背景，防止 Android WebView 默认白色背景的闪烁
            // HTML 加载后内联脚本会立刻纠正为正确值
            nativeWv.SetBackgroundColor(Android.Graphics.Color.ParseColor("#1e1e1e"));
        }
    }
#endif

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.MauiContext?.Services != null)
        {
            var resources = Android.App.Application.Context.Resources;
            if (resources != null)
            {
                int top = 0, bottom = 0;

                // 状态栏高度（顶部）
                int rid = resources.GetIdentifier("status_bar_height", "dimen", "android");
                if (rid > 0)
                {
                    int hPx = resources.GetDimensionPixelSize(rid);
                    float density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
                    top = (int)(hPx / density);
                }

                // 导航栏高度（底部）
                int navRid = resources.GetIdentifier("navigation_bar_height", "dimen", "android");
                if (navRid > 0)
                {
                    int hPx = resources.GetDimensionPixelSize(navRid);
                    float density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
                    bottom = (int)(hPx / density);
                }

                if (top > 0 || bottom > 0)
                    Padding = new Thickness(0, top, 0, bottom);
            }
        }
#endif
    }
}
