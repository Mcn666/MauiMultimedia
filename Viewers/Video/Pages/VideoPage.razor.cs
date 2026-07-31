using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Timers;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Utils;

namespace MauiMultimedia.Viewers.Video.Pages;

public partial class VideoPage : ComponentBase, IAsyncDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IMauiOrientation Orientation { get; set; } = null!;
    [Inject] private IFileServerService FileServer { get; set; } = null!;

    private static readonly HashSet<string> Exts = VideoConstants.Exts;

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

    // ── 自定义控件条状态 ──
    private IDisposable? _dotNetRef;
    private bool _isPlaying;
    private double _currentTime;
    private double _duration;
    private float _volume = 1f;
    private bool _isMuted;
    private bool _isFullscreen;

    // 控件条自动隐藏：播放中且用户闲置一段时间后淡出，只留细进度条
    private bool _uiHidden;
    private System.Timers.Timer? _hideTimer;
    private bool _hideTimerInit;
    private const int ControlHideDelayMs = 3000;

    // 当前播放进度百分比（0–100），供常驻细进度条使用
    private double ProgressPercent => _duration > 0 ? Math.Clamp(_currentTime / _duration * 100, 0, 100) : 0;
    private double _playbackRate = 1.0;
    private bool _isLooping;
    private bool _showOverflow;
    // 溢出菜单二级面板：null=主面板，"rate"=倍速子面板，"mode"=播放模式子面板
    private string? _sub;

    // 播放结束行为（互斥三态）：Stop=播完停止 / Loop=循环当前 / AutoNext=自动连播下一个
    private enum PlayEndMode { Stop, Loop, AutoNext }
    private PlayEndMode _playEndMode = PlayEndMode.AutoNext;   // 原默认即为连播，保持兼容

    // 横向滑动拖动进度（scrub）时居中的时间提示（target / duration），.show 控制淡入淡出
    private string? _seekHintText;
    private bool _scrubHintOn;

    // ═══════════ 生命周期 ═══════════

    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("eval",
            "document.documentElement.style.overflowY = 'hidden'");

        fileList = NavState.CurrentDirectoryFiles?
            .Where(f => Exts.Contains(Path.GetExtension(f)))
            .Select(f => { try { return Path.GetFullPath(f); } catch { return f; } })
            .ToList() ?? new();
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

                // 绑定自定义控件条的事件回传（仅首次）
                _dotNetRef ??= DotNetObjectReference.Create(this);
                try
                {
                    await _jsModule.InvokeVoidAsync("initControls", _videoElementId, _dotNetRef);
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
        // 视频查看器自行决定 MIME（沿用标准表；如需覆盖可在注册时传入自定义值）。
        _currentToken = FileServer.RegisterFile(path, MimeTypes.Get(path));
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

    private async Task GoBack()
    {
        _isFullscreen = false;
        try { await Orientation.ExitLandscapeAsync(); } catch { }
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

    // ═══════════ 自定义控件条 ═══════════

    private void TogglePlay() => _ = _jsModule?.InvokeVoidAsync("playPause", _videoElementId);

    // 显示完整控件条（任意交互时调用），并重启隐藏倒计时
    private void ShowControls()
    {
        bool changed = _uiHidden;
        _uiHidden = false;
        if (_isPlaying)
        {
            EnsureHideTimer();
            _hideTimer?.Stop();
            _hideTimer?.Start();
        }
        if (changed) StateHasChanged();
    }

    // 懒初始化隐藏计时器（只在播放且闲置时触发一次淡出）
    private void EnsureHideTimer()
    {
        if (_hideTimerInit) return;
        _hideTimerInit = true;
        _hideTimer = new System.Timers.Timer(ControlHideDelayMs) { AutoReset = false };
        _hideTimer.Elapsed += (_, _) =>
        {
            // 仅在仍在播放、且控件当前可见时才隐藏
            if (_isPlaying && !_uiHidden)
            {
                _uiHidden = true;
                // 控件条淡出时，若溢出菜单（含二级子菜单）仍展开，一并收起，
                // 避免菜单悬在已隐藏的控件条之上，且下次控件条重现时菜单不会自动弹回、遮罩层随之清除
                if (_showOverflow || _sub != null)
                {
                    _showOverflow = false;
                    _sub = null;
                }
                InvokeAsync(StateHasChanged);
            }
        };
    }

    private async Task OnVolumeInput(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), out var v))
        {
            _volume = (float)Math.Clamp(v, 0, 1);
            _isMuted = _volume <= 0;
            if (_jsModule != null) await _jsModule.InvokeVoidAsync("setVolume", _videoElementId, _volume);
            StateHasChanged();
        }
    }

    private async Task ToggleMute()
    {
        _isMuted = !_isMuted;
        if (_jsModule != null) await _jsModule.InvokeVoidAsync("setMuted", _videoElementId, _isMuted);
        StateHasChanged();
    }

    private async Task OnSeekInput(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), out var t) && _jsModule != null)
        {
            await _jsModule.InvokeVoidAsync("setCurrentTime", _videoElementId, t);
            _currentTime = t;
            StateHasChanged();
        }
    }

    private async Task SetRate(double r)
    {
        _playbackRate = r;
        _showOverflow = false;
        _sub = null;
        if (_jsModule != null) await _jsModule.InvokeVoidAsync("setPlaybackRate", _videoElementId, r);
        StateHasChanged();
    }

    // 展开/收起溢出菜单的二级面板（倍速 / 播放模式）
    private void OpenSub(string? which) { _sub = which; StateHasChanged(); }

    // 设置播放结束行为（互斥三态）。Loop 模式同步 video.loop；其余清除 loop。
    private async Task SetMode(PlayEndMode mode)
    {
        _playEndMode = mode;
        _isLooping = mode == PlayEndMode.Loop;
        if (_jsModule != null) await _jsModule.InvokeVoidAsync("setLoop", _videoElementId, _isLooping);
        _showOverflow = false;
        _sub = null;
        ShowControls();
        StateHasChanged();
    }

    private string ModeLabel => _playEndMode switch
    {
        PlayEndMode.Loop => "循环",
        PlayEndMode.AutoNext => "连播",
        _ => "播完停止"
    };

    private void ToggleOverflow()
    {
        _showOverflow = !_showOverflow;
        if (!_showOverflow) _sub = null;
        ShowControls();          // 展开时确保控件条可见并重置隐藏倒计时
        StateHasChanged();
    }

    // 点击菜单以外的任意位置（视频、其它控件、工具栏）关闭溢出菜单。
    // 菜单本身通过 .vp-overflow 的 @onclick:stopPropagation 拦截冒泡，不会触发本方法。
    private void CloseOverflow()
    {
        if (_showOverflow || _sub != null)
        {
            _showOverflow = false;
            _sub = null;
            StateHasChanged();
        }
    }

    private async Task ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        try
        {
            if (_isFullscreen) await Orientation.EnterLandscapeAsync();
            else await Orientation.ExitLandscapeAsync();
        }
        catch { }
        StateHasChanged();
    }

    private void GoNext()
    {
        if (currentIndex < fileList.Count - 1) OnFileSelected(currentIndex + 1);
    }

    private static string FormatTime(double secs)
    {
        if (!double.IsFinite(secs) || secs < 0) secs = 0;
        var t = TimeSpan.FromSeconds(secs);
        return t.Hours > 0
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    // JS 控件条事件回传
    [JSInvokable]
    public void OnPlayingChanged(bool playing)
    {
        _isPlaying = playing;
        if (playing)
        {
            // 开始播放：显示控件，并在倒计时后自动隐藏
            _uiHidden = false;
            EnsureHideTimer();
            _hideTimer?.Stop();
            _hideTimer?.Start();
        }
        else
        {
            // 暂停/播完：立即显示控件，并停止隐藏倒计时
            _uiHidden = false;
            _hideTimer?.Stop();
        }
        StateHasChanged();
    }

    [JSInvokable]
    public void OnTimeUpdate(double t) { _currentTime = t; StateHasChanged(); }

    [JSInvokable]
    public void OnDurationChanged(double d) { _duration = d; StateHasChanged(); }

    [JSInvokable]
    public void OnVolumeChanged(double v, bool muted) { _volume = (float)v; _isMuted = muted; StateHasChanged(); }

    [JSInvokable]
    public void OnEnded() { if (_playEndMode == PlayEndMode.AutoNext) GoNext(); }

    // 点按视频画面（由 JS 区分于点按/滑动后回传，立即执行，无 250ms 延迟）→ 播放/暂停
    [JSInvokable]
    public void OnVideoClick() => TogglePlay();

    // 横向滑动拖动进度开始：显示提示并唤起控件条（便于看到进度条）
    [JSInvokable]
    public void OnScrubStart(double _)
    {
        _scrubHintOn = true;
        ShowControls();
        StateHasChanged();
    }

    // 滑动过程中实时回传目标时间：更新居中提示（target / duration）
    [JSInvokable]
    public void OnScrub(double t)
    {
        _seekHintText = $"{FormatTime(t)} / {FormatTime(_duration)}";
        StateHasChanged();
    }

    // 滑动结束：提交最终进度，淡出提示
    [JSInvokable]
    public void OnScrubEnd(double t)
    {
        _currentTime = t;
        _scrubHintOn = false;
        ShowControls();
        StateHasChanged();
    }

    // ═══════════ 键盘 ═══════════

    private void OnPageKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
        case "Escape":
            _ = GoBack();
            break;
        }
    }

    // ═══════════ 资源释放 ═══════════

    public async ValueTask DisposeAsync()
    {
        if (_hideTimer != null)
        {
            try { _hideTimer.Stop(); _hideTimer.Dispose(); } catch { }
            _hideTimer = null;
        }
        try { await Orientation.ExitLandscapeAsync(); } catch { }
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
        if (_dotNetRef != null)
        {
            try { _dotNetRef.Dispose(); } catch { }
            _dotNetRef = null;
        }
    }

    // ═══════════ 诊断（已移除：定位期用的 JS console 镜像与界面面板） ═══════════
}
