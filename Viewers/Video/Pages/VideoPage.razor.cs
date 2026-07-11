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
        ".3gp", ".ogv", ".mpg", ".mpeg", ".ts", ".mts", ".m2ts"
    };

    private readonly string _videoElementId = "video-player";
    private IJSObjectReference? _jsModule;
    private string? _lastUrl;          // 上次设置的 URL，用于检测变化
    private string filePath = "";
    private string fileName = "";
    private string? _videoUrl;
    private string _mediaType = "native"; // native | flv | mpegts，决定 JS 侧走原生还是 mpegts.js 软解
    private string? _currentToken;     // 当前视频对应的文件服务令牌，离开时注销
    private string? errorMessage;
    private List<string> fileList = new();
    private int currentIndex = -1;

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
                // 导入 .razor.js 模块（RCL 静态资源用绝对路径 /_content/{Assembly}/...）
                _jsModule = await JS.InvokeAsync<IJSObjectReference>("import",
                    "/_content/MauiMultimedia.Viewers.Video/Pages/VideoPage.razor.js");

                // 自动播放下一个（失败不影响主流程）
                try
                {
                    await _jsModule.InvokeVoidAsync("setupAutoNext", _videoElementId);
                }
                catch { }
            }

            // 仅在 JS 模块已就绪且视频源真正变化时才调用 setVideoSource。
            // firstRender 的 import 是异步挂起的，挂起期间 Blazor 可能先触发一次
            // firstRender=false 的渲染，此时 _jsModule 仍为 null；若不加 _jsModule!=null
            // 守卫会带着 null 去调 setVideoSource 抛 ArgumentNullException。模块就绪的那次
            // 渲染会自然走到这里。用 _lastUrl 守，避免重复调用。
            if (_jsModule != null && _videoUrl != null && _videoUrl != _lastUrl)
            {
                _lastUrl = _videoUrl;
                var result = await _jsModule.InvokeAsync<string>(
                    "setVideoSource", _videoElementId, _videoUrl, _mediaType);
                if (result != "ok")
                {
                    // 把 JS 侧的真实错误原样呈现（含“请转码”提示）
                    errorMessage = result.StartsWith("error:")
                        ? result.Substring("error:".Length)
                        : $"视频源设置失败：{result}";
                    StateHasChanged();
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
        // 通过文件服务注册令牌，URL 只携带令牌而非裸路径，
        // WebView 内 JS 无法据此构造其他文件的访问 URL。
        _currentToken = FileServer.RegisterFile(path);
        return $"{FileServer.BaseUrl}/file?token={_currentToken}";
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

        // 切换视频前先注销旧令牌，避免令牌堆积
        if (_currentToken != null)
        {
            FileServer.UnregisterFile(_currentToken);
            _currentToken = null;
        }

        // 根据容器决定播放路径：FLV / MPEG-TS 浏览器原生不支持，走 mpegts.js 软解
        _mediaType = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant() switch
        {
            "flv" => "flv",
            "ts" or "mts" or "m2ts" => "mpegts",
            _ => "native"
        };

        try
        {
            _videoUrl = BuildVideoUrl(filePath);
        }
        catch (Exception ex)
        {
            errorMessage = $"无法提供视频文件：{ex.Message}";
            _videoUrl = null;
        }
    }

    // ═══════════ 导航 ═══════════

    private void OnFileSelected(int index)
    {
        if (index < 0 || index >= fileList.Count || index == currentIndex) return;
        currentIndex = index;
        ApplyNavigation();
        ReloadVideo();
    }

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
        if (_currentToken != null)
        {
            FileServer.UnregisterFile(_currentToken);
            _currentToken = null;
        }
        if (_jsModule != null)
        {
            try { await _jsModule.DisposeAsync(); }
            catch { }
        }
    }

    // ═══════════ 诊断（已移除：定位期用的 JS console 镜像与界面面板） ═══════════
}
