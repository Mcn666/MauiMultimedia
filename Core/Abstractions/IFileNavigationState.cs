namespace MauiMultimedia.Core.Abstractions;

/// <summary>
/// 文件导航状态，支持库页面通过此接口获取当前目录的文件列表以实现前后文件导航
/// </summary>
public interface IFileNavigationState
{
    /// <summary>
    /// 当前目录中的文件路径列表（按浏览器排序顺序），供查看器前后导航使用
    /// </summary>
    IReadOnlyList<string>? CurrentDirectoryFiles { get; set; }
}
