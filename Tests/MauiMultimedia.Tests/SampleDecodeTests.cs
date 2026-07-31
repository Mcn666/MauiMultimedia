using System.Text.Json;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Image.Services;
using MauiMultimedia.Viewers.Shared.Services;
using SharpCompress.Archives;
using SkiaSharp;
using Xunit;

namespace MauiMultimedia.Tests;

/// <summary>
/// 真实解码验证：用 TestSamples 中的样本文件跑查看器的实际解码路径，
/// 确认每个文件都能被真正处理（而不只是扩展名识别）。
/// </summary>
public class SampleDecodeTests
{
    private static FileSystemItem Item(string name) =>
        new() { Name = name, FullPath = name, IsFolder = false };
    private static string FindTestSamples()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "TestSamples");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到 TestSamples 目录");
    }

    private static string SamplesRoot => FindTestSamples();
    private static string Sample(string viewer, string name) =>
        Path.Combine(SamplesRoot, viewer, name);

    // ───────── Image: 全部样本真实解码 ─────────
    // 注：tiff/tif 已被移除（SkiaSharp 无 TIFF 编解码器）；avif 走浏览器直出（见 SvgImage/下）
    public static TheoryData<string> ImageSamples => new()
    {
        "sample.jpg", "sample.jpeg", "sample.jfif", "sample.png",
        "sample.gif", "sample.bmp", "sample.webp", "sample.ico"
    };

    [Theory]
    [MemberData(nameof(ImageSamples))]
    public void RasterImage_Decodes_ToNonEmptyThumbnail(string name)
    {
        var path = Sample("Image", name);
        var thumb = ImageProcessingService.GenerateThumbnail(path);
        Assert.StartsWith("data:", thumb);
    }

    [Fact]
    public void SvgImage_Decodes_ViaDecodeImage()
    {
        var result = ImageProcessingService.DecodeImage(Sample("Image", "sample.svg"));
        Assert.True(result.Width > 0 && result.Height > 0);
        Assert.StartsWith("data:", result.DataUri);
    }

    /// <summary>
    /// 网格快照回归：ImageProvider.CanProvideSnapshot(svg) 必须为 true——
    /// 曾因 NoSnapshotExts 排除 SVG（旧逻辑 SKCodec 解不了），导致 Home 网格
    /// 对 SVG 不渲染快照格子、不触发 generateSnapshot。SVG 缩略图已支持
    /// （浏览器原生 data URI），排除已移除。
    /// </summary>
    [Fact]
    public void Svg_GridSnapshot_IsEnabled()
    {
        var provider = new MauiMultimedia.Viewers.Image.ImageProvider();
        var item = Item("sample.svg");
        Assert.True(provider.CanProvideSnapshot(item),
            "SVG 必须允许网格快照（GenerateThumbnail 已支持浏览器原生 data URI）");
        Assert.True(provider.CanHandle(item));

        // 完整快照链路：CanProvideSnapshot → GenerateThumbnail 必须产出 data URI
        var thumb = ImageProcessingService.GenerateThumbnail(Sample("Image", "sample.svg"));
        Assert.StartsWith("data:image/svg+xml;base64,", thumb);
    }

    /// <summary>
    /// SVG/AVIF 缩略图回归：两者都是浏览器原生渲染但 SkiaSharp 无编解码器的格式，
    /// 缩略图直接用原始字节 data URI（浏览器 <img> 解码显示），不能返回空。
    /// </summary>
    [Theory]
    [InlineData("sample.svg", "data:image/svg+xml;base64,")]
    [InlineData("sample.avif", "data:image/avif;base64,")]
    public void SvgAndAvif_GenerateBrowserNativeThumbnail(string fileName, string expectedPrefix)
    {
        var thumb = ImageProcessingService.GenerateThumbnail(Sample("Image", fileName));
        Assert.StartsWith(expectedPrefix, thumb);
        Assert.True(thumb.Length > expectedPrefix.Length, "缩略图 data URI 不应为空");
    }

    [Fact]
    public void DdsImage_Decodes_ToNonEmptyThumbnail()
    {
        var path = Sample("Image", "sample.dds");
        var thumb = ImageProcessingService.GenerateThumbnail(path);
        Assert.StartsWith("data:", thumb);
    }

    /// <summary>
    /// 真实 MMD 模型贴图回归测试：BunnyLoli 的 M61501.dds 是标准 DXT5（fourCC="DXT5"，
    /// PIXELFORMAT 从 offset 76 起，2048×1024，bottom-up 行序需垂直翻转）。
    /// 保护 DdsDecoder 的 PIXELFORMAT 偏移不被改回错位（-4 会导致 fourCC 匹配不上、整图解码失败）。
    /// 文件不存在则跳过（开发机器上不一定有 BunnyLoli）。
    /// </summary>
    [Fact]
    public void Dds_RealBunnyLoli_DecodesDxt5Successfully()
    {
        var src = @"C:\Users\69562\Desktop\Other\模型\BunnyLoli\M61501.dds";
        if (!File.Exists(src)) return; // skip if file missing
        var (uri, w, h) = DdsDecoder.DecodeDds(src);
        Assert.NotNull(uri);
        Assert.StartsWith("data:image/png", uri!);
        Assert.Equal(2048, w);
        Assert.Equal(1024, h);
    }

    [Fact]
    public void Avif_IsBrowserNative_ButNotSkiaDecodable()
    {
        // AVIF 浏览器原生支持（WebView 直出渲染），但 SkiaSharp 无 AVIF 编解码器
        Assert.Contains(".avif", MauiMultimedia.Viewers.Image.ImageConstants.BrowserNative);
        using var codec = SKCodec.Create(Sample("Image", "sample.avif"));
        Assert.Null(codec); // 确认 SkiaSharp 解不了 → 网格缩略图为空但查看正常
    }

    /// <summary>
    /// 回归测试：AVIF 打开/预加载崩溃修复。
    /// 用户打开 sample.bmp 时，PreloadAdjacentAsync 预加载相邻的 sample.avif——
    /// 旧逻辑 GetDirectServeInfo 用 SKCodec 判断 canServe（AVIF SKCodec 不支持 → false）
    /// → 降级 DecodeImage → 抛「无法解码图片: sample.avif」→ unobserved task 异常崩溃。
    /// 修复后 canServe 由浏览器能力决定（true），AVIF 走 direct-serve 不再解码。
    /// </summary>
    [Fact]
    public void Avif_DirectServe_ReturnsTrue_WithDimensions()
    {
        var p = Sample("Image", "sample.avif");

        // canServe 必须为 true（浏览器可渲染），且能解析出尺寸（ispe box）
        var d = ImageProcessingService.GetDirectServeInfo(p);
        Assert.True(d.canServe, "AVIF 应走 direct-serve（浏览器原生渲染），不能降级到 DecodeImage");
        Assert.Equal(400, d.width);
        Assert.Equal(300, d.height);

        // 字节数组版本同样能取尺寸
        var gd = ImageProcessingService.GetImageDimensions(File.ReadAllBytes(p));
        Assert.Equal(400, gd.width);
        Assert.Equal(300, gd.height);

        // 关键断言：既然 canServe=true，Preload/全清路径都不会调用 DecodeImage，
        // 因此不会再抛出「无法解码图片」。DecodeImage 对 AVIF 抛异常属于预期，
        // 但任何调用路径都不应触达它。
    }

    [Fact]
    public void Tiff_IsNoLongerClaimed_ByImageViewer()
    {
        // TIFF 已被移除支持：SkiaSharp 无编解码器，浏览器也不原生渲染
        var viewer = new MauiMultimedia.Viewers.Image.ImageViewer();
        Assert.False(viewer.CanHandle(Item("sample.tiff")));
        Assert.False(viewer.CanHandle(Item("sample.tif")));
    }

    [Fact]
    public void AnimatedGif_HasMultipleFrames()
    {
        using var codec = SKCodec.Create(Sample("Image", "sample.gif"));
        Assert.NotNull(codec);
        Assert.True(codec!.FrameCount > 1, $"期望 GIF 有多帧，实际 {codec.FrameCount}");
    }

    // ───────── Archive: 全部样本真实解压 ─────────
    // 注：bz2/xz/zst 已被移除（单文件压缩流，ArchiveFactory.OpenArchive 不支持）
    public static TheoryData<string> ArchiveSamples => new()
    {
        "sample.zip", "sample.tar", "sample.gz", "sample.tgz",
        "sample.tar.gz", "sample.7z"
    };

    [Theory]
    [MemberData(nameof(ArchiveSamples))]
    public void Archive_Opens_WithEntries(string name)
    {
        var path = Sample("Archive", name);
        using var fs = File.OpenRead(path);
        using var archive = ArchiveFactory.OpenArchive(fs);
        var entries = archive.Entries.Count();
        Assert.True(entries > 0, $"{name}: 打开成功但无条目");
    }

    [Fact]
    public void SingleStreamFormats_NoLongerClaimed_ByArchiveViewer()
    {
        // bz2/xz/zst 是单文件压缩流，OpenArchive 无法打开，已从支持列表移除
        var viewer = new MauiMultimedia.Viewers.Archive.ArchiveViewer();
        Assert.False(viewer.CanHandle(Item("sample.bz2")));
        Assert.False(viewer.CanHandle(Item("sample.xz")));
        Assert.False(viewer.CanHandle(Item("sample.zst")));
    }

    // ───────── Model3D ─────────
    [Fact]
    public void Glb_HasValidContainer_AndParsableJson()
    {
        var path = Sample("Model3D", "sample.glb");
        var bytes = File.ReadAllBytes(path);

        // 头：magic + version + length
        Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));

        // 第一 chunk 应为 JSON，且可被解析
        var chunkLen = BitConverter.ToUInt32(bytes, 12);
        var chunkType = System.Text.Encoding.ASCII.GetString(bytes, 16, 4);
        Assert.Equal("JSON", chunkType);
        var json = System.Text.Encoding.UTF8.GetString(bytes, 20, (int)chunkLen);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("2.0", doc.RootElement.GetProperty("asset").GetProperty("version").GetString());
    }

    [Fact]
    public void Gltf_Json_Parses_AndHasBinBuffer()
    {
        var path = Sample("Model3D", "sample.gltf");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty("buffers", out var buffers));
        Assert.True(buffers.GetArrayLength() > 0);
        var binPath = Path.Combine(Path.GetDirectoryName(path)!, buffers[0].GetProperty("uri").GetString()!);
        Assert.True(File.Exists(binPath), "gltf 引用的 .bin 文件不存在");
    }

    // ───────── Video: 魔数 + 非零大小（无法在无 ffmpeg 环境解码） ─────────
    public static TheoryData<string> VideoSamples => new()
    {
        "sample.mp4",  "sample.webm", "sample.mkv", "sample.mov",
        "sample.avi",  "sample.wmv",  "sample.flv", "sample.m4v",
        "sample.3gp",  "sample.ogv",  "sample.mpg", "sample.mpeg",
        "sample.ts",   "sample.mts",  "sample.m2ts"
    };

    [Theory]
    [MemberData(nameof(VideoSamples))]
    public void Video_HasNonZeroSize_AndPlausibleMagic(string name)
    {
        var path = Sample("Video", name);
        var info = new FileInfo(path);
        Assert.True(info.Length > 0, $"{name}: 文件为空");

        using var fs = File.OpenRead(path);
        var head = new byte[16];
        fs.ReadExactly(head, 0, head.Length);

        // 常见容器魔数（u8 字面量会把非 ASCII 字节重编码为 UTF-8，EBML 用字节数组字面量）
        ReadOnlySpan<byte> ebml = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 };
        bool ok =
            head.AsSpan(4, 4).SequenceEqual("ftyp"u8) ||       // MP4/M4V/MOV/3GP
            head.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||       // AVI
            head.AsSpan(0, 3).SequenceEqual("FLV"u8) ||        // FLV
            head.AsSpan(0, 4).SequenceEqual(ebml) ||           // MKV/WebM (EBML)
            head.AsSpan(0, 4).SequenceEqual("OggS"u8) ||       // OGV (Ogg)
            (head[0] == 0x30 && head[1] == 0x26 && head[2] == 0xB2) || // WMV/ASF
            (head[0] == 0x00 && head[1] == 0x00 && head[2] == 0x01 && head[3] == 0xBA) || // MPEG PS
            head[0] == 0x47 || head[4] == 0x47;                // MPEG-TS / M2TS(192字节包，同步字节在偏移4)

        Assert.True(ok, $"{name}: 前导字节 {Convert.ToHexString(head)} 无法匹配已知容器魔数");
    }

    // ───────── Text: 全部样本可读且非空 ─────────
    public static TheoryData<string> TextSamples => new()
    {
        "sample.txt", "sample.log", "sample.md", "sample.csv",
        "sample.xml", "sample.json", "sample.yaml", "sample.yml",
        "sample.html", "sample.htm", "sample.css", "sample.js",
        "sample.py", "sample.cs", "sample.sh", "sample.bat",
        "sample.ps1", "sample.ini", "sample.cfg", "sample.conf",
        "sample.env", ".gitignore"
    };

    [Theory]
    [MemberData(nameof(TextSamples))]
    public void TextSample_IsReadable_AndNonEmpty(string name)
    {
        var path = Sample("Text", name);
        var content = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(content), $"{name}: 内容为空");
        Assert.True(content.Length >= 20, $"{name}: 内容过短");
    }

    [Theory]
    [MemberData(nameof(TextSamples))]
    public void TextSample_Decodes_AsUtf8(string name)
    {
        var bytes = File.ReadAllBytes(Sample("Text", name));
        var decoded = System.Text.Encoding.UTF8.GetString(bytes);
        // 无效 UTF-8 序列会解码为 U+FFFD 替换字符
        Assert.DoesNotContain('\uFFFD', decoded);
        // 有效 UTF-8 应能无损往返
        var roundTrip = System.Text.Encoding.UTF8.GetBytes(decoded);
        Assert.Equal(bytes, roundTrip);
    }

    // ───────── Html: 全部样本可读且非空 ─────────
    public static TheoryData<string> HtmlSamples => new()
    {
        "sample.html", "sample.htm", "sample.mht", "sample.mhtml"
    };

    [Theory]
    [MemberData(nameof(HtmlSamples))]
    public void HtmlSample_IsReadable_AndHasMarkup(string name)
    {
        var content = File.ReadAllText(Sample("Html", name));
        Assert.False(string.IsNullOrWhiteSpace(content), $"{name}: 内容为空");
        Assert.Contains("<", content);
        Assert.Contains(">", content);
    }

    // MHT/MHTML 走 MhtmlParser 真实解析（回归：续行折叠头曾吞掉正文导致 "未找到 HTML 内容"）
    public static TheoryData<string> MhtmlSamples => new() { "sample.mht", "sample.mhtml" };

    [Theory]
    [MemberData(nameof(MhtmlSamples))]
    public void MhtmlSample_Parses_WithHtmlBody(string name)
    {
        var result = MauiMultimedia.Viewers.Html.Services.MhtmlParser.Parse(Sample("Html", name));
        Assert.False(string.IsNullOrWhiteSpace(result.HtmlBody), $"{name}: 未解析出 HTML 正文");
        Assert.Contains("<", result.HtmlBody);
        Assert.Contains(">", result.HtmlBody);
    }

    // ───────── Model3D 文本格式结构验证 ─────────
    [Fact]
    public void Obj_HasVertices_AndFaces()
    {
        var lines = File.ReadAllLines(Sample("Model3D", "sample.obj"));
        Assert.Contains(lines, l => l.StartsWith("v "));
        Assert.Contains(lines, l => l.StartsWith("f "));
        Assert.Contains(lines, l => l.StartsWith("o "));
    }

    [Fact]
    public void Stl_IsAsciiSolid_WithFaces()
    {
        var content = File.ReadAllText(Sample("Model3D", "sample.stl"));
        Assert.StartsWith("solid", content.TrimStart());
        Assert.Contains("endsolid", content);
        Assert.Contains("facet normal", content);
        Assert.Contains("vertex", content);
    }
}
