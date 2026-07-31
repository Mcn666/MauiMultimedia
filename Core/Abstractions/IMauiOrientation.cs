namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 跨平台屏幕方向 / 全屏控制。由 Shell 提供平台实现，Blazor 查看器注入使用。
/// 当前用于视频播放器的“强制横屏 + 沉浸式”全屏体验。
/// </summary>
public interface IMauiOrientation
{
    /// <summary>锁定横屏并进入沉浸式（隐藏状态栏 / 导航栏），用于视频全屏播放。</summary>
    Task EnterLandscapeAsync();

    /// <summary>退出横屏锁，恢复传感器方向并恢复系统栏显示。</summary>
    Task ExitLandscapeAsync();
}
