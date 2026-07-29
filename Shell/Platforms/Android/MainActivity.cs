using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace MauiMultimedia.Shell;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation
    | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
    | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private bool _imeInsetRegistered;

    /// <summary>
    /// 让软键盘弹出时 WebView 内容收缩到键盘之上（原生层兜底）。
    /// 跨版本键盘检测：GlobalLayout + 可见显示区域（Android &lt; 30 不派发 IME inset，
    /// 故不能依赖 WindowInsetsCompat.Type.Ime()）。把键盘高度作为底部 padding 施加到
    /// content 视图使 WebView 收缩；AdjustResize 已让窗口收缩时 keyboardH≈0 不重复收缩，
    /// AdjustPan / edge-to-edge 未收缩窗口时 keyboardH&gt;0 才收缩内容。
    /// </summary>
    private void EnsureImeInsetHandling()
    {
        if (_imeInsetRegistered) return;
        _imeInsetRegistered = true;

        var window = Window;
        if (window == null) return;

        var content = window.DecorView.FindViewById(Android.Resource.Id.Content);
        if (content != null)
        {
            var gll = new KeyboardGlobalLayoutListener(content);
            var vto = content.ViewTreeObserver;
            if (vto != null)
                vto.AddOnGlobalLayoutListener(gll);
        }
    }

    /// <summary>
    /// 跨版本键盘高度探测：对比根视图高度与可见显示区域底部，得到键盘高度，
    /// 并作为底部 padding 施加到 content，使 WebView 收缩到键盘之上。
    /// - 关键：GetWindowVisibleDisplayFrame 返回的可见区**本身已排除**状态栏与导航栏，
    ///   故需从差值里减掉系统栏真实高度（从资源读取），否则键盘未弹出时
    ///   rootH - rect.Bottom = 状态栏 + 导航栏，会被误当成键盘高度注入 padding，
    ///   与页面 Padding(导航栏) 叠加成「约两倍导航栏」的底部留白。
    /// - 剔除系统栏后：静止 → keyboardH=0；键盘弹出(AdjustResize) → keyboardH=真实键盘高度。
    /// </summary>
    private class KeyboardGlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly Android.Views.View _content;
        public KeyboardGlobalLayoutListener(Android.Views.View content) => _content = content;

        public void OnGlobalLayout()
        {
            var rect = new Android.Graphics.Rect();
            _content.GetWindowVisibleDisplayFrame(rect);
            int rootH = _content.RootView?.Height ?? _content.Height;
            // 减去系统栏真实高度，避免把状态栏+导航栏误算成键盘
            int sysBars = GetSystemBarHeightPx();
            int keyboardH = rootH - rect.Bottom - sysBars;
            if (keyboardH < 0) keyboardH = 0;

            // 仅变化时才设置，避免 relayout 触发的无限循环
            if (_content.PaddingBottom != keyboardH)
                _content.SetPadding(_content.PaddingLeft, _content.PaddingTop,
                    _content.PaddingRight, keyboardH);
        }

        private static int GetSystemBarHeightPx()
        {
            // 仅底部系统栏参与「rootH - rect.Bottom」差值：
            // getWindowVisibleDisplayFrame 的 Bottom 不含顶部状态栏，故不能用 status_bar_height。
            // 直接用实时 WindowInsets 的真实底部 inset（自动按导航模式：三键=导航栏高，手势=0），
            // 与 NavigationInsets 同一数据源，避免「非三键导航多算导航栏」及「多减状态栏」。
            var (_, bottom, _, _) = NavigationInsets.GetSystemBarInsetsPx();
            return bottom;
        }
    }

    public void SetStatusBarStyle(bool isDark)
    {
        EnsureImeInsetHandling();

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
