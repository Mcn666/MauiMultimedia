using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Viewers.Text;

public class TextViewer : IFileViewer
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".csv", ".xml", ".json", ".yaml", ".yml",
        ".cs", ".js", ".ts", ".jsx", ".tsx", ".css", ".scss", ".less",
        ".html", ".htm", ".php", ".py", ".java", ".cpp", ".c", ".h",
        ".sql", ".sh", ".bat", ".ps1", ".rb", ".go", ".rs", ".swift",
        ".ini", ".cfg", ".conf", ".env", ".gitignore", ".dockerfile",
        ".csproj", ".sln", ".slnx", ".props", ".targets"
    };

    public string DisplayName => "文本查看器";

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));

    public string GetViewerRoute(FileSystemItem item) =>
        $"/textviewer?path={Uri.EscapeDataString(item.FullPath)}";
}
