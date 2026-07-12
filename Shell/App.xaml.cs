namespace MauiMultimedia.Shell
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
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
