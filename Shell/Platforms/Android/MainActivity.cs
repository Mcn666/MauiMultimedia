using Android.App;
using Android.Content.PM;
using Android.Views;
using AndroidX.Core.View;

namespace MauiMultimedia.Shell;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
    | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
    | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public void SetStatusBarStyle(bool isDark)
    {
        var window = Window;
        if (window == null) return;

        var bgColor = Android.Graphics.Color.ParseColor(isDark ? "#1e1e1e" : "#ffffff");

        // 同步 ContentPage 背景色（API 35+ 状态栏透明后由它透过显示）
        if (Microsoft.Maui.Controls.Application.Current?.Windows.Count > 0)
        {
            var mainPage = Microsoft.Maui.Controls.Application.Current.Windows[0].Page;
            if (mainPage != null)
                mainPage.BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb(
                    isDark ? "#1e1e1e" : "#ffffff");
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            // API 35+：Edge-to-Edge，状态栏透明
            WindowCompat.SetDecorFitsSystemWindows(window, false);
        }
        else
        {
            // API 5~34：传统方式设颜色
            window.ClearFlags(WindowManagerFlags.TranslucentStatus);
            window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);
            window.SetStatusBarColor(bgColor);
        }

        // 图标颜色（API 23+）
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            var controller = new WindowInsetsControllerCompat(window, window.DecorView);
            if (controller != null)
                controller.AppearanceLightStatusBars = !isDark;
        }
    }
}
