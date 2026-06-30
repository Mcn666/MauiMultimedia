namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 本地 HTTP 文件服务。为查看器（如视频播放器）提供基于 Range 请求的流式文件访问。
/// </summary>
public interface IFileServerService
{
    /// <summary>服务的基础 URL，如 http://127.0.0.1:12345</summary>
    string BaseUrl { get; }
}
