using MauiMultimedia.Shell.Models;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 文件系统服务实现，基于 System.IO 枚举目录和文件
/// </summary>
public class FileSystemService : IFileSystemService
{
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
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
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
}
