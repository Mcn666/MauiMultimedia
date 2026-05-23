using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 文件查看器抽象，处理文件点击后的导航/打开行为
/// </summary>
public interface IFileViewer
{
    /// <summary>
    /// 判断是否可处理该文件
    /// </summary>
    bool CanHandle(FileSystemItem item);

    /// <summary>
    /// 获取查看器路由（Shell 通过 NavigationManager 跳转）
    /// </summary>
    string GetViewerRoute(FileSystemItem item);
}
