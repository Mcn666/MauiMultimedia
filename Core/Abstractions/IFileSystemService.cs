using MauiMultimedia.Core.Models;
using System.Text;

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
    /// 获取"查看器可写输出目录"的唯一 sanctioned 出口。
    /// 始终返回 <c>cache/MauiMM_&lt;scope&gt;</c> 形态的路径，天然位于应用私有沙盒内，
    /// 且带 <c>MauiMM_</c> 前缀，会被进程启动时的 <see cref="CleanupViewerCache"/> 自动清扫。
    /// 各查看器（Image DDS 解码、Model3D 转换、Html 抽取、Archive 解压等）都应通过此 API
    /// 取得临时/缓存目录，禁止自行调用 <c>Path.GetTempPath()</c> 或任意绝对路径，
    /// 以防输出逃逸到应用私有目录之外。
    /// </summary>
    /// <param name="scope">查看器/用途标识（如 "DdsDecode"、"ModelConvert"、"Html"、"Archive"），会被清洗掉路径分隔符。</param>
    /// <returns>已创建、保证位于沙盒内的目录绝对路径。</returns>
    string GetScratchDirectory(string scope);

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

    /// <summary>
    /// 尝试以文本形式写入文件。Windows 上直接写入；Android 上优先尝试直接 IO，
    /// 若外部存储无权限则通过 MediaStore / SAF 写入公共目录；iOS 仅沙盒内可写。
    /// 返回 true 表示写入成功，false 表示因权限不足无法写入。
    /// </summary>
    Task<bool> TryWriteTextAsync(string path, string content, Encoding? encoding = null);
}
