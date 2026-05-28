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
        page.BackgroundColor = isDark ? Color.FromArgb("#1a1a1a") : Colors.White;

        var bwv = new BlazorWebView { HostPage = "wwwroot/viewer.html" };
        bwv.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = viewer.ComponentType
        });

#if ANDROID
        page.HandlerChanged += (_, _) =>
        {
            var resources = Android.App.Application.Context.Resources;
            if (resources != null)
            {
                int rid = resources.GetIdentifier("status_bar_height", "dimen", "android");
                if (rid > 0)
                {
                    int hPx = resources.GetDimensionPixelSize(rid);
                    float density = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
                    int top = (int)(hPx / density);
                    if (top > 0) page.Padding = new Thickness(0, top, 0, 0);
                }
            }

            if (bwv.Handler?.PlatformView is Android.Webkit.WebView nativeWv)
            {
                var dark2 = Preferences.Get("filebrowser-theme", "") switch
                {
                    "dark" => true, "light" => false, _ => Application.Current?.RequestedTheme == AppTheme.Dark
                };
                nativeWv.SetBackgroundColor(Android.Graphics.Color.ParseColor(
                    dark2 ? "#1a1a1a" : "#ffffff"));
            }
        };
#endif

        page.Content = bwv;
        NavigationPage.SetHasNavigationBar(page, false);
        return page;
    }
}
