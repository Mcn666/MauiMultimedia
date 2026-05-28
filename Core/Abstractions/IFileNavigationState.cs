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

    /// <summary>
    /// 当前选中的文件路径，Home 在导航前设置，查看器在 OnInitialized 中读取
    /// </summary>
    string? CurrentFilePath { get; set; }

    /// <summary>
    /// 返回 URL。查看器页面调用 GoBack 时跳转至此地址而非首页。
    /// 由 ArchivePage 等中间页面设置，用完后清空。
    /// </summary>
    string? ReturnUrl { get; set; }
}
