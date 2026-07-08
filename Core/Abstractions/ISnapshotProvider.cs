using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 条目快照（缩略图）职责：被动查询缓存、是否可生成、请求生成、生成完成事件，
/// 以及供 Shell 数据驱动地触发快照的程序集/方法标识。
/// 与 <see cref="IItemPresenter"/> 分离，使只关心快照的消费者（如网格缩略图加载器）
/// 不必依赖展示相关成员（符合接口隔离原则）。
/// <see cref="CanHandle"/> 同时声明于两个接口，以便各自可独立用于查找循环。
/// </summary>
public interface ISnapshotProvider
{
    /// <summary>判断是否可处理该条目</summary>
    bool CanHandle(FileSystemItem item);

    /// <summary>获取文件快照 data:URI，仅读缓存，不触发生成（被动查询）</summary>
    string? GetItemSnapshot(FileSystemItem item);

    /// <summary>该文件是否可生成快照（用于 Shell 判断是否显示快照占位符）</summary>
    bool CanProvideSnapshot(FileSystemItem item);

    /// <summary>请求加载文件快照，仅在可视区内调用（触发后台生成）</summary>
    void RequestItemSnapshot(FileSystemItem item);

    /// <summary>新快照生成完成时触发（用于通知 Shell 刷新网格）</summary>
    event Action? SnapshotsUpdated;

    /// <summary>快照 JSInvokable 所在程序集名（简单名）。默认空表示不提供快照。</summary>
    string SnapshotAssembly => string.Empty;

    /// <summary>快照 JSInvokable 方法名。默认空。</summary>
    string SnapshotMethod => string.Empty;
}
