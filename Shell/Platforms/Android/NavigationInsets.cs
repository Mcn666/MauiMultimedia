#if ANDROID
using Android.App;
using Android.Content.Res;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.Controls;

namespace MauiMultimedia.Shell;

/// <summary>
/// Android 系统栏 inset 计算（单位 px / dp）。
/// 优先读取实时 WindowInsets（由系统按当前导航模式给出真实 top/bottom）：
///   - 三键导航：底部 = navigation_bar_height（约 48dp）
///   - 双键导航：底部 = 实际底部条高度（约 24~48dp，因 ROM 而异）
///   - 手势导航：底部 = tappableElement 高度（约 20dp，用于避让"回家"手势区）
/// 通过 ViewCompat 的 inset 监听实时把 inset 同步到页面 Padding，修复一次性
/// RootWindowInsets 读取在页面创建早期拿到 0（Android 15+/16 edge-to-edge 下双键导航
/// 底部 inset 分发较晚）而导致底部留白丢失的问题。无 Window 上下文时降级为静态资源读取。
/// </summary>
internal static class NavigationInsets
{
    public static (int top, int bottom, int left, int right) GetSystemBarInsetsPx()
    {
        var real = TryReadWindowInsetsPx();
        if (real.HasValue) return real.Value;
        return FallbackInsetsPx();
    }

    public static (int statusBarDp, int navigationBarDp) GetSystemBarInsetsDp()
    {
        var (t, b, _, _) = GetSystemBarInsetsPx();
        float density = GetDensity();
        return ((int)(t / density), (int)(b / density));
    }

    /// <summary>
    /// 在指定视图上注册 inset 监听，实时把系统栏 inset 同步到页面 Padding。
    /// 注册时会先用当前 RootWindowInsets 设一次初始值，随后每次 inset 分发/变化
    /// （旋转、切换导航模式、软键盘）都更新，避免一次性读取时机过早拿到 0。
    /// </summary>
    public static void AttachInsets(Android.Views.View view, ContentPage page)
    {
        ApplyInsetsToPage(view.RootWindowInsets, page);
        ViewCompat.SetOnApplyWindowInsetsListener(view, new PageInsetsListener(page));
    }

    private static void ApplyInsetsToPage(Android.Views.WindowInsets? platformInsets, ContentPage page)
    {
        if (platformInsets == null) return;
        ApplyInsetsToPage(WindowInsetsCompat.ToWindowInsetsCompat(platformInsets)!, page);
    }

    private static void ApplyInsetsToPage(WindowInsetsCompat insets, ContentPage page)
    {
        var sb = insets.GetInsets(WindowInsetsCompat.Type.SystemBars())!;
        float density = GetDensity();
        page.Padding = new Thickness(0, sb.Top / density, 0, sb.Bottom / density);
    }

    private static (int top, int bottom, int left, int right)? TryReadWindowInsetsPx()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var decor = activity?.Window?.DecorView;
        var insets = decor?.RootWindowInsets;
        if (insets == null) return null;
        var compat = WindowInsetsCompat.ToWindowInsetsCompat(insets)!;
        var sb = compat.GetInsets(WindowInsetsCompat.Type.SystemBars())!;
        return (sb.Top, sb.Bottom, sb.Left, sb.Right);
    }

    private static (int top, int bottom, int left, int right) FallbackInsetsPx()
    {
        int top = 0, bottom = 0, left = 0, right = 0;
        var resources = global::Android.App.Application.Context?.Resources;
        if (resources != null)
        {
            int rid = resources.GetIdentifier("status_bar_height", "dimen", "android");
            if (rid > 0) top = resources.GetDimensionPixelSize(rid);

            // 仅手势导航（mode==2）不预留底部；三键、双键都有可见导航栏，预留 navigation_bar_height
            if (IsButtonNavigation(resources))
            {
                int navRid = resources.GetIdentifier("navigation_bar_height", "dimen", "android");
                if (navRid > 0) bottom = resources.GetDimensionPixelSize(navRid);
            }
        }
        return (top, bottom, left, right);
    }

    private static float GetDensity()
    {
        var resources = global::Android.App.Application.Context?.Resources;
        if (resources?.DisplayMetrics is { } dm)
            return dm.Density;
        return (float)DeviceDisplay.Current.MainDisplayInfo.Density;
    }

    /// <summary>
    /// 是否存在可见的按钮式导航栏（三键或双键）。手势导航（mode==2）返回 false。
    /// 资源不存在（Android 9 之前）时默认按三键处理。
    /// </summary>
    private static bool IsButtonNavigation(Resources resources)
    {
        int modeId = resources.GetIdentifier("config_navBarInteractionMode", "integer", "android");
        if (modeId <= 0) return true;
        int mode = resources.GetInteger(modeId);
        return mode != 2;
    }

    private sealed class PageInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        private readonly ContentPage _page;
        public PageInsetsListener(ContentPage page) => _page = page;
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
        {
            if (insets != null)
                ApplyInsetsToPage(insets, _page);
            return insets; // 不消费，继续向子视图分发
        }
    }
}
#endif
