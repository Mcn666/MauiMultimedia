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
            var mainPage = new MainPage();
            NavigationPage.SetHasNavigationBar(mainPage, false);
            return new Window(new NavigationPage(mainPage)) { Title = "MauiMultimedia.Shell" };
        }
    }
}
