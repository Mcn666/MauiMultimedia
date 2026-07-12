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
    /// - AdjustResize 已让窗口收缩：rootH≈rectBottom，keyboardH≈0，不重复收缩；
    /// - AdjustPan / edge-to-edge 未收缩窗口：keyboardH&gt;0，施加 padding 收缩内容。
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
            int keyboardH = rootH - rect.Bottom;
            // 阈值过滤导航栏/状态栏造成的固定误差（无键盘时 keyboardH 可能为导航栏高度）
            keyboardH = keyboardH < 80 ? 0 : keyboardH;

            // 仅变化时才设置，避免 relayout 触发的无限循环
            if (_content.PaddingBottom != keyboardH)
                _content.SetPadding(_content.PaddingLeft, _content.PaddingTop,
                    _content.PaddingRight, keyboardH);
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
