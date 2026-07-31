using MauiMultimedia.Core.Abstractions;

#if ANDROID
using Android.App;
using AndroidX.Core.View;
#endif

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 屏幕方向 / 全屏的 MAUI 原生实现。
/// - Android：RequestedOrientation=Landscape + 沉浸式隐藏状态/导航栏（与 ViewerPageFactory 的
///   NavigationInsets 监听协同，隐藏后 inset 归零会自动把 ContentPage.Padding 清掉，视频铺满）。
/// - iOS：尝试 programmatic 锁横屏；若系统拒绝（iOS 对强制旋转限制较严），则降级为
///   允许物理旋转（Info.plist 已声明支持 Landscape，用户转手机即可横屏）。
/// - 其它平台：no-op。
/// </summary>
public class MauiOrientationService : IMauiOrientation
{
    public Task EnterLandscapeAsync()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as Activity;
        if (activity?.Window != null)
        {
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Landscape;
            var decor = activity.Window.DecorView;
            var controller = new WindowInsetsControllerCompat(activity.Window, decor);
            controller.Hide(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.NavigationBars());
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }
#elif IOS
        SetIosOrientation(UIKit.UIInterfaceOrientation.LandscapeRight);
#endif
        return Task.CompletedTask;
    }

    public Task ExitLandscapeAsync()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as Activity;
        if (activity?.Window != null)
        {
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.Unspecified;
            var decor = activity.Window.DecorView;
            var controller = new WindowInsetsControllerCompat(activity.Window, decor);
            controller.Show(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.NavigationBars());
        }
#elif IOS
        SetIosOrientation(UIKit.UIInterfaceOrientation.Portrait);
#endif
        return Task.CompletedTask;
    }

#if IOS
    private static void SetIosOrientation(UIKit.UIInterfaceOrientation orientation)
    {
#pragma warning disable CA1416
        try
        {
            UIKit.UIDevice.CurrentDevice?.SetOrientation(orientation);
        }
        catch { }
#pragma warning restore CA1416
    }
#endif
}
