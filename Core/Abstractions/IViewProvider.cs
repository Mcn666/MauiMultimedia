using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 视图提供者抽象，允许文件支持库自定义列表/网格中的显示
/// </summary>
public interface IViewProvider
{
    /// <summary>
    /// 判断是否可处理该条目
    /// </summary>
    bool CanHandle(FileSystemItem item);

    /// <summary>
    /// 自定义 CSS 类名（可选，返回 null 则无额外样式）
    /// </summary>
    string? GetItemCssClass(FileSystemItem item);

    /// <summary>
    /// 自定义图标（可选，返回 null 则用默认 📁/📄）
    /// </summary>
    string? GetIcon(FileSystemItem item);
}
