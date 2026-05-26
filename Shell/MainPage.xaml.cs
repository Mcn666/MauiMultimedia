namespace MauiMultimedia.Shell;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
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
        }
#endif
    }
}
