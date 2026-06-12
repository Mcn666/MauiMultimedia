using MauiMultimedia.Shell.Services;

namespace MauiMultimedia.Shell
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            // 订阅系统主题切换事件（运行时生效，如用户在设置中切换暗/亮模式）
            RequestedThemeChanged += OnRequestedThemeChanged;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // CreateWindow 阶段 Application.Current 已就绪，RequestedTheme 正确可用
            // 初始化 Preferences 解析值，供 MainPage/ViewerPageFactory/Home 读取
            bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            Preferences.Set("filebrowser-theme", isDark ? "dark" : "light");

            var mainPage = new MainPage();
            NavigationPage.SetHasNavigationBar(mainPage, false);
            return new Window(new NavigationPage(mainPage)) { Title = "MauiMultimedia.Shell" };
        }

        private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        {
            ApplyTheme(e.RequestedTheme);
        }

        /// <summary>
        /// 运行时系统主题切换。
        /// 将解析后的暗/亮值存入 Preferences（供 native 代码和 Blazor 读取），
        /// 通知 Blazor 组件更新 HTML data-theme，同步 Android 状态栏。
        /// </summary>
        private void ApplyTheme(AppTheme theme)
        {
            bool isDark = theme == AppTheme.Dark;
            var themeStr = isDark ? "dark" : "light";

            // 1. 更新 Preferences（解析值，供 MainPage/ViewerPageFactory 和 Blazor 读取）
            Preferences.Set("filebrowser-theme", themeStr);

            // 2. 通知已订阅的 Blazor 组件更新 HTML data-theme
            ThemeEvents.NotifyThemeChanged(isDark);

            // 3. 同步 Android 状态栏
#if ANDROID
            if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is MauiMultimedia.Shell.MainActivity ma)
                ma.SetStatusBarStyle(isDark);
#endif
        }
    }
}
