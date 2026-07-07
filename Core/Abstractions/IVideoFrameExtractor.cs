using System.Threading.Tasks;

namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 视频首帧取帧能力。由各平台原生实现（Viewers/Video.Native/Platforms/*），
/// Video RCL 在运行时从已加载的查看器程序集中“发现”并注册，从而把取帧委托
/// 设置到被 JSInvokable 实际使用的 Video 程序集实例上。
/// 这样避免跨程序集用 [ModuleInitializer] 设置静态字段时注册到错误程序集
/// 实例的问题，且 Shell 对此完全无感知（零侵入）。
/// </summary>
public interface IVideoFrameExtractor
{
    /// <summary>
    /// 提取视频在 1 秒附近的代表帧，编码为 JPEG 字节；不支持/失败时返回 null。
    /// </summary>
    Task<byte[]?> TryExtractAsync(string path);
}
