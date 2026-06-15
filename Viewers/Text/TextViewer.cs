using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Text.Pages;

namespace MauiMultimedia.Viewers.Text;

public class TextViewer : IFileViewer
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".csv",
        ".xml", ".json", ".yaml", ".yml",
        ".html", ".htm", ".css", ".js",
        ".py", ".cs",
        ".sh", ".bat", ".ps1",
        ".ini", ".cfg", ".conf", ".env", ".gitignore"
    };

    public string DisplayName => "文本查看器";
    public Type ComponentType => typeof(TextPage);

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));
}
