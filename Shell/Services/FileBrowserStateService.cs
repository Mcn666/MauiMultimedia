using MauiMultimedia.Core.Abstractions;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 保存和恢复文件浏览器的页面状态（当前目录、排序等），
/// 以便从支持库查看页返回时恢复浏览位置。
/// </summary>
public class FileBrowserStateService : IFileNavigationState
{
    public string? CurrentPath { get; set; }
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public string? ViewMode { get; set; }
    public IReadOnlyList<string>? CurrentDirectoryFiles { get; set; }
    public string? CurrentFilePath { get; set; }
    public string? ReturnUrl { get; set; }

    public void Clear()
    {
        CurrentPath = null;
        SortColumn = null;
        SortDirection = null;
        ViewMode = null;
        CurrentDirectoryFiles = null;
        ReturnUrl = null;
    }
}
