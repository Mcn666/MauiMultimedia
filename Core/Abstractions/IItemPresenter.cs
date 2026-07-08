using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 列表/网格中条目的“展示”职责：是否可处理、图标、CSS 类、扫描分类。
/// 与 <see cref="ISnapshotProvider"/> 分离，使 Shell 只关心展示时无需依赖快照相关成员
/// （符合接口隔离原则）。<see cref="CanHandle"/> 同时声明于两个接口，以便各自可独立用于
/// “为某条目找到对应的提供者”的查找循环。
/// </summary>
public interface IItemPresenter
{
    /// <summary>判断是否可处理该条目</summary>
    bool CanHandle(FileSystemItem item);

    /// <summary>自定义 CSS 类名（可选，返回 null 则无额外样式）</summary>
    string? GetItemCssClass(FileSystemItem item);

    /// <summary>自定义图标（可选，返回 null 则用默认 📁/📄）</summary>
    string? GetIcon(FileSystemItem item);

    /// <summary>文件扫描分类（可选），Shell 用此构建类型筛选面板。返回 null 表示不参与。</summary>
    FileScanCategory? ScanCategory { get; }
}
