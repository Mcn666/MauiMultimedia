using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Viewers.Image.Services;

namespace MauiMultimedia.Viewers.Image.Pages;

public partial class ImagePage : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IFileServerService FileServer { get; set; } = null!;

    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".ico", ".tiff", ".tif", ".svg", ".avif"
    };

    private string filePath = "";
    private string fileName = "";
    private string? imageSource;
    private bool isLoading = true;
    private string? errorMessage;
    private List<string> fileList = new();
    private int currentIndex = -1;

    private int imageWidth;
    private int imageHeight;
    private string fileSizeDisplay = "";
    private string imageFormat = "";
    private string? _fileCreationTime;
    private string? _fileLastWriteTime;
    private bool showDetails;

    // 拼接模式
    private bool stitchMode;
    private bool canStitch;
    private string? stitchError;
    private List<StitchImageInfo>? stitchImages;
    private int _stitchLoadedCount;
    private CancellationTokenSource? _stitchCts;
    private class StitchImageInfo
    {
        public string BlobUrl { get; set; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public string FileName { get; init; } = "";
    }
    private float _stitchZoom = 1.0f;

    // 缩放
    private const float ZoomStep = 1.5f;
    private const float MaxZoom = 10.0f;
    private float displayZoom = 1.0f;
    private float fitZoom = 1.0f;
    private bool zoomFitMode = true;
    private float MinZoom => fitZoom;

    // 视口尺寸
    private float vpWidth;
    private float vpHeight;
    private float _dpr = 1f;

    // 鼠标拖拽
    private float panX, panY;
    private bool isDragging;
    private float dragStartX, dragStartY, panAtDragStartX, panAtDragStartY;

    // 触摸状态（部分在新方法区声明以匹配类型变化）
    private bool isTouchPan;

    private IJSObjectReference? _jsModule;

    // 解码失败回退时通过文件服务令牌暴露图片（避免 Android WebView 无法加载 file://），
    // 切换图片或离开页面时注销，防止令牌堆积。
    private string? _imageToken;

    private bool hasPrev => currentIndex > 0;
    private bool hasNext => currentIndex >= 0 && currentIndex < fileList.Count - 1;

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
        if (firstRender)
        {
            try
            {
                var asm = typeof(ImagePage).Assembly.GetName().Name!;
                _jsModule = await JS.InvokeAsync<IJSObjectReference>("import",
                    $"./_content/{asm}/Pages/ImagePage.razor.js");
                if (!stitchMode)
                    await _jsModule.InvokeVoidAsync("focusViewport");
            }
            catch { }
        }
        if (!isLoading && imageWidth > 0 && !stitchMode)
        {
            var fz = await CalcFitZoomAsync();
            if (Math.Abs(fz - fitZoom) > 0.001f)
            {
                fitZoom = fz;
                if (zoomFitMode)
                {
                    displayZoom = fitZoom;
                    StateHasChanged();
                }
            }
        }
    }

    private async Task LoadAsync()
    {
        var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
        var path = NavState.CurrentFilePath
            ?? uri.Query.TrimStart('?').Split('&')
                .Select(p => p.Split('=', 2))
                .Where(kv => kv.Length == 2 && kv[0] == "path")
                .Select(kv => Uri.UnescapeDataString(kv[1]))
                .FirstOrDefault() ?? "";

        if (string.IsNullOrEmpty(path))
        {
            errorMessage = "未指定文件路径";
            isLoading = false;
            return;
        }

        filePath = path;
        fileName = Path.GetFileName(filePath);
        currentIndex = fileList.FindIndex(f =>
            string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        ResetView();
        await LoadImageAsync();
        _ = CheckCanStitchAsync();
    }

    private void ResetView()
    {
        displayZoom = 1.0f;
        fitZoom = 1.0f;
        zoomFitMode = true;
        panX = 0;
        panY = 0;
    }

    /// <summary>
    /// 异步检查所有图片宽度是否相似，结果通过 canStitch 通知 UI
    /// </summary>
    private async Task CheckCanStitchAsync()
    {
        canStitch = false;
        if (fileList.Count < 2) return;

        // 先让 UI 完成当前渲染
        await Task.Yield();

        var widths = new List<int>();
        foreach (var p in fileList)
        {
            var c = DecodeCache.Get(p);
            if (c.HasValue)
                widths.Add(c.Value.Width);
        }
        if (widths.Count < 2) return;

        // ≥2 张即可拼接（不同宽度的各自按原生比例显示）
        canStitch = true;

        // 通知 UI 拼接按钮已就绪。
        // 必须在 UI 调度器上刷新：上面 Task.Yield() 之后代码跑在线程池线程，
        // 直接 StateHasChanged() 会抛 InvalidOperationException（未关联 Dispatcher）。
        await InvokeAsync(StateHasChanged);
    }

    private void ToggleDetails() => showDetails = !showDetails;
    private void CloseDetails() => showDetails = false;

    // ═══════════ 拼接模式 ═══════════

    /// <summary>撤销所有 blob URL，取消后台加载</summary>
    private async Task RevokeBlobUrls()
    {
        _stitchCts?.Cancel();
        _stitchCts = null;

        if (stitchImages != null)
        {
            var urls = stitchImages.Select(si => si.BlobUrl)
                .Where(u => !string.IsNullOrEmpty(u)).ToArray();
            if (urls.Length > 0 && _jsModule != null)
                await _jsModule.InvokeVoidAsync("revokeBlobUrls", urls);
        }
    }

    private static string GetMimeType(string ext) => ext switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "bmp" => "image/bmp",
        _ => "image/jpeg"
    };

    private async Task ToggleStitch()
    {
        stitchMode = !stitchMode;
        // 继承当前单图浏览的缩放比
        _stitchZoom = Math.Clamp(displayZoom, 0.3f, 3.0f);
        if (!stitchMode)
        {
            _stitchCts?.Cancel();
            await RevokeBlobUrls();
            stitchImages = null;
            stitchError = null;
            _stitchLoadedCount = 0;
            return;
        }

        // 收集尺寸（高度仅用于渲染 aspect-ratio，不影响可用性）
        var widths = new List<int>();
        var dims = new List<(string path, int w, int h)>();
        foreach (var p in fileList)
        {
            var c = DecodeCache.Get(p);
            if (c.HasValue)
            {
                widths.Add(c.Value.Width);
                dims.Add((p, c.Value.Width, c.Value.Height));
            }
            else
            {
                var d = await Task.Run(() => ImageProcessingService.GetImageDimensions(p));
                widths.Add(d.width);
                dims.Add((p, d.width, d.height));
            }
        }
        if (dims.Count < 2)
        {
            stitchError = "至少需要 2 张图片才能拼接";
            return;
        }

        // 接受所有宽度（竖页窄、跨页宽各自显示，浏览器自适应）
        stitchImages = dims.Select(d => new StitchImageInfo { BlobUrl = "", Width = d.w, Height = d.h, FileName = Path.GetFileName(d.path) }).ToList();
        _stitchLoadedCount = 0;
        stitchError = null;
        StateHasChanged();

        // 分批加载 blob URL：先加载第一屏（12 张）
        _stitchCts = new CancellationTokenSource();
        var cts = _stitchCts; // 局部引用，防止并发退出时被第二调用清空
        try
        {
            await LoadStitchBatchAsync(0, Math.Min(12, fileList.Count), cts.Token);
            if (!cts.IsCancellationRequested)
                _ = LoadRemainingStitchBatchesAsync(12, cts.Token);

            await Task.Delay(50);
            if (_jsModule != null)
                await _jsModule.InvokeVoidAsync("scrollStitchIntoView", currentIndex);
        }
        catch (OperationCanceledException) { }
        catch (NullReferenceException) { }
    }

    /// <summary>加载一批 blob URL</summary>
    private async Task LoadStitchBatchAsync(int start, int end, CancellationToken ct)
    {
        for (int i = start; i < end && i < fileList.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // 退出拼接后 stitchImages 被清空，跳过后续加载
                if (stitchImages == null) return;
                if (!string.IsNullOrEmpty(stitchImages[i].BlobUrl)) continue;

                var path = fileList[i];
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var _ = stream.ConfigureAwait(false);
                if (stitchImages == null) return;

                var streamRef = new DotNetStreamReference(stream);
                var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var blobUrl = await JS.InvokeAsync<string>("createBlobUrl", streamRef, GetMimeType(ext));
                if (stitchImages == null) { await RevokeBlobUrlAsync(blobUrl); return; }

                stitchImages[i].BlobUrl = blobUrl;
                _stitchLoadedCount++;
                await InvokeAsync(StateHasChanged);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                Debug.WriteLine($"[Stitch] Failed to load: {fileList[i]}");
            }
        }
    }

    /// <summary>撤销单个 blob URL</summary>
    private async Task RevokeBlobUrlAsync(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            if (_jsModule != null)
                await _jsModule.InvokeVoidAsync("revokeBlobUrls", new object[] { url });
            else
                await JS.InvokeVoidAsync("eval", $"URL.revokeObjectURL('{url}')");
        }
    }

    /// <summary>后台分批加载剩余图片</summary>
    private async Task LoadRemainingStitchBatchesAsync(int startIndex, CancellationToken ct)
    {
        const int batchSize = 10;
        try
        {
            for (int i = startIndex; i < fileList.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                await LoadStitchBatchAsync(i, Math.Min(i + batchSize, fileList.Count), ct);
                await Task.Delay(30, ct); // 让 UI 喘息
            }
        }
        catch (OperationCanceledException) { }
    }

    // ═══════════ 图片加载 + 缓存 + 预加载 ═══════════

    private async Task LoadImageAsync()
    {
        isLoading = true;
        errorMessage = null;
        imageSource = null;
        // 回收上一张图片可能注册的回退令牌（若存在）
        if (_imageToken != null)
        {
            try { FileServer.UnregisterFile(_imageToken); } catch { }
            _imageToken = null;
        }
        imageWidth = 0;
        imageHeight = 0;
        fileSizeDisplay = "";
        imageFormat = "";
        _fileCreationTime = null;
        _fileLastWriteTime = null;

        try
        {
            // 先查缓存
            var cached = DecodeCache.Get(filePath);
            if (cached.HasValue)
            {
                ApplyDecoded(cached.Value.DataUri, cached.Value.Width, cached.Value.Height);
                // 缓存命中时也要读取文件元数据（大小/格式/时间）
                try
                {
                    var fi = new FileInfo(filePath);
                    fileSizeDisplay = ImageProcessingService.FormatFileSize(fi.Length);
                    imageFormat = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
                    _fileCreationTime = fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                    _fileLastWriteTime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch { Debug.WriteLine($"[Load] FileInfo read failed for: {fileName}"); }
                isLoading = false;
                StateHasChanged();
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                errorMessage = "文件不存在";
                return;
            }

            fileSizeDisplay = ImageProcessingService.FormatFileSize(fileInfo.Length);
            imageFormat = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            _fileCreationTime = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
            _fileLastWriteTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

            await Task.Run(async () =>
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(filePath);
                    var result = ImageProcessingService.DecodeImage(bytes, fileName);
                    DecodeCache.Set(filePath, result.DataUri, result.Width, result.Height);
                    ApplyDecoded(result.DataUri, result.Width, result.Height);
                }
                catch
                {
                    Debug.WriteLine($"[Load] DecodeImage fallback for: {fileName}");
                    try
                    {
                        // 解码失败回退：通过本地文件服务令牌暴露图片，使 Android WebView 也能加载
                        // （直接 file:// 在 Android WebView 中无法访问任意路径，会显示空白）。
                        _imageToken = FileServer.RegisterFile(filePath);
                        imageSource = $"{FileServer.BaseUrl}/file?token={_imageToken}";
                        var bytes = await File.ReadAllBytesAsync(filePath);
                        var dims = ImageProcessingService.GetImageDimensions(bytes);
                        imageWidth = dims.width;
                        imageHeight = dims.height;
                    }
                    catch
                    {
                        _imageToken = null;
                        imageSource = null;
                        errorMessage = $"无法解码该图片：{fileName}";
                        Debug.WriteLine($"[Load] Fallback failed for: {fileName}");
                    }
                }
            });

            // 当前图加载完成 → 后台预加载前后图片
            _ = PreloadAdjacentAsync();
        }
        catch (Exception ex)
        {
            errorMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            isLoading = false;
            _ = CheckCanStitchAsync();
            StateHasChanged();
        }
    }

    private void ApplyDecoded(string dataUri, int w, int h)
    {
        imageSource = dataUri;
        imageWidth = w;
        imageHeight = h;
    }

    /// <summary>
    /// 后台预加载上一张和下一张图片，存入缓存
    /// </summary>
    private async Task PreloadAdjacentAsync()
    {
        var paths = new List<string>(2);
        if (currentIndex > 0) paths.Add(fileList[currentIndex - 1]);
        if (currentIndex < fileList.Count - 1) paths.Add(fileList[currentIndex + 1]);

        foreach (var p in paths)
        {
            if (DecodeCache.Get(p).HasValue) continue; // 已缓存
            try
            {
                var bytes = await File.ReadAllBytesAsync(p);
                var result = await Task.Run(() => ImageProcessingService.DecodeImage(bytes, Path.GetFileName(p)));
                DecodeCache.Set(p, result.DataUri, result.Width, result.Height);
            }
            catch
            {
                Debug.WriteLine($"[Preload] Failed: {p}");
            }
        }
        // 预加载完成后重新检查拼接条件（首次加载时缓存只有1张，无法判定）
        await CheckCanStitchAsync();
    }

    private async Task<float> CalcFitZoomAsync()
    {
        try
        {
            var dims = _jsModule != null
                ? await _jsModule.InvokeAsync<double[]>("getViewportMetrics")
                : await JS.InvokeAsync<double[]>("eval",
                    "(() => { var v = document.querySelector('.image-viewport'); " +
                    "return [v.offsetWidth, v.offsetHeight, window.devicePixelRatio || 1]; })()");
            vpWidth = (float)dims[0];
            vpHeight = (float)dims[1];
            _dpr = (float)dims[2];
            if (vpWidth <= 0 || vpHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return 1.0f;

            // 图片适配视口所需的 CSS 缩放（上限 CSS 1:1，不过度放大）
            float cssFitW = vpWidth / imageWidth;
            float cssFitH = vpHeight / imageHeight;
            return Math.Min(Math.Min(cssFitW, cssFitH), 1.0f);
        }
        catch
        {
            Debug.WriteLine("[Zoom] CalcFitZoom JS interop failed");
            return 1.0f;
        }
    }

    private void ClampPan()
    {
        float dispW = imageWidth * displayZoom;
        float dispH = imageHeight * displayZoom;
        float maxX = Math.Max(0, (dispW - vpWidth) / 2);
        float maxY = Math.Max(0, (dispH - vpHeight) / 2);
        panX = Math.Clamp(panX, -maxX, maxX);
        panY = Math.Clamp(panY, -maxY, maxY);
    }

    // ═══════════ 缩放控制 ═══════════

    private string GetZoomStyle()
    {
        if (zoomFitMode) return "";
        return $"transform: translate({panX:F1}px, {panY:F1}px) scale({displayZoom:F4});";
    }

    private void ExitFit()
    {
        zoomFitMode = false;
        panX = 0;
        panY = 0;
    }

    private void ZoomIn()
    {
        if (stitchMode) { _stitchZoom = Math.Min(_stitchZoom * 1.5f, 5.0f); StateHasChanged(); return; }
        ExitFit(); displayZoom = Math.Min(displayZoom * ZoomStep, MaxZoom); ClampPan();
    }
    private void ZoomOut()
    {
        if (stitchMode) { _stitchZoom = Math.Max(_stitchZoom / 1.5f, 0.1f); StateHasChanged(); return; }
        ExitFit(); displayZoom = Math.Max(displayZoom / ZoomStep, MinZoom); ClampPan();
    }

    private void ZoomFit()
    {
        if (stitchMode)
        {
            _stitchZoom = imageWidth > 0 ? Math.Max(vpWidth / imageWidth, 0.1f) : 1.0f;
            StateHasChanged(); return;
        }
        zoomFitMode = true;
        displayZoom = fitZoom;
        panX = 0; panY = 0;
    }

    private void ZoomActual()
    {
        if (stitchMode) { _stitchZoom = GetOneToOneZoom(); StateHasChanged(); return; }
        ExitFit();
        displayZoom = GetOneToOneZoom();
    }

    /// <summary>物理像素 1:1 对应的缩放值（1 图像像素 = 1 物理像素）</summary>
    private float GetOneToOneZoom() => Math.Max(1.0f / _dpr, 0.01f);

    /// <summary>
    /// 在适应和1:1之间切换（合并按钮）
    /// </summary>
    private void ToggleFitActual()
    {
        if (zoomFitMode) ZoomActual(); else ZoomFit();
        StateHasChanged();
    }

    private string GetDisplayZoom()
    {
        var zoom = stitchMode ? _stitchZoom : displayZoom;
        return $"{zoom * _dpr * 100:F0}%";
    }

    private string GetFitToggleText()
    {
        if (stitchMode) return _stitchZoom >= 1.0f ? "适应" : "1:1";
        return zoomFitMode ? "1:1" : "适应";
    }

    private string GetFitToggleTitle()
    {
        if (stitchMode) return _stitchZoom >= 1.0f ? "适应窗口" : "1:1";
        return zoomFitMode ? "1:1" : "适应窗口";
    }

    private void OnWheel(WheelEventArgs e)
    {
        float cx = (float)e.ClientX;
        float cy = (float)e.ClientY;

        if (zoomFitMode) { zoomFitMode = false; panX = 0; panY = 0; }

        float oldZoom = displayZoom;
        float oldPanX = panX;
        float oldPanY = panY;

        displayZoom = e.DeltaY < 0
            ? Math.Min(displayZoom * ZoomStep, MaxZoom)
            : Math.Max(displayZoom / ZoomStep, MinZoom);

        if (vpWidth > 0 && vpHeight > 0 && oldZoom > 0.001f)
        {
            float localX = (cx - vpWidth / 2f - oldPanX) / oldZoom;
            float localY = (cy - vpHeight / 2f - oldPanY) / oldZoom;
            panX = cx - vpWidth / 2f - localX * displayZoom;
            panY = cy - vpHeight / 2f - localY * displayZoom;
        }

        ClampPan();
        StateHasChanged();
    }

    private void OnDoubleClick(MouseEventArgs e)
    {
        if (zoomFitMode) ZoomActual(); else ZoomFit();
        StateHasChanged();
    }

    // ═══════════ 鼠标拖拽 ═══════════

    private void OnPointerDown(PointerEventArgs e)
    {
        if (zoomFitMode) return;
        isDragging = true;
        dragStartX = (float)e.ClientX;
        dragStartY = (float)e.ClientY;
        panAtDragStartX = panX;
        panAtDragStartY = panY;
    }

    private void OnPointerMove(PointerEventArgs e)
    {
        if (!isDragging) return;
        panX = panAtDragStartX + (float)(e.ClientX - dragStartX);
        panY = panAtDragStartY + (float)(e.ClientY - dragStartY);
        ClampPan();
        StateHasChanged();
    }

    private void OnPointerUp(PointerEventArgs e) => isDragging = false;

    // ═══════════ 触摸手势 ═══════════

    private float touchStartX, touchStartY;
    private float touchPanStartX, touchPanStartY;
    private float touchPanAtDragStartX, touchPanAtDragStartY;
    private long touchId1, touchId2;
    private float touchMidX, touchMidY;
    private float touchStartDist;
    private float touchStartZoom;
    private float touchSwipeDx; // 累计水平滑动距离，用于左滑右滑翻页

    private void OnTouchStart(TouchEventArgs e)
    {
        touchSwipeDx = 0;
        if (e.Touches.Length == 1)
        {
            touchStartX = (float)e.Touches[0].ClientX;
            touchStartY = (float)e.Touches[0].ClientY;
            isTouchPan = true;
            touchId1 = e.Touches[0].Identifier;
            touchPanStartX = (float)e.Touches[0].ClientX;
            touchPanStartY = (float)e.Touches[0].ClientY;
            touchPanAtDragStartX = panX;
            touchPanAtDragStartY = panY;
        }
        else if (e.Touches.Length == 2)
        {
            isTouchPan = false;
            touchId1 = e.Touches[0].Identifier;
            touchId2 = e.Touches[1].Identifier;
            touchMidX = (float)((e.Touches[0].ClientX + e.Touches[1].ClientX) / 2);
            touchMidY = (float)((e.Touches[0].ClientY + e.Touches[1].ClientY) / 2);
            float dx = (float)(e.Touches[0].ClientX - e.Touches[1].ClientX);
            float dy = (float)(e.Touches[0].ClientY - e.Touches[1].ClientY);
            touchStartDist = MathF.Sqrt(dx * dx + dy * dy);
            touchStartZoom = displayZoom;
        }
    }

    private void OnTouchMove(TouchEventArgs e)
    {
        // 双指捏合缩放（任何模式下都支持）
        if (e.Touches.Length == 2)
        {
            isTouchPan = false;
            float dx = (float)(e.Touches[0].ClientX - e.Touches[1].ClientX);
            float dy = (float)(e.Touches[0].ClientY - e.Touches[1].ClientY);
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (touchStartDist > 0)
            {
                float oldZoom = displayZoom;
                displayZoom = Math.Clamp(touchStartZoom * (dist / touchStartDist), MinZoom, MaxZoom);

                // 以双指中点为缩放中心
                float mx = (float)((e.Touches[0].ClientX + e.Touches[1].ClientX) / 2);
                float my = (float)((e.Touches[0].ClientY + e.Touches[1].ClientY) / 2);

                if (vpWidth > 0 && vpHeight > 0 && oldZoom > 0.001f)
                {
                    float localX = (mx - vpWidth / 2f - panX) / oldZoom;
                    float localY = (my - vpHeight / 2f - panY) / oldZoom;
                    panX = mx - vpWidth / 2f - localX * displayZoom;
                    panY = my - vpHeight / 2f - localY * displayZoom;
                }

                ClampPan();
                StateHasChanged();
            }
            return;
        }

        // 单指：适应模式下累计滑动距离，非适应模式拖拽平移
        if (e.Touches.Length == 1 && isTouchPan)
        {
            float cx = (float)e.Touches[0].ClientX;

            if (zoomFitMode)
            {
                // 适应模式：只记录滑动距离，拖拽手感轻反馈
                touchSwipeDx = cx - touchStartX;
            }
            else
            {
                // 非适应模式：拖拽平移
                panX = touchPanAtDragStartX + (float)(e.Touches[0].ClientX - touchPanStartX);
                panY = touchPanAtDragStartY + (float)(e.Touches[0].ClientY - touchPanStartY);
                ClampPan();
                StateHasChanged();
            }
        }
    }

    private void OnTouchEnd(TouchEventArgs e)
    {
        isTouchPan = false;

        // 适应模式下检测左滑右滑翻页
        if (zoomFitMode && Math.Abs(touchSwipeDx) > 60)
        {
            _ = touchSwipeDx > 0 ? GoPrev() : GoNext();
        }
    }

    // ═══════════ 键盘 ═══════════

    private void OnKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowLeft": _ = GoPrev(); break;
            case "ArrowRight":
            case " ": _ = GoNext(); break;
            case "ArrowUp":
            case "=": ZoomIn(); StateHasChanged(); break;
            case "ArrowDown":
            case "-": ZoomOut(); StateHasChanged(); break;
            case "Escape": GoBack(); break;
        }
    }

    // ═══════════ 导航（优先查缓存） ═══════════

    private void GoBack()
    {
        stitchMode = false; stitchImages = null;
        if (_imageToken != null)
        {
            try { FileServer.UnregisterFile(_imageToken); } catch { }
            _imageToken = null;
        }
        _ = MauiNav.GoBackAsync();
    }

    // 滑动切换动画（CSS @keyframes + JS animationend Promise 驱动）
    private string _slideAniClass = "";
    private bool _isAnimating;

    /// <summary>等待当前 img-slide 的 animationend 事件</summary>
    private Task WaitSlideAnimation()
    {
        if (_jsModule != null)
            return _jsModule.InvokeAsync<object>("waitAnimationEnd").AsTask();
        return JS.InvokeAsync<object>("eval", @"
            new Promise(r => {
                var el = document.querySelector('.img-slide');
                if (!el) { r(); return; }
                el.addEventListener('animationend', () => r(), {once:true});
            })").AsTask();
    }

    private async Task SetSlideTransformAsync(string transform)
    {
        if (_jsModule != null)
            await _jsModule.InvokeVoidAsync("setSlideTransform", transform);
        else
            await JS.InvokeVoidAsync("eval",
                $"document.querySelector('.img-slide')?.style.setProperty('transform','{transform}')");
    }

    private async Task ClearSlideTransformAsync()
    {
        if (_jsModule != null)
            await _jsModule.InvokeVoidAsync("clearSlideTransform");
        else
            await JS.InvokeVoidAsync("eval",
                "document.querySelector('.img-slide')?.style.removeProperty('transform')");
    }

    private async Task GoPrev()
    {
        if (!hasPrev || _isAnimating) return;
        _isAnimating = true;
        stitchMode = false; stitchImages = null;

        // Phase 1: 滑出（向右 +100%）
        _slideAniClass = "slide-out-right";
        StateHasChanged();
        await Task.Delay(16);
        await WaitSlideAnimation();

        // 定位到左侧外（-100%），为滑入做准备
        await SetSlideTransformAsync("-100%");
        _slideAniClass = "";

        // Phase 2: 切换图片
        currentIndex--; filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        StateHasChanged();
        await Task.Delay(16);

        // Phase 3: 从左侧滑入（向左）
        _slideAniClass = "slide-in-from-left";
        StateHasChanged();
        await Task.Delay(16);
        await WaitSlideAnimation();

        // 清理
        await ClearSlideTransformAsync();
        _slideAniClass = "";
        StateHasChanged();
        _isAnimating = false;
    }

    private async Task GoNext()
    {
        if (!hasNext || _isAnimating) return;
        _isAnimating = true;
        stitchMode = false; stitchImages = null;

        // Phase 1: 滑出（向左 -100%）
        _slideAniClass = "slide-out-left";
        StateHasChanged();
        await Task.Delay(16);
        await WaitSlideAnimation();

        // 定位到右侧外（+100%），为滑入做准备
        await SetSlideTransformAsync("100%");
        _slideAniClass = "";

        // Phase 2: 切换图片
        currentIndex++; filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        StateHasChanged();
        await Task.Delay(16);

        // Phase 3: 从右侧滑入
        _slideAniClass = "slide-in-from-right";
        StateHasChanged();
        await Task.Delay(16);
        await WaitSlideAnimation();

        // 清理
        await ClearSlideTransformAsync();
        _slideAniClass = "";
        StateHasChanged();
        _isAnimating = false;
    }
}
