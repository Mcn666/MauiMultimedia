namespace MauiMultimedia.Viewers.Image;

/// <summary>
/// 图片查看器支持的扩展名常量，统一管理避免散落四处。
/// </summary>
public static class ImageConstants
{
    /// <summary>
    /// 所有支持的图片格式（用于文件列表过滤、查看器注册）。
    /// 注意：TIFF 已被移除——SkiaSharp 不包含 TIFF 编解码器，浏览器也不原生支持，
    /// 声明了也打不开（经 TestSamples 解码测试确认）。
    /// </summary>
    public static readonly HashSet<string> AllExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".webp",
        ".ico", ".svg", ".avif", ".dds"
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
