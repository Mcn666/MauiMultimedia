using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 根据 IFileViewer 创建查看器 ContentPage（含 BlazorWebView、主题色、Android 状态栏处理）
/// </summary>
public class ViewerPageFactory
{
    public ContentPage CreatePage(IFileViewer viewer, FileSystemItem item)
    {
        var page = new ContentPage { Padding = new Thickness(0) };

        // 主题色背景（防闪白）
        var saved = Preferences.Get("filebrowser-theme", "");
        var isDark = saved == "dark" || (saved != "light" && Application.Current?.RequestedTheme == AppTheme.Dark);
        page.BackgroundColor = isDark ? Color.FromArgb("#1e1e1e") : Colors.White;

        var bwv = new BlazorWebView { HostPage = "wwwroot/viewer.html" };
        bwv.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = viewer.ComponentType
        });

        // 配置各平台 WebView
        page.HandlerChanged += (_, _) =>
        {
#if ANDROID
            SetupAndroid(bwv, page, isDark);
#endif
        };

        page.Content = bwv;
        NavigationPage.SetHasNavigationBar(page, false);
        NavigationPage.SetHasBackButton(page, false);
        return page;
    }

#if ANDROID
    private static void SetupAndroid(BlazorWebView bwv, ContentPage page, bool isDark)
    {
        // 初始 inset（兜底）。实时值由 AttachInsets 的 inset 监听同步，避免一次性读取时机过早拿到 0。
        var (top, bottom) = NavigationInsets.GetSystemBarInsetsDp();
        if (top > 0 || bottom > 0)
            page.Padding = new Thickness(0, top, 0, bottom);

        if (bwv.Handler?.PlatformView is Android.Webkit.WebView nativeWv)
        {
            // 主题色背景
            nativeWv.SetBackgroundColor(Android.Graphics.Color.ParseColor(
                isDark ? "#1e1e1e" : "#ffffff"));

            // 实时监听系统栏 inset，同步到页面 Padding（修复 Android 16 双键导航底部留白丢失）
            NavigationInsets.AttachInsets(nativeWv, page);

            // 允许混合内容（HTTP 视频从 HTTPS 页面加载）
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Lollipop)
            {
                nativeWv.Settings.MixedContentMode =
                    Android.Webkit.MixedContentHandling.AlwaysAllow;
            }
        }
    }
#endif
}
