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

        var bg = dark ? "#1a1a1a" : "#ffffff";
        BackgroundColor = Color.FromArgb(bg);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.MauiContext?.Services != null)
        {
            var resources = Android.App.Application.Context.Resources;
            if (resources != null)
            {
                int resourceId = resources.GetIdentifier("status_bar_height", "dimen", "android");
                if (resourceId > 0)
                {
                    int statusBarHeightPx = resources.GetDimensionPixelSize(resourceId);
                    float density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
                    int topPadding = (int)(statusBarHeightPx / density);
                    if (topPadding > 0)
                        Padding = new Thickness(0, topPadding, 0, 0);
                }
            }

            // 设置 Android 原生 WebView 背景色（HTML 加载前就生效）
            if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView nativeWv)
            {
                var saved = Preferences.Get("filebrowser-theme", "");
                bool dark;
                if (saved == "dark") dark = true;
                else if (saved == "light") dark = false;
                else dark = Application.Current?.RequestedTheme == AppTheme.Dark;

                nativeWv.SetBackgroundColor(Android.Graphics.Color.ParseColor(
                    dark ? "#1a1a1a" : "#ffffff"));
            }
        }
#endif
    }
}
