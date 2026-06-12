using System.Collections.Frozen;

namespace MauiMultimedia.Core.Models;

/// <summary>
/// 文件锁定功能常量：魔数标记、各格式头部混淆长度。
/// </summary>
public static class FileLockConstants
{
    /// <summary>
    /// 锁定文件尾部魔数标记（8 字节）。
    /// </summary>
    public static readonly byte[] MagicFooter = "MMLOCK1A"u8.ToArray();

    /// <summary>魔数标记字节长度</summary>
    public const int MagicLength = 8;

    /// <summary>头部长度存储字节数（4 字节 int32 LE）</summary>
    public const int LengthFieldSize = 4;

    /// <summary>默认最大头部混淆字节数</summary>
    public const int DefaultHeaderLength = 256;

    /// <summary>文件最小长度（必须能容纳头部 + 尾部 + 至少 1 字节正文）</summary>
    public const int MinFileSize = DefaultHeaderLength + LengthFieldSize + MagicLength + 1;

    /// <summary>
    /// 各格式对应的头部混淆长度（字节数）。
    /// 选择足够覆盖文件魔数/Signature 的长度，确保外部应用无法识别格式。
    /// </summary>
    public static FrozenDictionary<string, int> HeaderLengths { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        // ── 图片格式 ──
        { ".jpg",   256 },  // FF D8 + APP markers
        { ".jpeg",  256 },
        { ".png",   64 },   // 8-byte signature + IHDR
        { ".gif",   64 },   // 6-byte signature + Logical Screen Descriptor
        { ".bmp",   64 },   // 14-byte BITMAPFILEHEADER
        { ".webp",  128 },  // RIFF....WEBP
        { ".ico",   64 },   // 6-byte header + directory entries
        { ".tiff",  64 },   // 4-byte magic + IFD
        { ".tif",   64 },
        { ".avif",  128 },  // ftyp box
        { ".heic",  128 },
        { ".svg",   256 },  // text-based, need to break XML structure

        // ── 文本与代码 ──
        { ".txt",   256 },
        { ".log",   256 },
        { ".md",    256 },
        { ".csv",   128 },
        { ".xml",   256 },
        { ".json",  128 },
        { ".yaml",  128 },
        { ".yml",   128 },
        { ".cs",    128 },
        { ".js",    128 },
        { ".ts",    128 },
        { ".jsx",   128 },
        { ".tsx",   128 },
        { ".css",   128 },
        { ".scss",  128 },
        { ".less",  128 },
        { ".html",  256 },
        { ".htm",   256 },
        { ".php",   128 },
        { ".py",    128 },
        { ".java",  128 },
        { ".cpp",   128 },
        { ".c",     128 },
        { ".h",     128 },
        { ".sql",   128 },
        { ".sh",    128 },
        { ".bat",   128 },
        { ".ps1",   128 },
        { ".rb",    128 },
        { ".go",    128 },
        { ".rs",    128 },
        { ".swift", 128 },
        { ".ini",   128 },
        { ".cfg",   128 },
        { ".conf",  128 },
        { ".env",   128 },
        { ".gitignore", 128 },
        { ".dockerfile", 128 },
        { ".csproj", 256 },
        { ".sln",   256 },
        { ".slnx",  256 },
        { ".props", 128 },
        { ".targets", 128 },

        // ── 其他 ──
        { ".pdf",   256 },
        { ".doc",   256 },
        { ".docx",  256 },
        { ".xls",   256 },
        { ".xlsx",  256 },
        { ".ppt",   256 },
        { ".pptx",  256 },
        { ".zip",   256 },  // PK\x03\x04
        { ".tar",   256 },
        { ".gz",    256 },
        { ".tgz",   256 },
        { ".rar",   256 },
        { ".7z",    256 },
    }.ToFrozenDictionary();

    /// <summary>
    /// 获取指定文件扩展名的头部混淆长度。
    /// 未知格式使用 DefaultHeaderLength。
    /// </summary>
    public static int GetHeaderLength(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return HeaderLengths.TryGetValue(ext, out var len) ? len : DefaultHeaderLength;
    }
}
