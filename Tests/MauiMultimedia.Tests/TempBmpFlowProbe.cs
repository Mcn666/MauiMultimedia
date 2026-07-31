using MauiMultimedia.Viewers.Image.Services;
using Xunit;

namespace MauiMultimedia.Tests;

public class TempBmpFlowProbe
{
    private static readonly string P =
        @"C:\Users\69562\Desktop\Project\MauiMultimedia\TestSamples\Image\sample.bmp";

    [Fact]
    public void Probe_FullFlow()
    {
        // 1. 缓存查询（ImagePage 877 行）
        var cached = DecodeCache.Get(P);
        System.Console.WriteLine($"PROBE DecodeCache.Get: has={cached.HasValue} direct={cached?.IsDirectServe} {cached?.Width}x{cached?.Height}");

        // 2. GetDirectServeInfo（ImagePage 918 行）
        var dims = ImageProcessingService.GetDirectServeInfo(P);
        System.Console.WriteLine($"PROBE GetDirectServeInfo: canServe={dims.canServe} {dims.width}x{dims.height}");

        // 3. GenerateThumbnailBytes（decodeBudget 路径）
        var thumb = ImageProcessingService.GenerateThumbnailBytes(P, 400);
        System.Console.WriteLine($"PROBE GenerateThumbnailBytes: {(thumb == null ? "NULL" : thumb.Length + " bytes")}");

        // 4. DecodeImage(bytes)（全清路径）
        try
        {
            var bytes = File.ReadAllBytes(P);
            var r = ImageProcessingService.DecodeImage(bytes, Path.GetFileName(P), cacheDir: Path.GetTempPath());
            System.Console.WriteLine($"PROBE DecodeImage(bytes): OK {r.Width}x{r.Height} fmt={r.Format}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"PROBE DecodeImage(bytes): FAIL {ex.GetType().Name}: {ex.Message}");
        }

        // 5. DecodeImage(path) 版本
        try
        {
            var r2 = ImageProcessingService.DecodeImage(P);
            System.Console.WriteLine($"PROBE DecodeImage(path): OK {r2.Width}x{r2.Height} fmt={r2.Format}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"PROBE DecodeImage(path): FAIL {ex.GetType().Name}: {ex.Message}");
        }

        // 6. GenerateThumbnail（网格缩略图路径）
        var t = ImageProcessingService.GenerateThumbnail(P);
        System.Console.WriteLine($"PROBE GenerateThumbnail: {(string.IsNullOrEmpty(t) ? "EMPTY" : t[..Math.Min(20, t.Length)] + "...")}");
    }
}
