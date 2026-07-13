using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.IO;
using MauiMultimedia.Core.Models;
using System.Threading;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 文件系统服务实现，基于 System.IO 枚举目录和文件
/// </summary>
public class FileSystemService : IFileSystemService
{
    private string? _userRoot;

    /// <summary>
    /// 获取用户级根目录（不允许返回更上层）。Android 上为外部存储根目录。
    /// </summary>
    private string GetUserRoot()
    {
        if (_userRoot != null) return _userRoot;
#if ANDROID
        _userRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/storage/emulated/0";
#else
        _userRoot = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
#endif
        return _userRoot;
    }
    public Task<List<FileSystemItem>> ListDirItemsAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                return Directory.EnumerateDirectories(path)
                    .Select(dir => SafeCreateItem(dir, isFolder: true))
                    .Where(item => item != null)
                    .OrderBy(item => item!.Name)
                    .Cast<FileSystemItem>()
                    .ToList();
            }
            catch { return new List<FileSystemItem>(); }
        });
    }

    public Task<List<FileSystemItem>> ListFileItemsAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                return Directory.EnumerateFiles(path)
                    .Select(file => SafeCreateItem(file, isFolder: false))
                    .Where(item => item != null)
                    .OrderBy(item => item!.Name)
                    .Cast<FileSystemItem>()
                    .ToList();
            }
            catch { return new List<FileSystemItem>(); }
        });
    }

    private static FileSystemItem? SafeCreateItem(string fullPath, bool isFolder)
    {
        try
        {
            if (isFolder)
            {
                var dirInfo = new DirectoryInfo(fullPath);
                return new FileSystemItem
                {
                    Name = dirInfo.Name,
                    FullPath = dirInfo.FullName,
                    IsFolder = true,
                    LastModified = dirInfo.LastWriteTime
                };
            }
            else
            {
                var fileInfo = new FileInfo(fullPath);
                return new FileSystemItem
                {
                    Name = fileInfo.Name,
                    FullPath = fileInfo.FullName,
                    IsFolder = false,
                    LastModified = fileInfo.LastWriteTime,
                    Size = GetFileSize(fullPath)
                };
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取文件字节数。优先用 FileInfo.Length；若为 0（Android scoped storage /
    /// MediaStore 虚拟文件经 System.IO 取长度常为 0）则回退到打开流读取真实长度，
    /// 在 Android 上再回退到 MediaStore 查询 SIZE 列。任何一步失败都安全降级为 0，
    /// 不抛异常（避免整条文件被 SafeCreateItem 丢弃）。
    /// </summary>
    private static long GetFileSize(string fullPath)
    {
        try
        {
            var fi = new FileInfo(fullPath);
            if (fi.Exists)
            {
                var len = fi.Length;
                if (len > 0) return len; // 仅当 >0 才采用；为 0 时继续走回退
            }
        }
        catch { }

        // 回退 1：直接打开流读取真实长度（部分平台 FileInfo.Length 缓存/返回 0 时更可靠）
        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return fs.Length;
        }
        catch { }

#if ANDROID
        // 回退 2：MediaStore 查询（Android 上部分媒体文件只有此路能拿到真实大小）
        var mediaSize = GetAndroidMediaSize(fullPath);
        if (mediaSize > 0) return mediaSize;
#endif

        return 0;
    }

#if ANDROID
    private static long GetAndroidMediaSize(string path)
    {
        try
        {
            var ctx = Android.App.Application.Context;
            var resolver = ctx?.ContentResolver;
            if (resolver == null) return 0;
            var uri = Android.Provider.MediaStore.Files.GetContentUri("external");
            if (uri == null) return 0;
            var sizeCol = Android.Provider.MediaStore.IMediaColumns.Size;
            var dataCol = Android.Provider.MediaStore.IMediaColumns.Data;
            using var cursor = resolver.Query(uri, new[] { sizeCol }, dataCol + " = ?", new[] { path }, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                int idx = cursor.GetColumnIndex(sizeCol);
                if (idx >= 0) return cursor.GetLong(idx);
            }
        }
        catch { }
        return 0;
    }
#endif

    public string GetPathRoot(string path)
    {
        return Path.GetPathRoot(path) ?? path;
    }

    public string? GetParentPath(string path)
    {
        try
        {
            if (string.Equals(path, GetAppPrivateRoot(), StringComparison.OrdinalIgnoreCase))
                return null;
#if ANDROID
            if (string.Equals(path, GetUserRoot(), StringComparison.OrdinalIgnoreCase))
                return null;
#endif
            var parent = Directory.GetParent(path);
            return parent?.FullName;
        }
        catch
        {
            return null;
        }
    }

    public bool IsRootPath(string path)
    {
        try
        {
            if (string.Equals(path, GetAppPrivateRoot(), StringComparison.OrdinalIgnoreCase))
                return true;
#if ANDROID
            if (string.Equals(path, GetUserRoot(), StringComparison.OrdinalIgnoreCase))
                return true;
#endif
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) &&
                   string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string GetDefaultPath()
    {
        return GetUserRoot();
    }

    public int? TryGetChildCount(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Count();
        }
        catch
        {
            return null;
        }
    }

    private string? _appDataDir;
    private string? _appPrivateRoot;

    public string GetAppDataDirectory()
    {
        if (_appDataDir != null) return _appDataDir;
        try
        {
            _appDataDir = FileSystem.AppDataDirectory;
        }
        catch
        {
            // MAUI FileSystem 不可用时的兜底：退回 LocalApplicationData 下的固定子目录。
            // 该位置仍位于用户/应用空间内（非系统 Temp），避免输出逃逸到应用私有目录之外。
            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MauiMultimedia", "files");
        }
        return _appDataDir;
    }

    public string GetAppPrivateRoot()
    {
        if (_appPrivateRoot != null) return _appPrivateRoot;
        try
        {
            // AppDataDirectory 是包名根目录下的 files 子目录，取父目录即得包名根目录
            var files = GetAppDataDirectory();
            var root = Path.GetDirectoryName(files);
            _appPrivateRoot = string.IsNullOrEmpty(root) ? files : root;
        }
        catch
        {
            _appPrivateRoot = GetAppDataDirectory();
        }
        return _appPrivateRoot;
    }

    public bool IsAppPrivateDirectory(string path)
    {
        var priv = GetAppPrivateRoot();
        if (string.Equals(path, priv, StringComparison.OrdinalIgnoreCase))
            return true;
        // 跨平台分隔符兼容（Windows '\' / 其它平台 '/'）
        return path.StartsWith(priv + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(priv + "/", StringComparison.OrdinalIgnoreCase);
    }

    public string GetCacheDirectory()
    {
        try
        {
            return FileSystem.CacheDirectory;
        }
        catch
        {
            // 极少数环境 MAUI FileSystem 不可用时，退回私有目录下的 Cache 子目录（仍在沙盒内）
            var dir = Path.Combine(GetAppDataDirectory(), "Cache");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }

    public string GetScratchDirectory(string scope)
    {
        // 始终落在 cache 目录下（cache 本身位于应用私有沙盒内），并带 MauiMM_ 前缀
        // 以确保被 CleanupViewerCache 启动清扫覆盖。PathSandbox 做最后一道越界校验。
        var safeScope = PathSandbox.SanitizeScope(scope);
        var dir = Path.Combine(GetCacheDirectory(), "MauiMM_" + safeScope);
        dir = PathSandbox.EnsureWithin(GetCacheDirectory(), dir);
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    private static int _cacheCleaned;

    /// <summary>
    /// 进程启动时一次性清扫查看器遗留的临时/缓存目录。
    /// 这些目录（MauiMM_DdsDecode 图片DDS解码 / MauiMM_Html mhtml抽取 / MauiMM_ModelConvert FBX转换 / MauiMM_Archive 压缩包解压）
    /// 原本仅在正常 DisposeAsync / 页面释放时清理；程序崩溃、被强杀或 OOM 终止时这些路径不执行，
    /// 残留会越积越多。此处在任何 viewer 创建目录之前，删除缓存目录下所有 MauiMM_ 前缀的残留项。
    /// 幂等：整个进程生命周期内仅真正执行一次。
    /// </summary>
    public static void CleanupViewerCache()
    {
        // 仅执行一次（进程内）。Interlocked 保证多线程安全。
        if (Interlocked.Exchange(ref _cacheCleaned, 1) == 1) return;

        string cacheDir;
        try { cacheDir = FileSystem.CacheDirectory; }
        catch { return; }
        if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir)) return;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(cacheDir))
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name)) continue;
                if (!name.StartsWith("MauiMM_", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                        Directory.Delete(entry, recursive: true);
                    else
                        File.Delete(entry);
                }
                catch { /* 单条清理失败不影响其余项与启动 */ }
            }
        }
        catch { /* 枚举失败忽略 */ }

        // 旧版本兼容清理：Archive 查看器曾把解压产物写在 AppData/MauiArchive（持久 data 目录），
        // 该目录不被系统回收、也不在上一段 cache 清扫覆盖范围内。一次性删除，清掉升级前的历史残留
        // （新版 Archive 已改用 cache/MauiMM_Archive，会被上一段自动覆盖）。
        try
        {
            var legacyArchiveDir = Path.Combine(FileSystem.AppDataDirectory, "MauiArchive");
            if (Directory.Exists(legacyArchiveDir))
                Directory.Delete(legacyArchiveDir, recursive: true);
        }
        catch { /* 忽略：无残留或权限问题都不影响启动 */ }
    }

    public async Task<bool> CheckStoragePermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            return Android.OS.Environment.IsExternalStorageManager;

        // Android 10 (API 29)：需要运行时请求 READ_EXTERNAL_STORAGE
        var readStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
        return readStatus == PermissionStatus.Granted;
#else
        return true;
#endif
    }

    public void RequestStoragePermission()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var ctx = Android.App.Application.Context;
            var intent = new Android.Content.Intent(
                Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(Android.Net.Uri.Parse("package:" + ctx.PackageName));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            ctx.StartActivity(intent);
        }
        else
        {
            // Android 10：通过 MAUI Permissions API 请求
            _ = Permissions.RequestAsync<Permissions.StorageRead>();
        }
#endif
    }

    public Task<int[]> CountFilesByTypesAsync(string rootPath, IReadOnlyList<string[]> extensionGroups, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var counts = new int[extensionGroups.Count];
            CountDirByTypes(rootPath, extensionGroups, counts, ct);
            return counts;
        }, ct);
    }

    /// <summary>
    /// 判断目录是否为 reparse point（Windows junction/symlink 或类 Unix 符号链接）。
    /// 递归扫描时必须跳过，否则用户建的循环链接（如文件夹指向其父目录）会导致
    /// 无限递归 → 不可捕获的 StackOverflowException 直接崩进程。
    /// </summary>
    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch { return false; }
    }

    private static void CountDirByTypes(string dir, IReadOnlyList<string[]> extensionGroups, int[] counts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string[] files;
        try
        {
            files = Directory.GetFiles(dir);
        }
        catch { return; }

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(f);
            for (int i = 0; i < extensionGroups.Count; i++)
            {
                if (extensionGroups[i].Length == 0 ||
                    extensionGroups[i].Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    counts[i]++;
                }
            }
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dir);
        }
        catch { return; }

        foreach (var d in subDirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith(".") ||
                string.Equals(name, "Android", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "data", StringComparison.OrdinalIgnoreCase))
                continue;

            // 跳过 junction/符号链接，防止循环链接导致无限递归崩溃
            if (IsReparsePoint(d)) continue;

            CountDirByTypes(d, extensionGroups, counts, ct);
        }
    }

    public Task<List<FileSystemItem>> ScanFilesByTypeAsync(string rootPath, string[] extensions, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var results = new List<FileSystemItem>();
            ScanDir(rootPath, extensions, results, ct);
            return results;
        }, ct);
    }

    private static void ScanDir(string dir, string[] extensions, List<FileSystemItem> results, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string[] files;
        try
        {
            files = Directory.GetFiles(dir);
        }
        catch { return; }

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(f);
            if (extensions.Length == 0 || extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var fi = new FileInfo(f);
                    results.Add(new FileSystemItem
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        IsFolder = false,
                        LastModified = fi.LastWriteTime,
                        Size = GetFileSize(f)
                    });
                }
                catch { }
            }
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dir);
        }
        catch { return; }

        foreach (var d in subDirs)
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith(".") ||
                string.Equals(name, "Android", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "data", StringComparison.OrdinalIgnoreCase))
                continue;

            // 跳过 junction/符号链接，防止循环链接导致无限递归崩溃
            if (IsReparsePoint(d)) continue;

            ScanDir(d, extensions, results, ct);
        }
    }
}
