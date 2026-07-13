namespace MauiMultimedia.Shell
{
    using MauiMultimedia.Shell.Services;

    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // 进程启动清扫查看器遗留缓存（崩溃/强杀后残留的 MauiMM_* 目录）。
            // 同步执行：保证在任何 viewer 创建目录之前完成，避免与刚建好的目录产生竞态；
            // 方法内部幂等，整个进程仅真正执行一次。删除少量目录通常很快。
            FileSystemService.CleanupViewerCache();

            // 仅在尚未保存过主题时，用系统主题初始化一次。
            // 关键：不能无条件用系统主题覆盖！用户在 Home 选择的主题会写入 Preferences，
            // 若这里每次启动都覆写，原生侧(MainPage/ViewerPageFactory)读到的就是系统主题而非应用主题，产生冲突。
            var existing = Preferences.Get("filebrowser-theme", "");
            if (existing != "dark" && existing != "light" && existing != "system")
            {
                bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
                Preferences.Set("filebrowser-theme", isDark ? "dark" : "light");
            }

            var mainPage = new MainPage();
            NavigationPage.SetHasNavigationBar(mainPage, false);
            return new Window(new NavigationPage(mainPage)) { Title = "MauiMultimedia.Shell" };
        }
    }
}
