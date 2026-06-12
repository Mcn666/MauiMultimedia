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
            // 初始化 Preferences，供 MainPage/ViewerPageFactory 启动时读取背景色
            bool isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            Preferences.Set("filebrowser-theme", isDark ? "dark" : "light");

            var mainPage = new MainPage();
            NavigationPage.SetHasNavigationBar(mainPage, false);
            return new Window(new NavigationPage(mainPage)) { Title = "MauiMultimedia.Shell" };
        }
    }
}
