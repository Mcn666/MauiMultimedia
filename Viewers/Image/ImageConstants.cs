namespace MauiMultimedia.Viewers.Image;

/// <summary>
/// 图片查看器支持的扩展名常量，统一管理避免散落四处。
/// </summary>
public static class ImageConstants
{
    /// <summary>
    /// 所有支持的图片格式（用于文件列表过滤、查看器注册）。
    /// </summary>
    public static readonly HashSet<string> AllExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".webp",
        ".ico", ".tiff", ".tif", ".svg", ".avif", ".dds"
    };

    /// <summary>
    /// 浏览器可直接原生渲染的格式（无需 SkiaSharp 解码，直出 FileServer URL）。
    /// 不含 TIFF（浏览器不支持）、SVG（走文本通道）、DDS（需手动解码）。
    /// </summary>
    public static readonly HashSet<string> BrowserNative = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".webp", ".bmp", ".ico", ".avif"
    };
}
