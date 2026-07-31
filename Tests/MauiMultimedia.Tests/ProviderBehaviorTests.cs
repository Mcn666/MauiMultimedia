using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Core.Utils;
using MauiMultimedia.Viewers.Archive;
using MauiMultimedia.Viewers.Html;
using MauiMultimedia.Viewers.Image;
using MauiMultimedia.Viewers.Model3D;
using MauiMultimedia.Viewers.Text;
using MauiMultimedia.Viewers.Video;
using Xunit;

namespace MauiMultimedia.Tests;

/// <summary>
/// 特征测试（characterization tests）：锁定各查看器提供者的真实行为，
/// 作为接口隔离（IViewProvider 拆分为 IItemPresenter / ISnapshotProvider）后的安全网。
/// 拆分后方法归属变了，但返回值不变，测试应仍全绿。
/// </summary>
public class ProviderBehaviorTests
{
    private static FileSystemItem Item(string name, bool folder = false) =>
        new() { Name = name, FullPath = (folder ? "/d/" : "/d/") + name, IsFolder = folder };

    // ───────── Image ─────────
    [Fact]
    public void ImageProvider_HandlesKnownExts_NotFolder()
    {
        var provider = new ImageProvider();
        IItemPresenter p = provider;
        Assert.True(p.CanHandle(Item("a.jpg")));
        Assert.True(p.CanHandle(Item("a.webp")));
        Assert.True(p.CanHandle(Item("a.svg")));
        Assert.False(p.CanHandle(Item("a.mp4")));
        Assert.False(p.CanHandle(Item("a.xyz")));
        Assert.False(p.CanHandle(Item("folder", folder: true)));
    }

    [Fact]
    public void ImageProvider_PresenterValues()
    {
        var provider = new ImageProvider();
        IItemPresenter p = provider;
        Assert.Equal("is-image-file", p.GetItemCssClass(Item("a.png")));
        Assert.Equal("\U0001F5BC", p.GetIcon(Item("a.png")));
        Assert.NotNull(p.ScanCategory);
        Assert.Equal("图片", p.ScanCategory!.Label);
        Assert.Contains(".png", p.ScanCategory.Extensions);
    }

    [Fact]
    public void ImageProvider_SnapshotBehavior()
    {
        var provider = new ImageProvider();
        ISnapshotProvider s = provider;
        Assert.True(s.CanProvideSnapshot(Item("a.png")));
        Assert.True(s.CanProvideSnapshot(Item("a.svg"))); // SVG 已支持浏览器原生缩略图（data URI）
        Assert.Null(s.GetItemSnapshot(Item("a.png")));
        Assert.Equal("MauiMultimedia.Viewers.Image", s.SnapshotAssembly);
        Assert.Equal("generateSnapshot", s.SnapshotMethod);
    }

    // ───────── Video ─────────
    [Fact]
    public void VideoProvider_HandlesKnownExts()
    {
        var provider = new VideoProvider();
        IItemPresenter p = provider;
        Assert.True(p.CanHandle(Item("a.mp4")));
        Assert.True(p.CanHandle(Item("a.mkv")));
        Assert.False(p.CanHandle(Item("a.jpg")));
        Assert.False(p.CanHandle(Item("a.xyz")));
    }

    [Fact]
    public void VideoProvider_PresenterValues()
    {
        var provider = new VideoProvider();
        IItemPresenter p = provider;
        Assert.Equal("is-video-file", p.GetItemCssClass(Item("a.mp4")));
        Assert.Equal("\U0001F3AC", p.GetIcon(Item("a.mp4")));
        Assert.Equal("视频", p.ScanCategory!.Label);
    }

    [Fact]
    public void VideoProvider_SnapshotBehavior()
    {
        var provider = new VideoProvider();
        ISnapshotProvider s = provider;
        Assert.True(s.CanProvideSnapshot(Item("a.mp4")));
        Assert.Null(s.GetItemSnapshot(Item("a.mp4")));
        Assert.Equal("MauiMultimedia.Viewers.Video", s.SnapshotAssembly);
        Assert.Equal("generateVideoSnapshot", s.SnapshotMethod);
    }

    // ───────── Text ─────────
    [Fact]
    public void TextProvider_HandlesKnownExts_NoSnapshot()
    {
        var provider = new TextProvider();
        IItemPresenter p = provider;
        ISnapshotProvider s = provider;
        Assert.True(p.CanHandle(Item("a.txt")));
        Assert.True(p.CanHandle(Item("a.cs")));
        Assert.True(p.CanHandle(Item("a.gitignore")));
        Assert.False(p.CanHandle(Item("a.jpg")));
        Assert.Equal("is-text-file", p.GetItemCssClass(Item("a.txt")));
        Assert.Equal("\U0001F4DD", p.GetIcon(Item("a.txt")));
        Assert.Equal("文档", p.ScanCategory!.Label);
        Assert.False(s.CanProvideSnapshot(Item("a.txt")));
        Assert.Equal(string.Empty, s.SnapshotAssembly);
        Assert.Equal(string.Empty, s.SnapshotMethod);
    }

    // ───────── Html ─────────
    [Fact]
    public void HtmlProvider_HandlesKnownExts_NoSnapshot()
    {
        var provider = new HtmlProvider();
        IItemPresenter p = provider;
        ISnapshotProvider s = provider;
        Assert.True(p.CanHandle(Item("a.html")));
        Assert.True(p.CanHandle(Item("a.mhtml")));
        Assert.False(p.CanHandle(Item("a.txt")));
        Assert.Equal("is-html-file", p.GetItemCssClass(Item("a.html")));
        Assert.Equal("\U0001F310", p.GetIcon(Item("a.html")));
        Assert.Equal("网页", p.ScanCategory!.Label);
        Assert.False(s.CanProvideSnapshot(Item("a.html")));
        Assert.Equal(string.Empty, s.SnapshotAssembly);
    }

    // ───────── Archive ─────────
    [Fact]
    public void ArchiveProvider_HandlesKnownExts_IncludingDoubleExt()
    {
        var provider = new ArchiveProvider();
        IItemPresenter p = provider;
        ISnapshotProvider s = provider;
        Assert.True(p.CanHandle(Item("a.zip")));
        Assert.True(p.CanHandle(Item("a.tar")));
        Assert.True(p.CanHandle(Item("a.tar.gz")));   // 双扩展名特例
        Assert.True(p.CanHandle(Item("a.7z")));
        Assert.False(p.CanHandle(Item("a.jpg")));
        Assert.Equal("is-archive-file", p.GetItemCssClass(Item("a.zip")));
        Assert.Equal("\U0001F4E6", p.GetIcon(Item("a.zip")));
        Assert.Equal("压缩包", p.ScanCategory!.Label);
        Assert.False(s.CanProvideSnapshot(Item("a.zip")));
        Assert.Equal(string.Empty, s.SnapshotAssembly);
    }

    // ───────── Model3D ─────────
    [Fact]
    public void Model3DProvider_HandlesKnownExts_NoSnapshot()
    {
        var provider = new Model3DProvider();
        IItemPresenter p = provider;
        ISnapshotProvider s = provider;
        Assert.True(p.CanHandle(Item("a.glb")));
        Assert.True(p.CanHandle(Item("a.obj")));
        Assert.False(p.CanHandle(Item("a.jpg")));
        Assert.Equal("is-model-file", p.GetItemCssClass(Item("a.glb")));
        Assert.Equal("\U0001F4F9", p.GetIcon(Item("a.glb")));
        Assert.Equal("3D 模型", p.ScanCategory!.Label);
        Assert.False(s.CanProvideSnapshot(Item("a.glb")));
        Assert.Equal(string.Empty, s.SnapshotAssembly);
    }

    // ───────── 接口隔离：每个 Provider 同时实现两个窄接口 ─────────
    [Theory]
    [InlineData(typeof(ImageProvider))]
    [InlineData(typeof(VideoProvider))]
    [InlineData(typeof(TextProvider))]
    [InlineData(typeof(HtmlProvider))]
    [InlineData(typeof(ArchiveProvider))]
    [InlineData(typeof(Model3DProvider))]
    public void EveryProvider_ImplementsBothNarrowInterfaces(Type providerType)
    {
        var instance = Activator.CreateInstance(providerType)!;
        Assert.IsAssignableFrom<IItemPresenter>(instance);
        Assert.IsAssignableFrom<ISnapshotProvider>(instance);
    }

    // ───────── 纯逻辑：自然排序 ─────────
    [Fact]
    public void NaturalSortComparer_SortsNumericPartsNumerically()
    {
        var list = new[] { "img10", "img2", "img1", "img20" }.ToList();
        list.Sort(NaturalSortComparer.Instance);
        Assert.Equal(new[] { "img1", "img2", "img10", "img20" }, list);
    }

    [Fact]
    public void NaturalSortComparer_HandlesMixedPrefixAndSuffix()
    {
        // "a2b" < "a10b" 因为数字段 2 < 10
        Assert.True(NaturalSortComparer.Instance.Compare("a2b", "a10b") < 0);
        Assert.True(NaturalSortComparer.Instance.Compare("a10b", "a2b") > 0);
    }

    [Fact]
    public void NaturalSortComparer_HandlesNullsAndPlainStrings()
    {
        Assert.True(NaturalSortComparer.Instance.Compare("abc", "abd") < 0);
        Assert.Equal(0, NaturalSortComparer.Instance.Compare(null, null));
    }
}
