namespace MauiMultimedia.Core.Models;

/// <summary>
/// 文件系统条目模型
/// </summary>
public class FileSystemItem
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    /// <summary>
    /// 显示图标
    /// </summary>
    public string Icon => IsFolder ? "\U0001F4C1" : "\U0001F4C4";

    public DateTime? LastModified { get; set; }

    /// <summary>
    /// 文件大小（字节），文件夹为 0
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 子项目数量（仅文件夹有效，null 表示尚未加载）
    /// </summary>
    public int? ChildCount { get; set; }

    /// <summary>
    /// 格式化后的子项目数量
    /// </summary>
    public string ChildCountDisplay
    {
        get
        {
            if (!IsFolder) return "";
            if (ChildCount == null) return "···";
            return $"{ChildCount} 项";
        }
    }

    /// <summary>
    /// 格式化后的修改时间
    /// </summary>
    public string LastModifiedDisplay
    {
        get
        {
            if (LastModified.HasValue)
                return LastModified.Value.ToString("yyyy-MM-dd HH:mm");
            return "-";
        }
    }

    /// <summary>
    /// 格式化后的文件大小；文件夹返回 "-"（不适用）
    /// </summary>
    public string SizeDisplay
    {
        get
        {
            if (IsFolder) return "-";
            return FormatSize(Size);
        }
    }

    /// <summary>
    /// 将字节数格式化为可读字符串（1024 进制：B / KB / MB / GB / TB）
    /// </summary>
    private static string FormatSize(long bytes)
    {
        double value = bytes;
        // 单位索引与「除以 1024 的次数」对齐：0→B, 1→KB, 2→MB, 3→GB, 4→TB
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
