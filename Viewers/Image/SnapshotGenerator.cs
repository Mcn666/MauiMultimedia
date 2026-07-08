using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MauiMultimedia.Core.Services;
using MauiMultimedia.Viewers.Image.Services;
using Microsoft.JSInterop;

[assembly: InternalsVisibleTo("MauiMultimedia.Viewers.Image.Tests")]

namespace MauiMultimedia.Viewers.Image;

/// <summary>
/// JS 互调入口：供 IntersectionObserver 调用的缩略图生成。
/// 实际的缓存 / 限流逻辑由共享基类 <see cref="SnapshotServiceBase"/> 提供，
/// 本类仅作薄外观：保留静态 [JSInvokable] 入口并委托给内部单例。
/// </summary>
public static class SnapshotGenerator
{
    private static readonly ImageSnapshotService _instance = new();

    /// <summary>
    /// 生成缩略图并缓存。由 JS IntersectionObserver 在可视区内调用。
    /// </summary>
    [JSInvokable("generateSnapshot")]
    public static Task<string?> GenerateSnapshot(string filePath)
        => _instance.GetSnapshotAsync(filePath);

    private sealed class ImageSnapshotService : SnapshotServiceBase
    {
        protected override Task<string?> GenerateAsync(string filePath)
            => Task.Run(() => (string?)ImageProcessingService.GenerateThumbnail(filePath));
    }
}
