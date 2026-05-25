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
    public Task<List<FileSystemItem>> ListItemsAsync(string path)
    {
        var items = new List<FileSystemItem>();

        try
        {
            // 枚举并排序目录
            var dirs = Directory.EnumerateDirectories(path)
                .Select(dir => SafeCreateItem(dir, isFolder: true))
                .Where(item => item != null)
                .OrderBy(item => item!.Name)
                .Cast<FileSystemItem>()
                .ToList();

            items.AddRange(dirs);

            // 枚举并排序文件
            var files = Directory.EnumerateFiles(path)
                .Select(file => SafeCreateItem(file, isFolder: false))
                .Where(item => item != null)
                .OrderBy(item => item!.Name)
                .Cast<FileSystemItem>()
                .ToList();

            items.AddRange(files);
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (PathTooLongException) { }
        catch (IOException) { }

        return Task.FromResult(items);
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

    public string GetAppDataDirectory()
    {
        try
        {
            return FileSystem.AppDataDirectory;
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "MauiMultimedia");
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

            ScanDir(d, extensions, results, ct);
        }
    }
}
