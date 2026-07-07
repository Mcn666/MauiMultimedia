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

    /// <summary>
    /// 文件扫描分类（可选），Shell 用此构建类型筛选面板。返回 null 表示不参与。
    /// </summary>
    FileScanCategory? ScanCategory { get; }

    /// <summary>
    /// 快照 JSInvokable 所在程序集名（简单名）。Shell 网格据此数据驱动地触发快照，
    /// 避免硬编码扩展名→程序集路由（保持 Shell 通用、零侵入）。默认空表示不提供快照。
    /// </summary>
    string SnapshotAssembly => string.Empty;

    /// <summary>
    /// 快照 JSInvokable 方法名。默认空。各查看器通常在自己的程序集内以
    /// "generateSnapshot" 暴露，按程序集名区分彼此。
    /// </summary>
    string SnapshotMethod => string.Empty;
}
