namespace MauiMultimedia.Viewers.Html;

/// <summary>
/// HTML 查看器支持的扩展名常量。
/// </summary>
public static class HtmlConstants
{
    public static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".mht", ".mhtml"
    };
}
