#if ANDROID
using Android.App;
using Android.Content.Res;

namespace MauiMultimedia.Shell;

/// <summary>
/// Android 系统栏 inset 计算（单位 dp）。
/// 关键：区分系统导航模式。仅「三键导航」（config_navBarInteractionMode == 0）
/// 才预留底部导航栏高度；手势/双键导航（1、2）不占高度，忽略。
/// 这是修复「非三键导航栏仍被加底部留白」的核心逻辑。
/// </summary>
internal static class NavigationInsets
{
    public static (int statusBarDp, int navigationBarDp) GetSystemBarInsetsDp()
    {
        int top = 0, bottom = 0;
        var resources = global::Android.App.Application.Context?.Resources;
        if (resources != null)
        {
            float density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;

            // 状态栏高度（顶部，任何模式都需预留）
            int rid = resources.GetIdentifier("status_bar_height", "dimen", "android");
            if (rid > 0)
                top = (int)(resources.GetDimensionPixelSize(rid) / density);

            // 导航栏高度（底部）：仅三键导航才预留
            if (IsThreeKeyNavigation(resources))
            {
                int navRid = resources.GetIdentifier("navigation_bar_height", "dimen", "android");
                if (navRid > 0)
                    bottom = (int)(resources.GetDimensionPixelSize(navRid) / density);
            }
        }
        return (top, bottom);
    }

    /// <summary>
    /// 当前是否为三键（经典）导航模式。
    /// config_navBarInteractionMode：0=三键，1=双键，2=手势（全屏）。
    /// 资源不存在（Android 9 之前）时默认按三键处理。
    /// </summary>
    private static bool IsThreeKeyNavigation(Resources resources)
    {
        int modeId = resources.GetIdentifier("config_navBarInteractionMode", "integer", "android");
        if (modeId <= 0) return true;
        int mode = resources.GetInteger(modeId);
        return mode == 0;
    }
}
#endif
