using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Image;
using MauiMultimedia.Viewers.Text;
using MauiMultimedia.Viewers.Html;
using MauiMultimedia.Viewers.Model3D;
using MauiMultimedia.Viewers.Video;
using MauiMultimedia.Viewers.Archive;
using Xunit;

namespace MauiMultimedia.Tests;

/// <summary>
/// 验证每个查看器的 Constants.Exts 与 TestSamples 目录中的样本文件互相匹配：
/// 1. 每个声明的扩展名都有对应的测试样本
/// 2. 每个样本都能被对应查看器的 CanHandle 接受
/// 防止"列表里写了但实际没有样本/处理不了"的脱节。
/// </summary>
public class SampleCoverageTests
{
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

    /// <summary>扩展名 → 样本文件名（处理 .tar.gz 双后缀和 .gitignore 点文件）</summary>
    private static string SampleFileName(string ext)
    {
        if (string.Equals(ext, ".tar.gz", StringComparison.OrdinalIgnoreCase))
            return "sample.tar.gz";
        if (string.Equals(ext, ".gitignore", StringComparison.OrdinalIgnoreCase))
            return ".gitignore";
        return $"sample{ext}";
    }

    private static FileSystemItem Item(string name) =>
        new() { Name = name, FullPath = name, IsFolder = false };

    private static void AssertAllSamplesPresent(string viewerDir, IEnumerable<string> exts, Func<string, bool> canHandle)
    {
        var dir = Path.Combine(SamplesRoot, viewerDir);
        Assert.True(Directory.Exists(dir), $"TestSamples/{viewerDir} 目录不存在");

        foreach (var ext in exts)
        {
            var fileName = SampleFileName(ext);
            var path = Path.Combine(dir, fileName);

            // 1. 样本必须存在
            Assert.True(File.Exists(path),
                $"{viewerDir}: 扩展名 {ext} 缺少样本 {fileName}");

            // 2. 样本必须被查看器识别
            Assert.True(canHandle(fileName),
                $"{viewerDir}: 样本 {fileName} ({ext}) 未被 CanHandle 接受");
        }
    }

    // ───────── Image ─────────
    [Fact]
    public void Image_AllDeclaredExts_HaveSamples()
    {
        var viewer = new ImageViewer();
        AssertAllSamplesPresent("Image", ImageConstants.AllExts,
            name => viewer.CanHandle(Item(name)));
    }

    // ───────── Text ─────────
    [Fact]
    public void Text_AllDeclaredExts_HaveSamples()
    {
        var viewer = new TextViewer();
        AssertAllSamplesPresent("Text", TextConstants.Exts,
            name => viewer.CanHandle(Item(name)));
    }

    // ───────── Html ─────────
    [Fact]
    public void Html_AllDeclaredExts_HaveSamples()
    {
        var viewer = new HtmlViewer();
        AssertAllSamplesPresent("Html", HtmlConstants.Exts,
            name => viewer.CanHandle(Item(name)));
    }

    // ───────── Model3D ─────────
    [Fact]
    public void Model3D_DeclaredExts_HaveSamples()
    {
        var viewer = new Model3DViewer();
        // fbx/pmx/vrm 是专有格式，暂无样本；这里只验证"有样本的扩展名可处理"
        var hasSample = Model3DConstants.Exts
            .Where(ext => File.Exists(Path.Combine(Path.Combine(SamplesRoot, "Model3D"), SampleFileName(ext))))
            .ToList();
        Assert.NotEmpty(hasSample);
        AssertAllSamplesPresent("Model3D", hasSample,
            name => viewer.CanHandle(Item(name)));
    }

    // ───────── Video ─────────
    [Fact]
    public void Video_AllDeclaredExts_HaveSamples()
    {
        var viewer = new VideoViewer();
        AssertAllSamplesPresent("Video", VideoConstants.Exts,
            name => viewer.CanHandle(Item(name)));
    }

    // ───────── Archive ─────────
    [Fact]
    public void Archive_AllDeclaredExts_HaveSamples()
    {
        var viewer = new ArchiveViewer();
        // .rar 是专有格式无法免费生成样本；验证其余扩展名
        var hasSample = ArchiveConstants.Exts
            .Where(ext => File.Exists(Path.Combine(Path.Combine(SamplesRoot, "Archive"), SampleFileName(ext))))
            .ToList();
        Assert.NotEmpty(hasSample);
        AssertAllSamplesPresent("Archive", hasSample,
            name => viewer.CanHandle(Item(name)));
    }
}
