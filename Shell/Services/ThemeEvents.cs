namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 系统主题变更事件通道。
/// App.xaml.cs 在 RequestedThemeChanged 中触发，
/// Blazor 组件订阅后更新 HTML data-theme。
/// </summary>
public static class ThemeEvents
{
    public static event Action<bool>? SystemThemeChanged;

    internal static void NotifyThemeChanged(bool isDark)
    {
        SystemThemeChanged?.Invoke(isDark);
    }
}
