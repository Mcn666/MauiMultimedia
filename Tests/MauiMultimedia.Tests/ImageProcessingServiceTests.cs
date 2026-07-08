using System;
using MauiMultimedia.Viewers.Image.Services;
using SkiaSharp;
using Xunit;

namespace MauiMultimedia.Tests;

public class ImageProcessingServiceTests
{
    private static byte[] MakeImage(int w, int h, SKEncodedImageFormat fmt = SKEncodedImageFormat.Jpeg)
    {
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Orange);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(fmt, 90);
        return data.ToArray();
    }

    [Fact]
    public void GenerateThumbnail_ProducesSmallJpegDataUri()
    {
        var bytes = MakeImage(600, 400);
        var uri = ImageProcessingService.GenerateThumbnail(bytes);
        Assert.StartsWith("data:image/jpeg;base64,", uri);

        // 解码回产物，确认尺寸被约束在目标(maxSize=180)以内
        var b64 = uri["data:image/jpeg;base64,".Length..];
        var outBytes = Convert.FromBase64String(b64);
        using var codec = SKCodec.Create(new MemoryStream(outBytes));
        Assert.NotNull(codec);
        Assert.True(codec!.Info.Width <= 180 && codec.Info.Height <= 180);
    }

    [Fact]
    public void GenerateThumbnail_FromFilePath_Works()
    {
        var bytes = MakeImage(800, 600);
        var path = Path.Combine(Path.GetTempPath(), $"mm_test_{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, bytes);
        try
        {
            var uri = ImageProcessingService.GenerateThumbnail(path);
            Assert.StartsWith("data:image/jpeg;base64,", uri);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GenerateThumbnail_NonImage_ReturnsEmpty()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5 };
        Assert.Equal("", ImageProcessingService.GenerateThumbnail(garbage));
    }

    [Fact]
    public void GenerateThumbnail_LargeImage_CompletesWithoutThrow()
    {
        // 3000x2000 大图：验证中间解码上限与生命周期逻辑不抛异常且产出小图
        var bytes = MakeImage(3000, 2000);
        var uri = ImageProcessingService.GenerateThumbnail(bytes);
        Assert.StartsWith("data:image/jpeg;base64,", uri);

        var b64 = uri["data:image/jpeg;base64,".Length..];
        using var codec = SKCodec.Create(new MemoryStream(Convert.FromBase64String(b64)));
        Assert.NotNull(codec);
        Assert.True(codec!.Info.Width <= 180 && codec.Info.Height <= 180);
    }
}
