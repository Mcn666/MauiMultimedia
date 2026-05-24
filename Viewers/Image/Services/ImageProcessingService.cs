using SkiaSharp;

namespace MauiMultimedia.Viewers.Image.Services;

/// <summary>
/// SkiaSharp 图片处理服务：解码、EXIF 校正、生成 data:URI
/// </summary>
public static class ImageProcessingService
{
    public readonly record struct ImageResult(
        string DataUri,
        int Width,
        int Height,
        long FileSize,
        string Format
    );

    /// <summary>
    /// 解码图片并生成 data:URI。
    /// 自动处理 EXIF Orientation，超大图片做下采样。
    /// 
    /// 优化策略：
    ///   - 无需缩放 + 无需 EXIF 校正 → 直接读取原始文件字节（零 SkiaSharp 开销）
    ///   - 需要缩放或 EXIF 校正 → SkiaSharp 解码处理
    /// </summary>
    public static ImageResult DecodeImage(string filePath, int maxDimension = 4000)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("文件不存在", filePath);

        var ext = Path.GetExtension(filePath);

        // SVG 走文本读取
        if (string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase))
            return DecodeSvg(filePath, fileInfo.Length);

        using var codec = SKCodec.Create(filePath);
        if (codec == null)
            throw new InvalidOperationException("无法解码图片");

        var origin = codec.EncodedOrigin;
        bool needsDownscale = codec.Info.Width > maxDimension || codec.Info.Height > maxDimension;
        bool needsExifFix = origin != SKEncodedOrigin.TopLeft;

        // ── 快速路径：无需缩放且无需 EXIF 校正 → 零解码 ──
        if (!needsDownscale && !needsExifFix)
        {
            var rawBytes = File.ReadAllBytes(filePath);
            var base64 = Convert.ToBase64String(rawBytes);
            var mime = GetMimeType(ext);
            var dataUri = $"data:{mime};base64,{base64}";
            var fmtName = ext.TrimStart('.').ToUpperInvariant();
            return new ImageResult(dataUri, codec.Info.Width, codec.Info.Height,
                fileInfo.Length, fmtName);
        }

        // ── 慢速路径：需要缩放或 EXIF 校正 → SkiaSharp 解码 ──
        else
        {
            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap == null)
                throw new InvalidOperationException("无法解码位图");

            using var orientedBitmap = ApplyOrientation(bitmap, origin);
            using var displayBitmap = Downscale(orientedBitmap, maxDimension);

            bool hasAlpha = displayBitmap.AlphaType != SKAlphaType.Opaque;
            var encFormat = hasAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
            int quality = hasAlpha ? 100 : 90;

            using var image = SKImage.FromBitmap(displayBitmap);
            using var encoded = image.Encode(encFormat, quality);
            var base64Encoded = Convert.ToBase64String(encoded.ToArray());
            var mimeEncoded = hasAlpha ? "image/png" : "image/jpeg";
            var dataUriEncoded = $"data:{mimeEncoded};base64,{base64Encoded}";
            var fmtName = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();

            return new ImageResult(dataUriEncoded, displayBitmap.Width, displayBitmap.Height,
                fileInfo.Length, fmtName);
        }
    }

    /// <summary>
    /// 读取图片原始尺寸（考虑 EXIF 方向）
    /// </summary>
    public static (int width, int height) GetImageDimensions(string filePath)
    {
        using var codec = SKCodec.Create(filePath);
        if (codec == null) return (0, 0);

        var origin = codec.EncodedOrigin;
        bool swap = origin == SKEncodedOrigin.LeftBottom ||
                    origin == SKEncodedOrigin.RightTop;
        return swap
            ? (codec.Info.Height, codec.Info.Width)
            : (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>
    /// 格式化为人类可读的文件大小
    /// </summary>
    public static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    /// <summary>
    /// 生成缩略图 data:URI（网格快照用）。尺寸小、快速加载。
    /// 
    /// 优化：通过 codec.GetScaledDimensions 让解码器按目标尺寸
    /// 直接解码，避免先解码完整大图再缩放的内存开销。
    /// </summary>
    public static string GenerateThumbnail(string filePath, int maxSize = 180)
    {
        using var codec = SKCodec.Create(filePath);
        if (codec == null) return "";
        var origin = codec.EncodedOrigin;

        int origW = codec.Info.Width;
        int origH = codec.Info.Height;

        // 计算目标缩略尺寸
        float scale = Math.Min(maxSize / (float)origW, maxSize / (float)origH);
        scale = Math.Min(scale, 1f); // 不放大

        // 让 codec 给出它原生支持的最佳缩放尺寸
        var scaled = codec.GetScaledDimensions(scale);
        int decodeW = Math.Max(1, scaled.Width);
        int decodeH = Math.Max(1, scaled.Height);

        // 按缩放尺寸分配缓冲区，一次性解码到目标大小
        var info = new SKImageInfo(decodeW, decodeH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());

        // GetPixels 可能返回 Success 或 IncompleteInput（对完整文件都是 Success）
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            return "";

        // EXIF 方向校正
        using var oriented = ApplyOrientation(bitmap, origin);

        // 如果 codec 缩放后仍略大于目标，再缩一次
        using var final = (oriented.Width > maxSize || oriented.Height > maxSize)
            ? Downscale(oriented, maxSize)
            : oriented.Copy();

        // 用较高品质编码，避免缩略图压缩伪影
        using var image = SKImage.FromBitmap(final);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        var base64 = Convert.ToBase64String(encoded.ToArray());
        return $"data:image/jpeg;base64,{base64}";
    }

    // ── 内部方法 ──────────────────────────────────────────

    /// <summary>
    /// 文件扩展名 → MIME 类型
    /// </summary>
    private static string GetMimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".tiff" or ".tif" => "image/tiff",
        ".avif" => "image/avif",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// 如果图片超过最大尺寸则等比缩小。
    /// 使用 Mipmap Linear 滤波，缩略图更平滑、不锐化。
    /// </summary>
    private static SKBitmap Downscale(SKBitmap bitmap, int maxDimension)
    {
        if (bitmap.Width <= maxDimension && bitmap.Height <= maxDimension)
            return bitmap.Copy();

        float scale = Math.Min(
            maxDimension / (float)bitmap.Width,
            maxDimension / (float)bitmap.Height
        );
        int newW = (int)(bitmap.Width * scale);
        int newH = (int)(bitmap.Height * scale);

        var resized = new SKBitmap(newW, newH, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(resized);
        // DrawImage 支持 SKSamplingOptions：Linear + Mipmap 下采样更平滑
        using var srcImage = SKImage.FromBitmap(bitmap);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(srcImage, new SKRect(0, 0, newW, newH), sampling);
        return resized;
    }

    /// <summary>
    /// SVG → base64 data:URI
    /// </summary>
    private static ImageResult DecodeSvg(string filePath, long fileSize)
    {
        var svgContent = File.ReadAllText(filePath);
        var base64 = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(svgContent));
        return new ImageResult(
            $"data:image/svg+xml;base64,{base64}",
            0, 0, fileSize, "SVG");
    }

    /// <summary>
    /// 根据 EXIF Orientation 做旋转/翻转
    /// </summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return source.Copy();

        // 需要交换宽高的方向：RightTop (6) 顺时针90°, LeftBottom (8) 逆时针90°
        bool swap = origin is SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightTop;

        int targetW = swap ? source.Height : source.Width;
        int targetH = swap ? source.Width : source.Height;

        var rotated = new SKBitmap(targetW, targetH, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(rotated);

        switch (origin)
        {
            case SKEncodedOrigin.BottomRight:  // 3: 180°
                canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f);
                canvas.DrawBitmap(source, 0, 0);
                break;

            case SKEncodedOrigin.RightTop:     // 6: 顺时针 90°
                canvas.Translate(rotated.Width, 0);
                canvas.RotateDegrees(90);
                canvas.DrawBitmap(source, 0, 0);
                break;

            case SKEncodedOrigin.LeftBottom:   // 8: 逆时针 90°（顺时针 270°）
                canvas.Translate(0, rotated.Height);
                canvas.RotateDegrees(270);
                canvas.DrawBitmap(source, 0, 0);
                break;

            case SKEncodedOrigin.TopRight:     // 2: 水平翻转
                canvas.Scale(-1, 1);
                canvas.Translate(-source.Width, 0);
                canvas.DrawBitmap(source, 0, 0);
                break;

            case SKEncodedOrigin.BottomLeft:   // 4: 垂直翻转
                canvas.Scale(1, -1);
                canvas.Translate(0, -source.Height);
                canvas.DrawBitmap(source, 0, 0);
                break;

            default:                                // 5, 7 带镜像的旋转，极罕见
                return source.Copy();
        }

        canvas.Flush();
        return rotated;
    }
}
