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

    // 鼠标拖拽
    private float panX, panY;
    private bool isDragging;
    private float dragStartX, dragStartY, panAtDragStartX, panAtDragStartY;

    // 触摸状态
    private long touchId1, touchId2;
    private float touchStartDist;
    private float touchStartZoom;
    private float touchMidX, touchMidY;
    private bool isTouchPan;
    private float touchPanStartX, touchPanStartY, touchPanAtDragStartX, touchPanAtDragStartY;

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
            await JS.InvokeVoidAsync("eval",
                "document.querySelector('.image-viewport')?.focus()");
        }
        if (!isLoading && imageWidth > 0)
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
        var qs = uri.Query.TrimStart('?');
        var path = "";
        foreach (var p in qs.Split('&'))
        {
            var kv = p.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "path")
                path = Uri.UnescapeDataString(kv[1]);
        }

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
    }

    private void ResetView()
    {
        displayZoom = 1.0f;
        fitZoom = 1.0f;
        zoomFitMode = true;
        panX = 0;
        panY = 0;
    }

    // ═══════════ 图片加载 + 缓存 + 预加载 ═══════════

    private async Task LoadImageAsync()
    {
        isLoading = true;
        errorMessage = null;
        imageSource = null;
        imageWidth = 0;
        imageHeight = 0;
        fileSizeDisplay = "";
        imageFormat = "";

        try
        {
            // 先查缓存
            var cached = DecodeCache.Get(filePath);
            if (cached.HasValue)
            {
                ApplyDecoded(cached.Value.DataUri, cached.Value.Width, cached.Value.Height);
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

            await Task.Run(() =>
            {
                try
                {
                    var result = ImageProcessingService.DecodeImage(filePath);
                    DecodeCache.Set(filePath, result.DataUri, result.Width, result.Height);
                    ApplyDecoded(result.DataUri, result.Width, result.Height);
                }
                catch
                {
                    imageSource = new Uri(filePath).AbsoluteUri;
                    var dims = ImageProcessingService.GetImageDimensions(filePath);
                    imageWidth = dims.width;
                    imageHeight = dims.height;
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
                var result = await Task.Run(() => ImageProcessingService.DecodeImage(p));
                DecodeCache.Set(p, result.DataUri, result.Width, result.Height);
            }
            catch { /* 预加载失败不影响当前图 */ }
        }
    }

    private async Task<float> CalcFitZoomAsync()
    {
        try
        {
            var dims = await JS.InvokeAsync<double[]>("eval",
                "(() => { var r = document.querySelector('.image-viewport').getBoundingClientRect(); return [r.width, r.height]; })()");
            vpWidth = (float)dims[0];
            vpHeight = (float)dims[1];
            if (vpWidth <= 0 || vpHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return 1.0f;
            return Math.Min(Math.Min(vpWidth / imageWidth, vpHeight / imageHeight), 1.0f);
        }
        catch { return 1.0f; }
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

    private void ZoomIn() { ExitFit(); displayZoom = Math.Min(displayZoom * ZoomStep, MaxZoom); ClampPan(); }
    private void ZoomOut() { ExitFit(); displayZoom = Math.Max(displayZoom / ZoomStep, MinZoom); ClampPan(); }

    private void ZoomFit()
    {
        zoomFitMode = true;
        displayZoom = fitZoom;
        panX = 0; panY = 0;
    }

    private void ZoomActual() { ExitFit(); displayZoom = 1.0f; }

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

    private void OnTouchStart(TouchEventArgs e)
    {
        if (zoomFitMode) return;
        if (e.Touches.Length == 1)
        {
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
        if (zoomFitMode) return;

        // 双指捏合缩放
        if (e.Touches.Length == 2)
        {
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

        // 单指拖拽平移
        if (e.Touches.Length == 1 && isTouchPan)
        {
            panX = touchPanAtDragStartX + (float)(e.Touches[0].ClientX - touchPanStartX);
            panY = touchPanAtDragStartY + (float)(e.Touches[0].ClientY - touchPanStartY);
            ClampPan();
            StateHasChanged();
        }
    }

    private void OnTouchEnd(TouchEventArgs e)
    {
        isTouchPan = false;
    }

    // ═══════════ 键盘 ═══════════

    private void OnKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowLeft": GoPrev(); break;
            case "ArrowRight":
            case " ": GoNext(); break;
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
        _ = JS.InvokeVoidAsync("eval",
            "document.documentElement.style.overflowY = ''");
        Navigation.NavigateTo("/");
    }

    private void GoPrev()
    {
        if (!hasPrev) return;
        currentIndex--; filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        ResetView(); _ = LoadImageAsync();
    }

    private void GoNext()
    {
        if (!hasNext) return;
        currentIndex++; filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        ResetView(); _ = LoadImageAsync();
    }
}
