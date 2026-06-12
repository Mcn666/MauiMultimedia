namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 文件锁定服务接口。
/// 通过头部混淆（方案二）实现"其他应用无法正确打开文件"的效果。
/// </summary>
public interface IFileLockEncryptionService
{
    /// <summary>检查文件是否处于锁定状态（包含魔数尾部标记）</summary>
    bool IsLocked(string filePath);

    /// <summary>锁定文件：混淆头部，Append 尾部元数据</summary>
    Task LockAsync(string filePath, CancellationToken ct = default);

    /// <summary>解锁文件：从尾部恢复原始头部，删除尾部元数据</summary>
    Task UnlockAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// 打开已解码的只读流。
    /// 如果文件未锁定，直接返回 FileStream（FileShare.Read）；
    /// 如果文件已锁定，返回 DecryptedReadStream（在内存中修复头部，流式读取正文）。
    /// </summary>
    Task<Stream> OpenDecryptedReadStreamAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// 将锁定文件的全部内容以修正后的字节数组形式返回。
    /// 未锁定的文件直接返回 File.ReadAllBytes。
    /// </summary>
    Task<byte[]> ReadDecryptedBytesAsync(string filePath, CancellationToken ct = default);
}
