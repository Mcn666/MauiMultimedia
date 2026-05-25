namespace MauiMultimedia.Core.Models;

/// <summary>
/// 文件扫描分类——由支持库声明其可扫描的文件类型、标签和图标
/// </summary>
public record FileScanCategory(string Label, string[] Extensions, string? Icon = null);
