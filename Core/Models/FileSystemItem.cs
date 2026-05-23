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
}
