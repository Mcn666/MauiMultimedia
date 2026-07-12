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
    /// 获取应用数据目录路径（MAUI 的 FileSystem.AppDataDirectory，Android 上为 /data/data/&lt;包名&gt;/files），
    /// 各支持库可在此目录下创建自己的子目录存放缓存/临时文件。
    /// 注意：这是包名根目录下的 files 子目录，并非私有目录根。
    /// </summary>
    string GetAppDataDirectory();

    /// <summary>
    /// 获取应用私有沙盒的根目录（包名根目录，Android 上为 /data/data/&lt;包名&gt;）。
    /// 该目录下包含 files、cache、databases、shared_prefs 等子目录，是应用完全私有的范围。
    /// Home 的"私有目录"入口导航到此，可浏览整个沙盒，而非仅 files 子目录。
    /// </summary>
    string GetAppPrivateRoot();

    /// <summary>
    /// 判断给定路径是否位于应用私有沙盒（包名根目录，含其自身）内；
    /// 用于 Home 区分"设备存储"与"应用私有目录"两个根，控制入口显隐。
    /// </summary>
    bool IsAppPrivateDirectory(string path);

    /// <summary>
    /// 获取应用私有缓存目录（MAUI 的 FileSystem.CacheDirectory）。
    /// 位于应用沙盒内，OS 可在低存储时自动清理；各 viewer 的临时缓存应写入此处，
    /// 避免落到系统 Temp（沙盒外，对其他 app 可见且不会被自动清理）。
    /// </summary>
    string GetCacheDirectory();

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

    /// <summary>
    /// 单次递归扫描目录树，一次性计算多组扩展名的文件数量。
    /// extensionGroups[i] 对应返回值的 int[i]。
    /// 比多次调用 ScanFilesByTypeAsync 高效得多（只遍历一次目录树）。
    /// </summary>
    Task<int[]> CountFilesByTypesAsync(string rootPath, IReadOnlyList<string[]> extensionGroups, CancellationToken ct = default);
}
