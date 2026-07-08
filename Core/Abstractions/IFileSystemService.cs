using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 文件系统服务接口
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// 仅列出目录（快速，数量少时 <50ms）
    /// </summary>
    Task<List<FileSystemItem>> ListDirItemsAsync(string path);

    /// <summary>
    /// 仅列出文件（大文件夹慢）
    /// </summary>
    Task<List<FileSystemItem>> ListFileItemsAsync(string path);

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

    /// <summary>
    /// 检查存储权限是否已授予（Android 需要访问外部存储）
    /// </summary>
    Task<bool> CheckStoragePermissionAsync();

    /// <summary>
    /// 请求存储权限（Android 上引导用户至系统设置）
    /// </summary>
    void RequestStoragePermission();

    /// <summary>
    /// 递归扫描目录树，按扩展名过滤文件
    /// </summary>
    Task<List<FileSystemItem>> ScanFilesByTypeAsync(string rootPath, string[] extensions, CancellationToken ct = default);
}
