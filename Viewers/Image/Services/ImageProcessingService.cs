using SkiaSharp;
using MauiMultimedia.Viewers.Shared.Services;
using System.Runtime.InteropServices;
using System.Diagnostics;
using MauiMultimedia.Core.Utils;

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
            var mime = MimeTypes.Get(ext);
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
    /// 浏览器原生可直接渲染的图片格式（无需 SkiaSharp 解码/重编码）。
    /// 这些格式交给 WebView 自己的解码器，由 FileServer 以原始文件流式提供，
    /// 省去 base64 膨胀、C#→JS 序列化开销，以及一次额外 Skia 解码。
    /// 注意：TIFF 浏览器不原生渲染，排除；SVG 走文本通道，单独处理。
    /// </summary>
    private static readonly HashSet<string> BrowserNativeFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".ico", ".avif"
    };

    /// <summary>
    /// 判断图片能否直接以原始文件经 FileServer 提供给浏览器（零 Skia 开销）。
    /// 返回的宽高已按 EXIF 方向校正——现代浏览器默认 <c>image-orientation:
    /// from-image</c>，会自行校正方向，因此即使带 EXIF 旋转的 JPEG 也能正确显示。
    /// </summary>
    public static (bool canServe, int width, int height) GetDirectServeInfo(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!BrowserNativeFormats.Contains(ext))
            return (false, 0, 0);

        using var codec = SKCodec.Create(filePath);
        if (codec == null)
            return (false, 0, 0);

        var origin = codec.EncodedOrigin;
        bool swap = origin is SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightTop;
        int w = swap ? codec.Info.Height : codec.Info.Width;
        int h = swap ? codec.Info.Width : codec.Info.Height;
        return (true, w, h);
    }

    /// <summary>
    /// 生成缩略图 data:URI（网格快照用）。尺寸小、快速加载。
    /// 
    /// 优化：通过 codec.GetScaledDimensions 让解码器按目标尺寸
    /// 直接解码，避免先解码完整大图再缩放的内存开销。
    /// </summary>
    public static string GenerateThumbnail(string filePath, int maxSize = 180)
    {
        // 中间解码上限（最长边）：先缩到此处再释放全尺寸位图，避免超大图一次性占满内存
        const int safeCap = 1024;
        // 超过该像素数的图跳过缩略图生成，防止极端大图 OOM 拖垮整个 app
        const long maxMegapixels = 100_000_000L;

        using var codec = SKCodec.Create(filePath);
        if (codec == null)
        {
            // DDS: SKCodec doesn't support it — decode full then downscale
            if (Path.GetExtension(filePath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                return GenerateDdsThumbnail(filePath, maxSize);
            return "";
        }
        var origin = codec.EncodedOrigin;

        int origW = codec.Info.Width;
        int origH = codec.Info.Height;
        if ((long)origW * origH > maxMegapixels) return "";

        try
        {
            // 计算目标缩略尺寸
            float scale = Math.Min(maxSize / (float)origW, maxSize / (float)origH);
            scale = Math.Min(scale, 1f); // 不放大

            // 让 codec 给出它原生支持的最佳缩放尺寸
            var scaled = codec.GetScaledDimensions(scale);
            int decodeW = Math.Max(1, scaled.Width);
            int decodeH = Math.Max(1, scaled.Height);

            // 解码上限保护：若解码尺寸仍过大（如 PNG 不支持原生下采样、返回原尺寸），
            // 收紧到 safeCap 以内，后续靠 Downscale 立即释放全尺寸位图。
            if (decodeW > safeCap || decodeH > safeCap)
            {
                float capScale = Math.Min(safeCap / (float)origW, safeCap / (float)origH);
                capScale = Math.Min(capScale, 1f);
                var capScaled = codec.GetScaledDimensions(capScale);
                decodeW = Math.Max(1, capScaled.Width);
                decodeH = Math.Max(1, capScaled.Height);
            }

            // 按缩放尺寸分配缓冲区，一次性解码到目标大小
            var info = new SKImageInfo(decodeW, decodeH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());

            // GetPixels 可能返回 Success 或 IncompleteInput（对完整文件都是 Success）
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                return "";

            // EXIF 方向校正：TopLeft 无需旋转，直接复用 bitmap，避免多分配一张全尺寸位图
            using var oriented = (origin == SKEncodedOrigin.TopLeft)
                ? null
                : ApplyOrientation(bitmap, origin);
            var source = oriented ?? bitmap;

            // 关键：先缩到 safeCap 以内并立即释放全尺寸位图，
            // 避免 bitmap + oriented + final 等多张全尺寸位图同时驻留造成尖峰。
            using var capped = (source.Width > safeCap || source.Height > safeCap)
                ? Downscale(source, safeCap)
                : source.Copy();

            // 若仍需更小，再缩到最终目标
            using var final = (capped.Width > maxSize || capped.Height > maxSize)
                ? Downscale(capped, maxSize)
                : capped.Copy();

            // 用较高品质编码，避免缩略图压缩伪影
            using var image = SKImage.FromBitmap(final);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            var base64 = Convert.ToBase64String(encoded.ToArray());
            return $"data:image/jpeg;base64,{base64}";
        }
        catch (Exception ex)
        {
            // 解码失败（含极端大图导致的 OOM）不崩溃，仅跳过该缩略图
            Debug.WriteLine($"[ImageProc] 缩略图生成失败: {ex.GetType().Name}: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 按 <paramref name="maxSize"/>（输出最长边，不放大）下采样解码，返回 JPEG/PNG 字节。
    /// 供查看器把“显示尺寸缩略图”注册到 FileServer（served URL），实现“不全解码”。
    /// 内部复用 <see cref="GenerateThumbnail(string, int)"/> 的下采样核心（含 safeCap 中间解码上限与 DDS 回退）。
    /// </summary>
    public static byte[]? GenerateThumbnailBytes(string filePath, int maxSize = 180)
    {
        var uri = GenerateThumbnail(filePath, maxSize);
        if (string.IsNullOrEmpty(uri)) return null;
        const string jpegPrefix = "data:image/jpeg;base64,";
        const string pngPrefix = "data:image/png;base64,";
        string b64;
        if (uri.StartsWith(jpegPrefix, StringComparison.OrdinalIgnoreCase)) b64 = uri[jpegPrefix.Length..];
        else if (uri.StartsWith(pngPrefix, StringComparison.OrdinalIgnoreCase)) b64 = uri[pngPrefix.Length..];
        else return null;
        try { return Convert.FromBase64String(b64); }
        catch { return null; }
    }

    /// <summary>
    /// 从字节数组解码图片（适用于已解密的内存数据）。
    /// </summary>
    public static ImageResult DecodeImage(byte[] fileData, string fileName, int maxDimension = 4000, string? cacheDir = null)
    {
        var ext = Path.GetExtension(fileName);

        // SVG 走文本读取
        if (string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase))
            return DecodeSvg(fileData);

        // DDS: SKCodec doesn't support it, use manual decoder
        if (string.Equals(ext, ".dds", StringComparison.OrdinalIgnoreCase))
        {
            // Write bytes to a temp file for DecodeDds（落在应用私有缓存目录内，而非系统 Temp）
            var tmpDir = Path.Combine(cacheDir ?? Path.GetTempPath(), "MauiMM_DdsDecode");
            Directory.CreateDirectory(tmpDir);
            var tmpPath = Path.Combine(tmpDir, Guid.NewGuid() + ".dds");
            try
            {
                File.WriteAllBytes(tmpPath, fileData);
                var result = DecodeDds(tmpPath);
                if (result.dataUri != null)
                    return new ImageResult
                    {
                        DataUri = result.dataUri,
                        Width = result.width,
                        Height = result.height,
                        Format = "PNG"
                    };
            }
            finally { try { File.Delete(tmpPath); } catch { } }
            throw new InvalidOperationException("无法解码DDS图片");
        }

        using var stream = new MemoryStream(fileData);
        using var codec = SKCodec.Create(stream);
        if (codec == null)
            throw new InvalidOperationException("无法解码图片");

        var origin = codec.EncodedOrigin;
        bool needsDownscale = codec.Info.Width > maxDimension || codec.Info.Height > maxDimension;
        bool needsExifFix = origin != SKEncodedOrigin.TopLeft;

        // ── 快速路径：无需缩放且无需 EXIF 校正 → 直接 base64 ──
        if (!needsDownscale && !needsExifFix)
        {
            var base64 = Convert.ToBase64String(fileData);
            var mime = MimeTypes.Get(ext);
            var dataUri = $"data:{mime};base64,{base64}";
            var fmtName = ext.TrimStart('.').ToUpperInvariant();
            return new ImageResult(dataUri, codec.Info.Width, codec.Info.Height,
                fileData.Length, fmtName);
        }

        // ── 慢速路径 ──
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
            var fmtName = ext.TrimStart('.').ToUpperInvariant();

            return new ImageResult(dataUriEncoded, displayBitmap.Width, displayBitmap.Height,
                fileData.Length, fmtName);
        }
    }

    /// <summary>
    /// 从字节数组中获取图片原始尺寸（考虑 EXIF 方向）
    /// </summary>
    public static (int width, int height) GetImageDimensions(byte[] fileData)
    {
        using var stream = new MemoryStream(fileData);
        using var codec = SKCodec.Create(stream);
        if (codec == null) return (0, 0);

        var origin = codec.EncodedOrigin;
        bool swap = origin == SKEncodedOrigin.LeftBottom ||
                    origin == SKEncodedOrigin.RightTop;
        return swap
            ? (codec.Info.Height, codec.Info.Width)
            : (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>
    /// 从字节数组生成缩略图 data:URI
    /// </summary>
    public static string GenerateThumbnail(byte[] fileData, int maxSize = 180)
    {
        // 中间解码上限（最长边）：先缩到此处再释放全尺寸位图，避免超大图一次性占满内存
        const int safeCap = 1024;
        // 超过该像素数的图跳过缩略图生成，防止极端大图 OOM 拖垮整个 app
        const long maxMegapixels = 100_000_000L;

        using var stream = new MemoryStream(fileData);
        using var codec = SKCodec.Create(stream);
        if (codec == null) return "";
        var origin = codec.EncodedOrigin;

        int origW = codec.Info.Width;
        int origH = codec.Info.Height;
        if ((long)origW * origH > maxMegapixels) return "";

        try
        {
            float scale = Math.Min(maxSize / (float)origW, maxSize / (float)origH);
            scale = Math.Min(scale, 1f);

            var scaled = codec.GetScaledDimensions(scale);
            int decodeW = Math.Max(1, scaled.Width);
            int decodeH = Math.Max(1, scaled.Height);

            // 解码上限保护：若解码尺寸仍过大（如 PNG 不支持原生下采样、返回原尺寸），
            // 收紧到 safeCap 以内，后续靠 Downscale 立即释放全尺寸位图。
            if (decodeW > safeCap || decodeH > safeCap)
            {
                float capScale = Math.Min(safeCap / (float)origW, safeCap / (float)origH);
                capScale = Math.Min(capScale, 1f);
                var capScaled = codec.GetScaledDimensions(capScale);
                decodeW = Math.Max(1, capScaled.Width);
                decodeH = Math.Max(1, capScaled.Height);
            }

            var info = new SKImageInfo(decodeW, decodeH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());

            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                return "";

            // EXIF 方向校正：TopLeft 无需旋转，直接复用 bitmap，避免多分配一张全尺寸位图
            using var oriented = (origin == SKEncodedOrigin.TopLeft)
                ? null
                : ApplyOrientation(bitmap, origin);
            var source = oriented ?? bitmap;

            // 关键：先缩到 safeCap 以内并立即释放全尺寸位图，
            // 避免 bitmap + oriented + final 等多张全尺寸位图同时驻留造成尖峰。
            using var capped = (source.Width > safeCap || source.Height > safeCap)
                ? Downscale(source, safeCap)
                : source.Copy();

            using var final = (capped.Width > maxSize || capped.Height > maxSize)
                ? Downscale(capped, maxSize)
                : capped.Copy();

            using var image = SKImage.FromBitmap(final);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            var base64 = Convert.ToBase64String(encoded.ToArray());
            return $"data:image/jpeg;base64,{base64}";
        }
        catch (Exception ex)
        {
            // 解码失败（含极端大图导致的 OOM）不崩溃，仅跳过该缩略图
            Debug.WriteLine($"[ImageProc] 缩略图生成失败: {ex.GetType().Name}: {ex.Message}");
            return "";
        }
    }

    // ── 内部方法 ──────────────────────────────────────────


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

    private static ImageResult DecodeSvg(byte[] fileData)
    {
        var base64 = Convert.ToBase64String(fileData);
        return new ImageResult(
            $"data:image/svg+xml;base64,{base64}",
            0, 0, fileData.Length, "SVG");
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

    private static string GenerateDdsThumbnail(string filePath, int maxSize)
    {
        try
        {
            // Decode full-size DDS to RGBA via DecodeDds, then downscale
            var sr = DecodeDds(filePath);
            if (sr.dataUri == null || sr.width <= 0 || sr.height <= 0) return "";
            if (sr.width <= maxSize && sr.height <= maxSize)
                return sr.dataUri; // already small enough

            // Decode the PNG back, downscale, re-encode
            var pngBase64 = sr.dataUri.Substring("data:image/png;base64,".Length);
            using var stream = new MemoryStream(Convert.FromBase64String(pngBase64));
            using var full = SKBitmap.Decode(stream);
            if (full == null) return "";

            float scale = Math.Min((float)maxSize / full.Width, (float)maxSize / full.Height);
            scale = Math.Min(scale, 1f);
            int tw = Math.Max(1, (int)(full.Width * scale));
            int th = Math.Max(1, (int)(full.Height * scale));

            using var scaled = full.Resize(new SKImageInfo(tw, th), new SKSamplingOptions(SKFilterMode.Linear));
            if (scaled == null) return "";
            using var image = SKImage.FromBitmap(scaled);
            using var png = image.Encode(SKEncodedImageFormat.Png, 80);
            return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
        }
        catch { return ""; }
    }


    // DDS decoding moved to MauiMultimedia.Viewers.Shared.Services.DdsDecoder.
    // Kept here as a thin facade so existing callers keep working unchanged.
    public static (string? dataUri, int width, int height) DecodeDds(string filePath)
        => DdsDecoder.DecodeDds(filePath);
}
