using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;

namespace MauiMultimedia.Viewers.Video.Pages;

public partial class VideoPage : ComponentBase, IAsyncDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IFileServerService FileServer { get; set; } = null!;

    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".m4v",
        ".3gp", ".ogv", ".mpg", ".mpeg", ".ts", ".mts"
    };

    private readonly string _videoElementId = "video-player";
    private IJSObjectReference? _jsModule;
    private string? _lastUrl;          // 上次设置的 URL，用于检测变化
    private string filePath = "";
    private string fileName = "";
    private string? _videoUrl;
    private string? errorMessage;
    private List<string> fileList = new();
    private int currentIndex = -1;

    private bool hasPrev => currentIndex > 0;
    private bool hasNext => currentIndex >= 0 && currentIndex < fileList.Count - 1;

    // ═══════════ 生命周期 ═══════════

    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("eval",
            "document.documentElement.style.overflowY = 'hidden'");

        fileList = NavState.CurrentDirectoryFiles?
            .Where(f => Exts.Contains(Path.GetExtension(f))).ToList() ?? new();
        ResolveFilePath();

        if (string.IsNullOrEmpty(filePath))
        {
            errorMessage = "未指定文件路径";
        }
        else
        {
            fileName = Path.GetFileName(filePath);
            currentIndex = fileList.FindIndex(f =>
                string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
            ReloadVideo();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                // 导入 .razor.js 模块
                _jsModule = await JS.InvokeAsync<IJSObjectReference>("import",
                    "./_content/MauiMultimedia.Viewers.Video/Pages/VideoPage.razor.js");

                // 自动播放下一个
                await _jsModule.InvokeVoidAsync("setupAutoNext", _videoElementId);
            }

            // 每次渲染后检查是否需要更新视频源
            if (_videoUrl != null && _videoUrl != _lastUrl)
            {
                _lastUrl = _videoUrl;
                if (_jsModule != null)
                {
                    var result = await _jsModule.InvokeAsync<string>(
                        "setVideoSource", _videoElementId, _videoUrl);
                    if (result != "ok")
                        errorMessage = $"视频源设置失败：{result}";
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"内部错误：{ex.GetType().Name} - {ex.Message}";
        }
    }

    // ═══════════ 视频源 ═══════════

    private string BuildVideoUrl(string path)
    {
        var encoded = Uri.EscapeDataString(path);
        return $"{FileServer.BaseUrl}/file?path={encoded}";
    }

    // ═══════════ 文件加载 ═══════════

    private void ResolveFilePath()
    {
        var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
        filePath = NavState.CurrentFilePath
            ?? uri.Query.TrimStart('?').Split('&')
                .Select(p => p.Split('=', 2))
                .Where(kv => kv.Length == 2 && kv[0] == "path")
                .Select(kv => Uri.UnescapeDataString(kv[1]))
                .FirstOrDefault() ?? "";
    }

    private void ReloadVideo()
    {
        errorMessage = null;
        if (!File.Exists(filePath))
        {
            errorMessage = "文件不存在";
            return;
        }
        _videoUrl = BuildVideoUrl(filePath);
    }

    // ═══════════ 导航 ═══════════

    private void GoBack()
    {
        _ = StopVideoAsync();
        _ = MauiNav.GoBackAsync();
    }

    private async Task StopVideoAsync()
    {
        if (_jsModule != null)
        {
            try { await _jsModule.InvokeVoidAsync("stopVideo", _videoElementId); }
            catch { }
        }
    }

    private void GoPrev()
    {
        if (!hasPrev) return;
        currentIndex--;
        ApplyNavigation();
        ReloadVideo();
    }

    private void GoNext()
    {
        if (!hasNext) return;
        currentIndex++;
        ApplyNavigation();
        ReloadVideo();
    }

    private void ApplyNavigation()
    {
        filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
    }

    // ═══════════ 键盘 ═══════════

    private void OnPageKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "Escape":
                GoBack();
                break;
        }
    }

    // ═══════════ 资源释放 ═══════════

    public async ValueTask DisposeAsync()
    {
        await StopVideoAsync();
        if (_jsModule != null)
        {
            try { await _jsModule.DisposeAsync(); }
            catch { }
        }
    }
}
