using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;

namespace MauiMultimedia.Viewers.Video.Pages;

public partial class VideoPage : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;

    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".m4v",
        ".3gp", ".ogv", ".mpg", ".mpeg", ".ts", ".mts"
    };

    private readonly string _videoElementId = "video-player";
    private string filePath = "";
    private string fileName = "";
    private string? _blobUrl;          // 当前 blob URL，用于撤销
    private string mimeType = "";
    private bool isLoading = true;
    private string? errorMessage;
    private List<string> fileList = new();
    private int currentIndex = -1;
    private bool _needsSourceUpdate;   // 标记: 渲染后需要设置视频源

    private bool hasPrev => currentIndex > 0;
    private bool hasNext => currentIndex >= 0 && currentIndex < fileList.Count - 1;

    // ═══════════ 生命周期 ═══════════

    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("eval",
            "document.documentElement.style.overflowY = 'hidden'");

        fileList = NavState.CurrentDirectoryFiles?
            .Where(f => Exts.Contains(Path.GetExtension(f))).ToList() ?? new();
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_needsSourceUpdate)
        {
            _needsSourceUpdate = false;
            await SetVideoSourceAsync();
        }

        if (firstRender)
        {
            // 首次渲染：监听视频结束事件
            await JS.InvokeVoidAsync("eval", $@"
                var v = document.getElementById('{_videoElementId}');
                if (v) {{
                    v.addEventListener('ended', function() {{
                        var nextBtn = document.querySelector('button[title=""下一个""]');
                        if (nextBtn && !nextBtn.disabled) nextBtn.click();
                    }});
                }}
            ");
        }
    }

    // ═══════════ 视频源设置 ═══════════

    /// <summary>
    /// 通过 JS 将 blob URL 设置到 video 元素的 src。
    /// 必须在 video 元素已渲染到 DOM 后调用。
    /// </summary>
    private async Task SetVideoSourceAsync()
    {
        if (string.IsNullOrEmpty(_blobUrl)) return;
        var escaped = _blobUrl.Replace("\\", "\\\\").Replace("'", "\\'");
        await JS.InvokeVoidAsync("eval", $@"
            var v = document.getElementById('{_videoElementId}');
            if (v) {{
                v.src = '{escaped}';
                v.load();
            }}
        ");
    }

    /// <summary>清除 video 的当前源并撤销 blob URL</summary>
    private async Task ClearVideoSourceAsync()
    {
        if (!string.IsNullOrEmpty(_blobUrl))
        {
            var escaped = _blobUrl.Replace("\\", "\\\\").Replace("'", "\\'");
            await JS.InvokeVoidAsync("eval", $"URL.revokeObjectURL('{escaped}')");
            _blobUrl = null;
        }
        await JS.InvokeVoidAsync("eval", $@"
            var v = document.getElementById('{_videoElementId}');
            if (v) {{
                v.pause();
                v.removeAttribute('src');
                v.load();
            }}
        ");
    }

    /// <summary>直接读取文件并创建 blob URL</summary>
    private async Task CreateBlobUrlAsync()
    {
        try
        {
            var stream = File.OpenRead(filePath);
            await using var s = stream.ConfigureAwait(false);

            var streamRef = new DotNetStreamReference(stream);
            _blobUrl = await JS.InvokeAsync<string>("createBlobUrl", streamRef, mimeType);
        }
        catch (Exception ex)
        {
            errorMessage = $"加载失败：{ex.Message}";
            Debug.WriteLine($"[Video] CreateBlobUrl failed: {ex}");
        }
    }

    // ═══════════ 文件加载 ═══════════

    private async Task LoadAsync()
    {
        var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
        filePath = NavState.CurrentFilePath
            ?? uri.Query.TrimStart('?').Split('&')
                .Select(p => p.Split('=', 2))
                .Where(kv => kv.Length == 2 && kv[0] == "path")
                .Select(kv => Uri.UnescapeDataString(kv[1]))
                .FirstOrDefault() ?? "";

        if (string.IsNullOrEmpty(filePath))
        {
            errorMessage = "未指定文件路径";
            isLoading = false;
            return;
        }

        fileName = Path.GetFileName(filePath);
        currentIndex = fileList.FindIndex(f =>
            string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        mimeType = GetMimeType(Path.GetExtension(filePath));

        await ReloadVideoAsync();
    }

    /// <summary>切换到当前文件路径的视频（首次加载 + 前后导航均调用）</summary>
    private async Task ReloadVideoAsync()
    {
        isLoading = true;
        errorMessage = null;
        StateHasChanged();

        // 检查文件存在
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            errorMessage = "文件不存在";
            isLoading = false;
            StateHasChanged();
            return;
        }

        // 撤销旧 blob URL + 清空 video 源
        await ClearVideoSourceAsync();

        // 创建新 blob URL
        await CreateBlobUrlAsync();

        if (string.IsNullOrEmpty(errorMessage))
        {
            _needsSourceUpdate = true;   // 渲染后由 OnAfterRenderAsync 设置 src
        }

        isLoading = false;
        StateHasChanged();               // 触发渲染，video 元素出现后设置源
    }

    // ═══════════ 导航 ═══════════

    private void GoBack()
    {
        _ = ClearVideoSourceAsync();
        _ = MauiNav.GoBackAsync();
    }

    private async Task GoPrev()
    {
        if (!hasPrev) return;
        currentIndex--;
        ApplyNavigation();
        await ReloadVideoAsync();
    }

    private async Task GoNext()
    {
        if (!hasNext) return;
        currentIndex++;
        ApplyNavigation();
        await ReloadVideoAsync();
    }

    private void ApplyNavigation()
    {
        filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        mimeType = GetMimeType(Path.GetExtension(filePath));
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

    // ═══════════ 工具方法 ═══════════

    private static string GetMimeType(string extension)
    {
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "mp4" or "m4v" => "video/mp4",
            "webm" => "video/webm",
            "mkv" => "video/x-matroska",
            "mov" => "video/quicktime",
            "avi" => "video/x-msvideo",
            "wmv" => "video/x-ms-wmv",
            "flv" => "video/x-flv",
            "3gp" => "video/3gpp",
            "ogv" => "video/ogg",
            "mpg" or "mpeg" => "video/mpeg",
            "ts" or "mts" => "video/mp2t",
            _ => "video/mp4"
        };
    }
}
