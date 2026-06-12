using System.Buffers.Binary;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 文件头部混淆锁定实现。
/// 
/// 锁定后文件布局：
///   [随机化头部（H 字节）][原始文件正文（B 字节）][尾部元数据]
///   尾部元数据 = [原始头部备份（H 字节）][头部长度（4字节 LE）][魔数 MMLOCK1A（8字节）]
/// 
/// 外部应用打开时头部被破坏，无法识别文件格式；
/// MauiMultimedia 通过尾部元数据恢复头部后正常读取。
/// </summary>
public class FileLockEncryptionService : IFileLockEncryptionService
{
    private static readonly byte[] Magic = FileLockConstants.MagicFooter;
    private const int MagicLen = FileLockConstants.MagicLength;
    private const int LenFieldSize = FileLockConstants.LengthFieldSize;
    private const int DefaultH = FileLockConstants.DefaultHeaderLength;

    // ────────────────────────────── 公开方法 ──────────────────────────────

    public bool IsLocked(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < MagicLen + LenFieldSize + 1) return false;

            // 读取末尾魔数
            var magicBuf = new byte[MagicLen];
            fs.Seek(-MagicLen, SeekOrigin.End);
            fs.ReadExactly(magicBuf, 0, MagicLen);
            return magicBuf.AsSpan().SequenceEqual(Magic);
        }
        catch
        {
            return false;
        }
    }

    public async Task LockAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("文件不存在", filePath);

        if (IsLocked(filePath))
            return; // 已锁定，幂等

        var fileInfo = new FileInfo(filePath);
        long originalLength = fileInfo.Length;
        // 记录原始修改时间，锁定后恢复（文件内容不变，只是头部混淆 + 尾部元数据）
        DateTime originalWriteTime = fileInfo.LastWriteTimeUtc;
        int headerLen = GetEffectiveHeaderLength(filePath, originalLength);

        // 读整个文件到内存
        var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        if (fileBytes.Length < headerLen + 1)
            throw new InvalidOperationException("文件太小，无法锁定");

        // 备份原始头部
        var headerBackup = new byte[headerLen];
        Array.Copy(fileBytes, headerBackup, headerLen);

        // 随机化头部
        Random.Shared.NextBytes(fileBytes.AsSpan(0, headerLen));

        // 构造尾部元数据
        int footerSize = headerLen + LenFieldSize + MagicLen;
        var footer = new byte[footerSize];
        Array.Copy(headerBackup, 0, footer, 0, headerLen);                         // 头部备份
        BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(headerLen), headerLen); // 头部长度
        Array.Copy(Magic, 0, footer, headerLen + LenFieldSize, MagicLen);          // 魔数

        // 写回文件：随机化头部 + 原始正文 + 尾部
        var output = new byte[fileBytes.Length + footerSize];
        Array.Copy(fileBytes, 0, output, 0, headerLen);       // 已随机化的头部（fileBytes 头部已被替换）
        Array.Copy(fileBytes, headerLen, output, headerLen,
            fileBytes.Length - headerLen);                     // 原始正文
        Array.Copy(footer, 0, output, fileBytes.Length, footerSize); // 尾部元数据

        await File.WriteAllBytesAsync(filePath, output, ct);
        File.SetLastWriteTimeUtc(filePath, originalWriteTime); // 恢复原始修改时间
    }

    public async Task UnlockAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("文件不存在", filePath);

        if (!IsLocked(filePath))
            return; // 未锁定，幂等

        // 记录原始修改时间（锁定文件的），解锁后恢复（原始文件内容不变）
        var fileInfo = new FileInfo(filePath);
        DateTime originalWriteTime = fileInfo.LastWriteTimeUtc;

        var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        var (headerLen, headerBackup) = ParseFooter(fileBytes);
        int footerSize = headerLen + LenFieldSize + MagicLen;
        int originalSize = fileBytes.Length - footerSize; // H + B

        // 重建原始文件
        var restored = new byte[originalSize];
        Array.Copy(headerBackup, 0, restored, 0, headerLen);               // 恢复头部
        Array.Copy(fileBytes, headerLen, restored, headerLen,
            originalSize - headerLen);                                      // 正文

        await File.WriteAllBytesAsync(filePath, restored, ct);
        File.SetLastWriteTimeUtc(filePath, originalWriteTime); // 恢复原始修改时间
    }

    public async Task<Stream> OpenDecryptedReadStreamAsync(string filePath, CancellationToken ct = default)
    {
        if (!IsLocked(filePath))
        {
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        var fileBytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        var (headerLen, headerBackup) = ParseFooter(fileBytes);
        int footerSize = headerLen + LenFieldSize + MagicLen;
        int originalSize = fileBytes.Length - footerSize;

        var result = new byte[originalSize];
        Array.Copy(headerBackup, 0, result, 0, headerLen);
        Array.Copy(fileBytes, headerLen, result, headerLen, originalSize - headerLen);

        return new MemoryStream(result);
    }

    public async Task<byte[]> ReadDecryptedBytesAsync(string filePath, CancellationToken ct = default)
    {
        if (!IsLocked(filePath))
            return await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

        var fileBytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        var (headerLen, headerBackup) = ParseFooter(fileBytes);
        int footerSize = headerLen + LenFieldSize + MagicLen;
        int originalSize = fileBytes.Length - footerSize;

        var result = new byte[originalSize];
        Array.Copy(headerBackup, 0, result, 0, headerLen);
        Array.Copy(fileBytes, headerLen, result, headerLen, originalSize - headerLen);

        return result;
    }

    // ────────────────────────────── 内部方法 ──────────────────────────────

    /// <summary>计算实际使用的头部长度（考虑文件大小）</summary>
    private static int GetEffectiveHeaderLength(string filePath, long fileLength)
    {
        int nominal = FileLockConstants.GetHeaderLength(filePath);
        // 至少保留 1 字节正文 + 尾部开销
        int minFooter = LenFieldSize + MagicLen + 1;
        return (int)Math.Min(nominal, Math.Max(1, fileLength - minFooter));
    }

    /// <summary>从文件字节数组中解析尾部元数据</summary>
    private static (int headerLen, byte[] headerBackup) ParseFooter(byte[] fileBytes)
    {
        // 验证魔数
        int magicStart = fileBytes.Length - MagicLen;
        if (!fileBytes.AsSpan(magicStart, MagicLen).SequenceEqual(Magic))
            throw new InvalidDataException("文件未处于锁定状态（魔数不匹配）");

        // 读取头部长度
        int lenStart = magicStart - LenFieldSize;
        int headerLen = BinaryPrimitives.ReadInt32LittleEndian(
            fileBytes.AsSpan(lenStart, LenFieldSize));

        if (headerLen <= 0 || headerLen > fileBytes.Length - LenFieldSize - MagicLen)
            throw new InvalidDataException("锁定元数据损坏（头部长度无效）");

        // 读取头部备份
        int backupStart = lenStart - headerLen;
        var headerBackup = new byte[headerLen];
        Array.Copy(fileBytes, backupStart, headerBackup, 0, headerLen);

        return (headerLen, headerBackup);
    }
}
