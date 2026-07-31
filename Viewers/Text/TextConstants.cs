namespace MauiMultimedia.Viewers.Text;

/// <summary>
/// 文本查看器支持的扩展名常量。
/// </summary>
public static class TextConstants
{
    public static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".csv",
        ".xml", ".json", ".yaml", ".yml",
        ".html", ".htm", ".css", ".js",
        ".py", ".cs",
        ".sh", ".bat", ".ps1",
        ".ini", ".cfg", ".conf", ".env", ".gitignore"
    };
}
