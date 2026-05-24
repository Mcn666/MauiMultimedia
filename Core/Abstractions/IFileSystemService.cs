using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 文件系统服务接口
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// 列出指定路径下的所有文件和文件夹
    /// </summary>
    Task<List<FileSystemItem>> ListItemsAsync(string path);

    /// <summary>
    /// 获取路径的根目录
    /// </summary>
    string GetPathRoot(string path);

    /// <summary>
    /// 获取上级目录
    /// </summary>
    string? GetParentPath(string path);

    /// <summary>
    /// 判断是否为根目录
    /// </summary>
    bool IsRootPath(string path);

    /// <summary>
    /// 获取默认起始路径（桌面）
    /// </summary>
    string GetDefaultPath();

    /// <summary>
    /// 获取指定目录下的直接子项目数量（不递归）
    /// </summary>
    int? TryGetChildCount(string path);

    /// <summary>
    /// 获取应用数据目录路径（MAUI 的 FileSystem.AppDataDirectory），
    /// 各支持库可在此目录下创建自己的子目录存放缓存/临时文件。
    /// </summary>
    string GetAppDataDirectory();
}
