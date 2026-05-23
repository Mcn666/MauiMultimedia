using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.TextViewer;

public class TextViewer : IFileViewer
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".csv", ".xml", ".json", ".yaml", ".yml",
        ".cs", ".js", ".ts", ".jsx", ".tsx", ".css", ".scss", ".less",
        ".html", ".htm", ".php", ".py", ".java", ".cpp", ".c", ".h",
        ".sql", ".sh", ".bat", ".ps1", ".rb", ".go", ".rs", ".swift",
        ".ini", ".cfg", ".conf", ".env", ".gitignore", ".dockerfile",
        ".csproj", ".sln", ".slnx", ".props", ".targets"
    };

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Extensions.Contains(Path.GetExtension(item.Name));

    public string GetViewerRoute(FileSystemItem item) =>
        $"/textviewer?path={Uri.EscapeDataString(item.FullPath)}";
}
