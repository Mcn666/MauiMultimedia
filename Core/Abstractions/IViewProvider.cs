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

    /// <summary>
    /// 获取文件快照 data:URI，仅读缓存，不触发生成（被动查询）
    /// </summary>
    string? GetItemSnapshot(FileSystemItem item);

    /// <summary>
    /// 该文件是否可生成快照（用于 Shell 判断是否显示快照占位符）
    /// </summary>
    bool CanProvideSnapshot(FileSystemItem item);

    /// <summary>
    /// 请求加载文件快照，仅在可视区内调用（触发后台生成）
    /// </summary>
    void RequestItemSnapshot(FileSystemItem item);

    /// <summary>
    /// 新快照生成完成时触发（用于通知 Shell 刷新网格）
    /// </summary>
    event Action? SnapshotsUpdated;
}
