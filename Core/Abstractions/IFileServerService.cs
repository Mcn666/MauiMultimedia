namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 本地 HTTP 文件服务。为查看器（如视频播放器）提供基于 Range 请求的流式文件访问。
/// </summary>
public interface IFileServerService
{
    /// <summary>服务的基础 URL，如 http://127.0.0.1:12345</summary>
    string BaseUrl { get; }

    /// <summary>
    /// 注册一个本地文件，返回一次性访问令牌。随后只能通过 <c>?token=xxx</c> 访问该文件，
    /// 令牌由宿主（C#）持有，WebView 内的 JS 无法枚举或构造其他文件的令牌，
    /// 从而杜绝通过 <c>?path=...</c> 读取整机任意文件的漏洞。
    /// </summary>
    /// <param name="filePath">要提供的本地文件绝对路径</param>
    /// <param name="mimeType">
    /// 可选的显式 MIME 类型。由调用方（查看器）自行决定其文件应当使用的 <c>Content-Type</c>，
    /// 实现“查看器管理自己的 MIME”。传 <c>null</c>（默认）则回落到标准映射表
    /// （<see cref="MauiMultimedia.Core.Utils.MimeTypes"/>）。
    /// </param>
    /// <returns>访问令牌</returns>
    /// <exception cref="ArgumentException">路径为空或文件不存在时抛出</exception>
    string RegisterFile(string filePath, string? mimeType = null);

    /// <summary>注销令牌，使其随后失效（如查看器离开页面时调用）。</summary>
    void UnregisterFile(string token);

    /// <summary>注册一个本地目录，返回目录级访问令牌。该目录下的任意文件可通过 <c>{BaseUrl}/dir/{token}/relative/path</c> 访问。</summary>
    /// <param name="dirPath">要提供的本地目录绝对路径</param>
    /// <param name="defaultMimeType">
    /// 可选的目录级默认 MIME。仅用于“标准表回落为 <c>application/octet-stream</c>”的文件（即标准表未覆盖的查看器自有格式），
    /// 用于填补空白；其余文件（如贴图、字体）仍按其真实类型返回，不会被覆盖。传 <c>null</c>（默认）则完全使用标准表。
    /// </param>
    string RegisterDirectory(string dirPath, string? defaultMimeType = null);

    /// <summary>注销目录令牌。</summary>
    void UnregisterDirectory(string token);
}
