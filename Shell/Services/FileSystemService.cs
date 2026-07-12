using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

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
                    LastModified = fileInfo.LastWriteTime
                };
            }
        }
        catch
        {
            return null;
        }
    }

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
            _appDataDir = Path.Combine(Path.GetTempPath(), "MauiMultimedia");
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
                        LastModified = fi.LastWriteTime
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
