using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Utils;
using MauiMultimedia.Viewers.Image.Components;
using MauiMultimedia.Viewers.Image.Services;
using System.Collections.Concurrent;

namespace MauiMultimedia.Viewers.Image.Pages;

public partial class ImagePage : ComponentBase, IAsyncDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IFileServerService FileServer { get; set; } = null!;
    [Inject] private IFileSystemService FileSystem { get; set; } = null!;

    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".ico", ".tiff", ".tif", ".svg", ".avif", ".dds"
    };

    // ── File state ──
    private string filePath = "";
    private string fileName = "";
    private string? imageSource;
    // Blurred low-res preview shown behind the full image while it loads (P4).
    private string? placeholderSource;
    // True when the current image is already available instantly (cached
    // data:URI). Signalled to ImageViewport so it skips the fade-in — avoids
    // a post-navigation-animation flash. Reset each LoadImageAsync.
    private bool _instantLoad;
    private bool isLoading = true;
    private string? errorMessage;
    private List<string> fileList = new();
    private int currentIndex = -1;
    private int imageWidth, imageHeight;
    private string fileSizeDisplay = "";
    private string imageFormat = "";
    private string? _fileCreationTime;
    private string? _fileLastWriteTime;

    // ── Immersive mode ──
    private bool showToolbar = true;
    private bool showHUD = true;
    private bool showFilmstrip = true;
    private bool showDetails;
    private Timer? _hudTimer;
    private DotNetObjectReference<ImagePage>? _dotNetRef;

    // ── Filmstrip ──
    private List<string> _filmstripThumbnails = new();

    // ── Child components ──

    // ── Stitch mode ──
    private bool stitchMode;
    private bool canStitch;
    // Diagnostic toggle: set FALSE to disable nav animations (cylinder / flip)
    // for isolating jitter to the zoom-calc path. Normally TRUE.
    private bool _navAnimationEnabled = true;
    private string? stitchError;
    private List<StitchImageInfo>? stitchImages;
    private int _stitchLoadedCount;
    private CancellationTokenSource? _stitchCts;
    private class StitchImageInfo
    {
        // Served HTTP URL (token-based, Range-streamed from disk) instead of a
        // blob: URL — avoids reading the whole file into memory. The previous
        // createBlobUrl bridge did streamRef.arrayBuffer() of the entire image.
        public string Url { get; set; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public string FileName { get; init; } = "";
    }
    private float _stitchZoom = 1.0f;
    private double[] _stitchTop = Array.Empty<double>();
    private double _stitchTotalHeight;
    private int _stitchVisibleStart;
    private int _stitchVisibleEnd;
    private double _stitchContentWidth;
    private double _stitchViewportH;
    private bool _stitchScrollPending;
    private int _stitchGap = 4;
    private const int StitchWindowPad = 6;

    // ── Zoom ──
    private const float ZoomStep = 1.5f;
    private const float MaxZoom = 10.0f;
    private float displayZoom = 1.0f;
    private float fitZoom = 1.0f;
    private bool zoomFitMode = true;
    private float MinZoom => fitZoom;
    private float vpWidth, vpHeight;
    private float _dpr = 1f;
    // Viewport top-left in window coordinates (from getBoundingClientRect).
    // Used to convert e.ClientX/Y (window coords) into viewport-local coords
    // for cursor-anchored zoom. The top bar pushes the viewport down (~44px),
    // so without this the zoom anchor drifts vertically. Left is normally 0.
    private float _vpOffX, _vpOffY;
    // True ONLY once a REAL fit/1:1 zoom has been computed (both the image
    // dimensions AND the viewport geometry were known). Drives ZoomReady: the
    // image stays hidden until this is true, so it can never flash a default
    // 1.0 scale (200% on HiDPI) on the first frame.
    private bool _zoomComputed;

    // ── Drag ──
    private float panX, panY;
    private bool isDragging;
    private float dragStartX, dragStartY, panAtDragStartX, panAtDragStartY;
    // Tracks the unclamped (desired) panX during drag, used to compute overscroll
    // past the pan boundary for edge-based navigation when zoomed in.
    private float _dragDesiredPanX;

    // ── Touch ──
    private bool isTouchPan;
    private float touchStartX, touchStartY;
    private float touchPanStartX, touchPanStartY;
    private float touchPanAtDragStartX, touchPanAtDragStartY;
    private long touchId1, touchId2;
    private float touchMidX, touchMidY;
    private float touchStartDist;
    private float touchStartZoom;
    private float touchSwipeDx;

    // ── JS ──
    private IJSObjectReference? _jsModule;
    // P1: maps filePath -> FileServer token for images served directly (no
    // Skia/base64). Tokens live for the page lifetime (never individually
    // revoked) so a cached URL never goes stale; all are revoked on dispose.
    private readonly ConcurrentDictionary<string, string> _servedTokens = new();

    // 显示尺寸（不全解码）缩略图的 page-scoped 缓存：path → (token, decodeMax)。
    // token 指向 FileServer 注册的内存字节；decodeMax 记录该图当前解码的最长边，
    // 供 MaybeUpgradeDecode 判断是否需升级到全清。Dispose 时随 _dynTokens 吊销。
    private readonly Dictionary<string, (string token, int decodeMax)> _dynCache = new();
    private readonly List<string> _dynTokens = new();
    // 当前显示图的解码最长边（== 原图最长边即表示已全清）。
    private int _currentDecodeMax;
    // 放大升级防重入。
    private bool _upgrading;

    // ── Navigation ──
    private bool hasPrev => currentIndex > 0;
    private bool hasNext => currentIndex >= 0 && currentIndex < fileList.Count - 1;
    private bool IsToolbarZoomFit => zoomFitMode && !stitchMode;
    private bool _isAnimating;

    // ═══════════ Lifecycle ═══════════

    protected override async Task OnInitializedAsync()
    {
        await JS.InvokeVoidAsync("eval",
            "document.documentElement.style.overflowY = 'hidden'");

        fileList = NavState.CurrentDirectoryFiles?
            .Where(f => Exts.Contains(Path.GetExtension(f))).ToList() ?? new();

        _dotNetRef = DotNetObjectReference.Create(this);
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

                if (!stitchMode && _jsModule != null)
                    await _jsModule.InvokeVoidAsync("focusViewport");

                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("initGestureTracker",
                        _dotNetRef, ".image-viewport", 80);
                    await _jsModule.InvokeVoidAsync("initResizeHandler",
                        _dotNetRef);

                    // Filmstrip positioning is deferred until after
                    // RefreshViewportMetricsAsync below, so vpWidth is known.
                }
            }
            catch { }

            StartHudTimer();

            // Measure the viewport first so the filmstrip gets a correct
            // ViewportWidth (vpWidth) for its minFill calculation.
            await RefreshViewportMetricsAsync();

            // Position the filmstrip at the current image — vpWidth is known,
            // so the virtual window is sized correctly from the first render.
            if (_jsModule != null)
            {
                if (!stitchMode && showFilmstrip && currentIndex >= 0 && filmstripRef != null)
                    await filmstripRef.ScrollToIndexAsync(currentIndex, false);
            }

            // Auto-select fit/1:1 for the current image using the now-known
            // using the now-known geometry. ApplyDecoded may have run during
            // OnInitializedAsync before the viewport was measured (vpWidth was 0)
            // and bailed out, so this is where the correct zoom is first
            // established. There is deliberately NO per-render recompute of an
            // already-computed zoom: AutoSelectZoom / RecomputeZoom are the single
            // source of truth, never a transient viewport read that could clobber
            // displayZoom for a frame and flash a wrong (e.g. 200%) zoom.
            await RefreshViewportMetricsAsync();
            AutoSelectZoom();
            StateHasChanged();

            // The WebView may not have laid out on the very first render, so
            // getViewportMetrics can still report 0 — in which case AutoSelectZoom
            // bailed and the zoom is not computed yet. Poll briefly until layout
            // settles, then compute the real zoom so the image can show at the
            // correct scale instead of flashing a default 1.0 (200% on HiDPI).
            // Bounded by iteration count; a hard fallback below guarantees the
            // image is never permanently hidden even if measurement truly fails.
            if (!stitchMode && !_zoomComputed)
            {
                for (int i = 0; i < 25 && !_zoomComputed && vpWidth <= 0; i++)
                {
                    await Task.Delay(16);
                    await RefreshViewportMetricsAsync();
                    AutoSelectZoom();
                }
                if (!_zoomComputed && imageWidth > 0)
                {
                    if (vpWidth <= 0 || vpHeight <= 0)
                    {
                        try
                        {
                            var w = await JS.InvokeAsync<double>("eval", "window.innerWidth || 360");
                            var h = await JS.InvokeAsync<double>("eval", "window.innerHeight || 640");
                            vpWidth = (float)w;
                            vpHeight = (float)h;
                        }
                        catch { vpWidth = 360; vpHeight = 640; }
                    }
                    AutoSelectZoom();
                    StateHasChanged();
                }
            }
        }
        else
        {
            // Race: the decode finished (imageWidth now known) but the viewport
            // wasn't measured yet when AutoSelectZoom first ran. Recompute once
            // real geometry is available. Loop-safe: it only fires when vpWidth>0
            // (real geometry) and stops the moment _zoomComputed becomes true, so
            // it can never spin. If vpWidth never becomes >0 the branch never
            // fires at all.
            if (!stitchMode && !_zoomComputed && !isLoading && imageWidth > 0 && imageHeight > 0 && vpWidth > 0 && vpHeight > 0)
            {
                AutoSelectZoom();
                StateHasChanged();
            }
        }
    }

    private async Task RefreshViewportMetricsAsync()
    {
        try
        {
            var dims = _jsModule != null
                ? await _jsModule.InvokeAsync<double[]>("getViewportMetrics")
                : await JS.InvokeAsync<double[]>("eval",
                    "new Promise(r => requestAnimationFrame(() => { var v = document.querySelector('.image-viewport'); if(!v){r([0,0,1,0,0]);return;} var rb=v.getBoundingClientRect(); r([v.offsetWidth, v.offsetHeight, window.devicePixelRatio || 1, rb.left, rb.top]); }))");
            vpWidth = (float)dims[0];
            vpHeight = (float)dims[1];
            _dpr = (float)dims[2];
            _vpOffX = (float)dims[3];
            _vpOffY = (float)dims[4];
        }
        catch { }
    }

    private void StartHudTimer()
    {
        _hudTimer?.Dispose();
        _hudTimer = new Timer(_ =>
        {
            InvokeAsync(() =>
            {
                if (showHUD)
                {
                    showHUD = false;
                    StateHasChanged();
                }
            });
        }, null, 3000, Timeout.Infinite);
    }

    private void ResetHudTimer()
    {
        showHUD = true;
        _hudTimer?.Change(3000, Timeout.Infinite);
    }

    // ═══════════ JSInvokable: Gesture Callbacks ═══════════

    [JSInvokable]
    public async Task OnGestureDrag(double offsetX)
    {
        // Called during drag — could show visual feedback
    }

    /// <summary>
    /// Called by the JS gesture tracker to get the adjacent image URI for
    /// the drag preview ("peek"). direction: -1 = prev, 1 = next.
    /// Returns null if there's no adjacent image in that direction.
    /// </summary>
    [JSInvokable]
    public async Task<string?> GetPeekUri(int direction)
    {
        var idx = currentIndex + direction;
        if (idx < 0 || idx >= fileList.Count) return null;
        return await GetImageDataUriAsync(fileList[idx]);
    }

    /// <summary>
    /// Returns the zoom factor for the adjacent image peek preview, so the
    /// peek renders at the same scale as the real image — avoiding a size
    /// jump when the slide transition completes.
    /// </summary>
    [JSInvokable]
    public double GetPeekZoom(int direction)
    {
        var idx = currentIndex + direction;
        if (idx < 0 || idx >= fileList.Count) return 1;
        var cached = DecodeCache.Get(fileList[idx]);
        if (!cached.HasValue || cached.Value.Width <= 0 || cached.Value.Height <= 0)
            return 1;
        var z = ComputeAutoZoom(cached.Value.Width, cached.Value.Height);
        return z.display;
    }

    /// <summary>
    /// Called (debounced) after the window is resized. Refreshes viewport
    /// metrics, recalculates fit zoom if in fit mode, and recenters the
    /// filmstrip so the current thumbnail stays in view.
    /// </summary>
    [JSInvokable]
    public async Task OnWindowResize()
    {
        await RefreshViewportMetricsAsync();
        RecomputeZoom();

        // Propagate the new viewport width to the filmstrip. Its OnParametersSet
        // then recomputes how many thumbnails fit and re-lays the window ONCE,
        // and the child recenters in its own OnAfterRenderAsync — a single,
        // coherent pass. We deliberately do NOT call ScrollToIndexAsync here:
        // doing so inline would race with the child's parameter-driven re-window
        // and (with the JS resize storm) spawn overlapping passes that churn
        // thumbnails during a shrink.
        StateHasChanged();
    }

    [JSInvokable]
    public async Task OnGestureRelease(double offsetX, double velocity)
    {
        if (_isAnimating) return;

        // ── Free mode (zoomed in): navigate by overscroll only ──
        if (!zoomFitMode)
        {
            float fingerDx = (float)offsetX;
            float panApplied = panX - panAtDragStartX;
            float overscroll = fingerDx - panApplied;

            // Dynamic threshold: at least 80px or 12% of viewport width.
            // A fixed 60px is too small on mobile — the user blasts past it
            // before noticing the guide line.
            float threshold = Math.Max(80f, vpWidth * 0.12f);

            if (Math.Abs(overscroll) >= threshold)
            {
                // Instantly clear overscroll inline styles before navigation
                if (_jsModule != null)
                {
                    await _jsModule.InvokeVoidAsync("clearSlideTransform");
                    await _jsModule.InvokeVoidAsync("hideOverscrollGuide");
                }
                // Use slide transition (DoFadeNavigate) instead of GoPrev/GoNext
                // (cylinder) for gesture-triggered navigation in free mode, so
                // the animation is consistent regardless of zoom mode. The image
                // may be small enough to fit entirely in the viewport at 1:1,
                // and the cylinder flip looks out of place there.
                if (overscroll > 0 && hasPrev) await DoFadeNavigate(-1, overscroll);
                else if (overscroll < 0 && hasNext) await DoFadeNavigate(1, overscroll);
            }
            else if (_jsModule != null)
            {
                // Smooth snap-back of the elastic overscroll visual
                await _jsModule.InvokeVoidAsync("setSlideOverscroll", 0);
                await _jsModule.InvokeVoidAsync("hideOverscrollGuide");
            }
            return;
        }

        // ── Fit mode: navigate by velocity / offset ──
        bool shouldNavigate;
        bool toPrev;

        if (Math.Abs(velocity) > 0.3)
        {
            shouldNavigate = true;
            toPrev = velocity > 0;
        }
        else if (Math.Abs(offsetX) > (vpWidth > 0 ? vpWidth * 0.25 : 80))
        {
            shouldNavigate = true;
            toPrev = offsetX > 0;
        }
        else
        {
            shouldNavigate = false;
            toPrev = false;
        }

        if (!shouldNavigate)
        {
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("springStart", ".img-slide",
                    offsetX, 0, velocity,
                    new { stiffness = 400, damping = 30, mass = 1 });
                await _jsModule.InvokeVoidAsync("cleanupGesturePeek");
            }
            else
            {
                await ClearSlideTransformAsync();
            }
            return;
        }

        if (toPrev && hasPrev) await DoFadeNavigate(-1, offsetX);
        else if (!toPrev && hasNext) await DoFadeNavigate(1, offsetX);
        else
        {
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("springStart", ".img-slide",
                    offsetX, 0, velocity,
                    new { stiffness = 400, damping = 30, mass = 1 });
                await _jsModule.InvokeVoidAsync("cleanupGesturePeek");
            }
            else
            {
                await ClearSlideTransformAsync();
            }
        }
    }

    private async Task DoFadeNavigate(int direction, double offsetX)
    {
        // direction: -1 = prev, 1 = next
        _isAnimating = true;

        var targetPath = fileList[currentIndex + direction];
        var targetUri = await GetImageDataUriAsync(targetPath);

        // Compute the target image's zoom WITHOUT calling ApplyZoomFor (which
        // triggers a Blazor re-render that changes .img-wrap scale mid-slide).
        // Instead, pre-compute just the numeric value for slideTransition,
        // then apply the zoom after animation completes.
        float targetZoom = displayZoom;
        var cached = DecodeCache.Get(targetPath);
        if (cached.HasValue && cached.Value.Width > 0 && cached.Value.Height > 0)
        {
            var z = ComputeAutoZoom(cached.Value.Width, cached.Value.Height);
            targetZoom = z.display;
        }

        // Hide the real wrap so the peek image shows through during the slide
        if (_jsModule != null) await _jsModule.InvokeVoidAsync("hideOverscrollGuide");

        // Slide: the peek image (already positioned at left:-100%/100% within
        // .img-slide) slides into view as the whole container translates.
        // The JS handler pins img.src + wrap.transform before resetting the
        // slide position, so there's no frame of the old image flashing back.
        if (_jsModule != null && targetUri != null)
            await _jsModule.InvokeVoidAsync("slideTransition", targetUri, direction, (double)vpWidth, targetZoom);

        // Clean up peek elements created during drag
        if (_jsModule != null) await _jsModule.InvokeVoidAsync("cleanupGesturePeek");

        // Now apply the target image's zoom (post-animation, no mid-slide
        // Blazor re-render to compete with the CSS transition).
        ApplyZoomFor(targetPath);
        if (_jsModule != null)
            await _jsModule.InvokeVoidAsync("waitFrame");

        // Switch state
        currentIndex += direction;
        filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        _isAnimating = false;
        ResetHudTimer();

        await ScrollFilmstripToCurrentAsync();
    }

    // ═══════════ Immersive Mode ═══════════

    /// <summary>
    /// Re-flow the image after the chrome (toolbar / filmstrip) show/hide changes
    /// the visible viewport region. The .image-viewport box is shrunk via CSS to
    /// sit between the chrome, so we only need to re-measure it and recompute the
    /// fit zoom. In fit mode the zoom re-fits to the visible region; in 1:1 mode
    /// the user's explicit zoom is preserved and only ClampPan re-bounds it.
    /// </summary>
    private async Task RecomputeZoomForChromeAsync()
    {
        if (stitchMode) { StateHasChanged(); return; }
        StateHasChanged();            // commit the new chrome classes -> viewport box shrinks
        await Task.Delay(30);         // let MAUI WebView commit layout before measuring
        await RefreshViewportMetricsAsync();
        RecomputeZoom();
        StateHasChanged();            // apply the corrected fit zoom
    }

    private async Task OnPageClick()
    {
        if (showDetails) return;
        ResetHudTimer();

        if (!showToolbar)
        {
            // Exit immersive
            showToolbar = true;
            showFilmstrip = true;
            await RecomputeZoomForChromeAsync();
        }
    }

    private async Task ToggleImmersive()
    {
        if (showToolbar)
        {
            showToolbar = false;
            showFilmstrip = false;
            showHUD = false;
        }
        else
        {
            showToolbar = true;
            showFilmstrip = true;
            showHUD = true;
            ResetHudTimer();
        }
        await RecomputeZoomForChromeAsync();
    }

    // ═══════════ Filmstrip ═══════════

    private static readonly Dictionary<string, string> s_thumbCache = new();
    // Capped (≤768px) decodes used only as the fast-passing pictures in the
    // filmstrip multi-slide animation — never the real decode path.
    private static readonly Dictionary<string, string> s_navSlideCache = new();
    private bool _filmstripBuilt;
    private ImageFilmstrip? filmstripRef;

    private async Task BuildFilmstripAsync()
    {
        if (_filmstripBuilt) return;

        _filmstripThumbnails = new List<string>(fileList.Count);
        for (int i = 0; i < fileList.Count; i++)
            _filmstripThumbnails.Add("");

        // Show cached items immediately (DecodeCache or thumb cache)
        for (int i = 0; i < fileList.Count; i++)
        {
            var path = fileList[i];
            if (s_thumbCache.TryGetValue(path, out var cachedThumb))
            {
                _filmstripThumbnails[i] = cachedThumb;
            }
            else
            {
                // Only reuse a cached entry as a filmstrip thumb if it is a
                // thumbnail-sized data:URI. P1 may cache a full-res FileServer
                // URL here; using it for a 120px strip would fetch the whole
                // image, so skip and let GenerateThumbnail populate it instead.
                var cached = DecodeCache.Get(path);
                if (cached.HasValue && cached.Value.DataUri.StartsWith("data:"))
                    _filmstripThumbnails[i] = cached.Value.DataUri;
            }
        }
        _filmstripBuilt = true;
        StateHasChanged();

        // Generate thumbnails for remaining images in background (once),
        // center-out so the current image and its neighbors appear first
        // instead of slowly walking 0 -> currentIndex.
        _ = Task.Run(async () =>
        {
            int n = fileList.Count;
            int center = currentIndex >= 0 ? currentIndex : 0;
            var order = new List<int>(n) { center };
            for (int d = 1; d < n; d++)
            {
                if (center - d >= 0) order.Add(center - d);
                if (center + d < n) order.Add(center + d);
            }

            foreach (var i in order)
            {
                if (!string.IsNullOrEmpty(_filmstripThumbnails[i]))
                    continue;

                var path = fileList[i];
                try
                {
                    if (!File.Exists(path)) continue;

                    var thumb = ImageProcessingService.GenerateThumbnail(path, 120);
                    lock (s_thumbCache)
                        s_thumbCache[path] = thumb;

                    _filmstripThumbnails[i] = thumb;
                    await InvokeAsync(StateHasChanged);
                    await Task.Delay(10);
                }
                catch { /* placeholder stays */ }
            }
        });
    }

    private async Task ScrollFilmstripToCurrentAsync(bool smooth = true)
    {
        if (!_filmstripBuilt) return;
        if (filmstripRef != null)
            await filmstripRef.ScrollToIndexAsync(currentIndex, smooth);
    }

    private async Task OnFilmstripClick(int index)
    {
        if (_isAnimating || index == currentIndex || index < 0 || index >= fileList.Count) return;

        stitchMode = false;
        _isAnimating = true;

        int dir = index > currentIndex ? 1 : -1;
        int dist = Math.Abs(index - currentIndex);

        // How many adjacent pictures to actually lay out in the slide
        // direction. Bounded so a far click (e.g. 30 thumbnails away) doesn't
        // decode 30 images — we sample `drawn` evenly-spaced pictures and whip
        // through them; the visual reads as "sliding through many images"
        // regardless of the true distance.
        int drawn = Math.Min(dist, 8);

        // Build the stream of passing images. The LAST entry is the target
        // itself (full-res + its own zoom). Each intermediate is sampled at an
        // evenly-spaced index and rendered at its OWN fit/1:1 zoom, so the
        // slide shows real pictures — not stretched 120px thumbnails.
        var streamUris = new List<string>(drawn);
        var streamZooms = new List<double>(drawn);
        for (int k = 0; k < drawn; k++)
        {
            double zoom;
            string uri;
            if (k == drawn - 1)
            {
                // Target: full-res URI + its own fit/1:1 zoom.
                uri = await GetImageDataUriAsync(fileList[index]) ?? "";
                var tc = DecodeCache.Get(fileList[index]);
                zoom = (tc.HasValue && tc.Value.Width > 0 && tc.Value.Height > 0)
                    ? ComputeAutoZoom(tc.Value.Width, tc.Value.Height).display
                    : displayZoom;
            }
            else
            {
                int idx = currentIndex + dir * (int)Math.Round((double)dist * (k + 1) / drawn);
                if (idx == currentIndex) idx += dir;
                idx = Math.Max(0, Math.Min(fileList.Count - 1, idx));

                uri = GetNavSlideUri(fileList[idx]);   // capped decode (≤ NavThumbCap px) for the fast pass
                var dims = ImageProcessingService.GetDirectServeInfo(fileList[idx]);
                if (dims.width > 0 && dims.height > 0)
                {
                    var fullZoom = ComputeAutoZoom(dims.width, dims.height).display;
                    // GetNavSlideUri returns a NavThumbCap-capped thumbnail, NOT the
                    // full image. Scaling it by the *full-image* zoom would shrink
                    // large pictures to a fraction of their true on-screen size
                    // (e.g. a 4000px image decoded to 768px then ×0.25 fit-zoom =
                    // 192px while the real image fills the viewport at ~1000px).
                    // Correct the zoom by the decode ratio so the thumbnail renders
                    // at the SAME size the real image would — the true "real zoom".
                    int fullMax = Math.Max(dims.width, dims.height);
                    int navMax = Math.Min(NavThumbCap, fullMax);
                    zoom = navMax > 0 ? fullZoom * ((double)fullMax / navMax) : fullZoom;
                }
                else
                {
                    zoom = displayZoom;   // non-native format: fall back to current zoom
                }
            }
            streamUris.Add(uri);
            streamZooms.Add(zoom);
        }

        // Compute the target image's fit/1:1 zoom WITHOUT calling ApplyZoomFor
        // (which would change the currently-displayed image's displayZoom and
        // make it snap to the target zoom on the very first animation frame).
        // We keep the current zoom for the whole slide, then apply the target
        // zoom AFTER the animation completes — matching DoFadeNavigate.
        float targetScale = displayZoom;
        var tCached = DecodeCache.Get(fileList[index]);
        if (tCached.HasValue && tCached.Value.Width > 0 && tCached.Value.Height > 0)
        {
            var z = ComputeAutoZoom(tCached.Value.Width, tCached.Value.Height);
            targetScale = z.display;
        }

        // Slide: lay out current + the stream of adjacent images, then whip the
        // whole track past so they slide through to the target. The last stream
        // entry IS the target (full-res), so the resting frame is already sharp.
        if (_jsModule != null && _navAnimationEnabled && streamUris.Count > 0)
            await _jsModule.InvokeVoidAsync("multiSlideTransition",
                streamUris.ToArray(),
                streamZooms.ToArray(),
                streamUris[streamUris.Count - 1],
                dir > 0 ? "next" : "prev",
                targetScale, displayZoom);

        // Apply the target zoom AFTER the animation so the on-screen image
        // keeps its own scale during the flip (no mid-animation zoom jump).
        ApplyZoomFor(fileList[index]);

        // Switch state
        currentIndex = index;
        filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        _isAnimating = false;

        // Snap the filmstrip to the new index without animating (instant) so
        // a far click never walks 0 -> index and spams thumbnail loads. The
        // child handles window re-centering + scroll suppression internally.
        if (filmstripRef != null)
            await filmstripRef.ScrollToIndexAsync(index, false);

        StateHasChanged();
    }

    // ═══════════ Image Loading ═══════════

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
            errorMessage = "No file path specified";
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
        _ = BuildFilmstripAsync();
    }

    private void ResetView()
    {
        // Only reset pan. The zoom (displayZoom / fitZoom / zoomFitMode) is the
        // single source of truth owned by AutoSelectZoom / ApplyZoomFor /
        // RecomputeZoom — never reset it here. On a HiDPI screen (_dpr = 2) the
        // old `displayZoom = 1.0f` used to read as 200% for one frame every
        // navigation. The correct zoom is restored synchronously by ApplyDecoded
        // -> AutoSelectZoom (cached path) or by ApplyZoomFor (called in
        // GoPrev/GoNext before the transition), so preserving it here avoids any
        // transient flash.
        panX = 0;
        panY = 0;
        _dragDesiredPanX = 0;
    }

    private async Task CheckCanStitchAsync()
    {
        // IMPORTANT: do NOT reset canStitch = false at the top. canStitch only
        // depends on the file list (>= 2 decoded images), never on which image
        // is currently shown. The old top-level `canStitch = false` made the 📐
        // (stitch) toolbar button blink out and back in on EVERY navigation,
        // because GoPrev/GoNext -> LoadImageAsync -> finally used to call this.
        // We now compute the value and only push a render when it actually
        // changes, so the button never flickers. (LoadImageAsync's finally no
        // longer calls this at all; it is invoked once after the filmstrip's
        // decode pass, where a change is expected and legitimate.)
        if (fileList.Count < 2) { SetCanStitch(false); return; }
        await Task.Yield();

        var widths = new List<int>();
        foreach (var p in fileList)
        {
            var c = DecodeCache.Get(p);
            if (c.HasValue) widths.Add(c.Value.Width);
        }
        SetCanStitch(widths.Count >= 2);
    }

    private void SetCanStitch(bool value)
    {
        if (canStitch == value) return;
        canStitch = value;
        _ = InvokeAsync(StateHasChanged);
    }

    private void ToggleDetails()
    {
        showDetails = !showDetails;
        // The thumbnail filmstrip is intentionally left visible while the detail
        // panel is open — it is controlled only by the toolbar/immersive state.
        _ = RecomputeZoomForChromeAsync();
    }
    private void CloseDetails() { showDetails = false; _ = RecomputeZoomForChromeAsync(); }

    // P1: get (or create) a loopback FileServer URL for an image file. The token
    // is cached per path for the page lifetime so the same image never registers
    // duplicate tokens and a cached URL never becomes invalid.
    private string ServedUrl(string path)
    {
        if (_servedTokens.TryGetValue(path, out var tok))
            return $"{FileServer.BaseUrl}/file?token={tok}";
        // 图片查看器自行决定 MIME（此处沿用标准表；如需覆盖可在注册时传入自定义值）。
        var t = FileServer.RegisterFile(path, MimeTypes.Get(path));
        _servedTokens[path] = t;
        return $"{FileServer.BaseUrl}/file?token={t}";
    }

    // Revoke every FileServer token we registered (called on navigation-away /
    // dispose so the loopback server stops serving our files).
    private void RevokeServedTokens()
    {
        foreach (var tok in _dynTokens)
        {
            try { FileServer.UnregisterBytes(tok); } catch { }
        }
        _dynTokens.Clear();
        foreach (var tok in _servedTokens.Values)
        {
            try { FileServer.UnregisterFile(tok); } catch { }
        }
        _servedTokens.Clear();
    }

    // P4: pick the cached grid thumbnail for `path` as the blurred placeholder
    // shown while the full image loads. No-op if no thumbnail is available yet.
    // IMPORTANT: skip the placeholder entirely when the image is already cached
    // (DecodeCache hit). A cached image is available instantly, so a blurred
    // preview would only flash for a frame — most visibly right after a
    // navigation animation that already shows the incoming image crisply —
    // reading as an edge-blur glitch. Cold (uncached) loads still get it.
    private void SetPlaceholderFor(string path)
    {
        if (DecodeCache.Get(path).HasValue) { placeholderSource = null; return; }
        if (s_thumbCache.TryGetValue(path, out var t) && !string.IsNullOrEmpty(t))
            placeholderSource = t;
        else
            placeholderSource = null;
    }

    private async Task LoadImageAsync()
    {
        isLoading = true;
        errorMessage = null;
        imageSource = null;
        _instantLoad = false;
        SetPlaceholderFor(filePath);

        imageWidth = 0;
        imageHeight = 0;
        fileSizeDisplay = "";
        imageFormat = "";
        _fileCreationTime = null;
        _fileLastWriteTime = null;

        try
        {
            var cached = DecodeCache.Get(filePath);
            if (cached.HasValue)
            {
                // A data:URI entry is truly instant (inline) — tell the viewport
                // to skip the fade-in so navigation doesn't flash. Direct-serve
                // entries still fetch from the loopback server, so keep the
                // (now blur-free, thanks to SetPlaceholderFor) fade.
                _instantLoad = !cached.Value.IsDirectServe;

                // P1 fix: a direct-serve entry stores only dimensions + a flag,
                // never the per-page FileServer token URL (that token is revoked
                // on dispose). A cross-page cache hit must re-mint a fresh token
                // via ServedUrl() instead of reusing the now-403 stale URL.
                string src = cached.Value.IsDirectServe
                    ? ServedUrl(filePath)
                    : cached.Value.DataUri;
                ApplyDecoded(src, cached.Value.Width, cached.Value.Height);
                _currentDecodeMax = Math.Max(cached.Value.Width, cached.Value.Height); // 全清命中
                try
                {
                    var fi = new FileInfo(filePath);
                    fileSizeDisplay = ImageProcessingService.FormatFileSize(fi.Length);
                    imageFormat = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
                    _fileCreationTime = fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                    _fileLastWriteTime = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch { }
                isLoading = false;
                StateHasChanged();
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) { errorMessage = "File not found"; return; }

            fileSizeDisplay = ImageProcessingService.FormatFileSize(fileInfo.Length);
            imageFormat = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            _fileCreationTime = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
            _fileLastWriteTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

            // 取原图尺寸（轻量 header parse，不解码像素）
            var dims = await Task.Run(() => ImageProcessingService.GetDirectServeInfo(filePath));
            int origW = dims.width, origH = dims.height;

            // 按显示尺寸决定解码分辨率：只解“屏幕上真实铺开的那几个像素 × DPR”。
            // fit 缩小的大图 → 生成显示尺寸缩略图（不全解码）；1:1/小图 → 走全清。
            if (origW > 0 && origH > 0 && vpWidth > 0 && vpHeight > 0)
            {
                float dispZoom = ComputeAutoZoom(origW, origH).display;   // fit 时 < 1
                int origMax = Math.Max(origW, origH);
                int decodeBudget = (int)Math.Min(origMax,
                    Math.Max(64, Math.Ceiling(origMax * dispZoom * _dpr)));

                if (decodeBudget < origMax - 1)
                {
                    string? dynSrc = null;
                    if (_dynCache.TryGetValue(filePath, out var dyn))
                        dynSrc = $"{FileServer.BaseUrl}/file?token={dyn.token}";
                    else
                    {
                        var jpeg = await Task.Run(() =>
                            ImageProcessingService.GenerateThumbnailBytes(filePath, decodeBudget));
                        if (jpeg != null && jpeg.Length > 0)
                        {
                            var tok = FileServer.RegisterBytes(jpeg, "image/jpeg");
                            _dynCache[filePath] = (tok, decodeBudget);
                            _dynTokens.Add(tok);
                            dynSrc = $"{FileServer.BaseUrl}/file?token={tok}";
                        }
                    }
                    if (dynSrc != null)
                    {
                        // 记录全清后备（direct-serve 可直出时），供放大升级使用
                        if (dims.canServe) DecodeCache.SetDirectServe(filePath, origW, origH);
                        ApplyDecoded(dynSrc, origW, origH);
                        _currentDecodeMax = decodeBudget;
                        _instantLoad = false;   // served URL 仍需加载；SetPlaceholderFor 已铺模糊占位避免闪
                        isLoading = false;
                        StateHasChanged();
                        _ = PreloadAdjacentAsync();
                        return;
                    }
                    // 生成失败 → 落空走下方全清路径
                }
            }

            // 全清路径（1:1/小图，或显示尺寸生成失败，或首开视口未测退化为全清）
            await Task.Run(() =>
            {
                try
                {
                    var serve = ImageProcessingService.GetDirectServeInfo(filePath);
                    if (serve.canServe)
                    {
                        var url = ServedUrl(filePath);
                        DecodeCache.SetDirectServe(filePath, serve.width, serve.height);
                        ApplyDecoded(url, serve.width, serve.height);
                        return;
                    }
                    var bytes = File.ReadAllBytes(filePath);
                    var result = ImageProcessingService.DecodeImage(bytes, Path.GetFileName(filePath), cacheDir: FileSystem.GetScratchDirectory("DdsDecode"));
                    DecodeCache.Set(filePath, result.DataUri, result.Width, result.Height);
                    ApplyDecoded(result.DataUri, result.Width, result.Height);
                }
                catch
                {
                    try
                    {
                        var url = ServedUrl(filePath);
                        var gd = ImageProcessingService.GetImageDimensions(File.ReadAllBytes(filePath));
                        ApplyDecoded(url, gd.width, gd.height);
                    }
                    catch
                    {
                        errorMessage = $"Cannot decode: {Path.GetFileName(filePath)}";
                    }
                }
            });
            _currentDecodeMax = Math.Max(imageWidth, imageHeight);
            _ = PreloadAdjacentAsync();
        }
        catch (Exception ex)
        {
            errorMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    // ═══ 放大时升级到全清（动态解码核心）═══
    /// <summary>
    /// 当 displayZoom 放大到超过当前“显示尺寸解码”的清晰度时，异步升级到全清图。
    /// 替换 src 时旧图（显示尺寸）会持续显示直到全清解码完成（&lt;img&gt; 默认行为），
    /// 配合 _instantLoad=true 跳过淡入 → 无闪、无空白。缩回 fit 不降级（保留更高质量）。
    /// </summary>
    private async Task MaybeUpgradeDecode()
    {
        if (_upgrading || stitchMode || vpWidth <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return;
        int origMax = Math.Max(imageWidth, imageHeight);
        if (_currentDecodeMax >= origMax - 1) return;            // 已是全清
        float needEdge = origMax * displayZoom * _dpr;           // 实际需要显示的像素
        if (needEdge <= _currentDecodeMax * 1.15f) return;        // 当前已足够清晰

        _upgrading = true;
        try
        {
            string? fullUrl = null;
            var cached = DecodeCache.Get(filePath);
            if (cached.HasValue && !cached.Value.IsDirectServe)
                fullUrl = cached.Value.DataUri;                  // 已有全清 data URI
            else
            {
                var serve = ImageProcessingService.GetDirectServeInfo(filePath);
                if (serve.canServe)
                    fullUrl = ServedUrl(filePath);               // direct-serve 全清
                else
                {
                    var bytes = await File.ReadAllBytesAsync(filePath);
                    var result = await Task.Run(() =>
                        ImageProcessingService.DecodeImage(bytes, Path.GetFileName(filePath), cacheDir: FileSystem.GetScratchDirectory("DdsDecode")));
                    DecodeCache.Set(filePath, result.DataUri, result.Width, result.Height);
                    fullUrl = result.DataUri;
                }
            }
            if (!string.IsNullOrEmpty(fullUrl))
            {
                _currentDecodeMax = origMax;
                _instantLoad = true;                             // 旧图持续显示直到全清就绪，无闪
                imageSource = fullUrl;
                StateHasChanged();
            }
        }
        catch { }
        finally { _upgrading = false; }
    }

    /// <summary>
    /// Pre-compute and apply the fit/1:1 zoom for a target image *before* it is
    /// shown. Used by GoPrev/GoNext so the transition's new-image layer (and the
    /// src swap it performs) inherits the correct scale instead of the
    /// currently-displayed image's zoom — eliminating the "fit vs 1:1 undecided"
    /// pop on navigation. Does not modify the transition itself.
    /// </summary>
    private void ApplyZoomFor(string path)
    {
        if (stitchMode || vpWidth <= 0 || vpHeight <= 0) return;
        var c = DecodeCache.Get(path);
        if (!c.HasValue || c.Value.Width <= 0 || c.Value.Height <= 0) return;

        var z = ComputeAutoZoom(c.Value.Width, c.Value.Height);
        fitZoom = z.fit;
        zoomFitMode = z.fitMode;
        displayZoom = z.display;
        _zoomComputed = true;
    }

    private void ApplyDecoded(string dataUri, int w, int h)
    {
        imageSource = dataUri;
        imageWidth = w;
        imageHeight = h;
        // Pick the correct fit/1:1 zoom synchronously the moment a decode
        // finishes, so the first rendered frame after a decode already shows the
        // right scale. displayZoom is the single source of truth that BOTH the
        // transform (ImageViewport, always scale(displayZoom)) and the toolbar
        // (displayZoom * _dpr) read, so the toolbar value always equals the
        // actual rendered zoom — there is no second mechanism that could drift.
        AutoSelectZoom();
    }

    // ── Zoom: single source of truth ──
    // `displayZoom` is the ACTUAL CSS scale factor applied to .img-wrap via
    // `transform: scale(displayZoom)` (see ImageViewport.GetZoomStyle, which no
    // longer has a fit/empty branch). The toolbar shows `displayZoom * _dpr *
    // 100%`, which is exactly the physical (device-pixel) zoom of the rendered
    // image — so the toolbar value and the actual zoom can never diverge as long
    // as .img-wrap always carries scale(displayZoom). 100% in the toolbar means
    // 1 image device-px == 1 screen device-px (true "actual pixels").

    /// <summary>
    /// Pure fit/1:1 decision for an image of the given dimensions, using the
    /// currently-cached viewport metrics (no JS round-trip). The single place
    /// that decides mode + zoom, shared by every caller so the logic can never
    /// drift between navigation, decode, and resize.
    /// </summary>
    private (float fit, float display, bool fitMode) ComputeAutoZoom(int imgW, int imgH)
    {
        if (vpWidth <= 0 || vpHeight <= 0 || imgW <= 0 || imgH <= 0)
            return (1f, 1f, true);
        float cssFit = Math.Min(Math.Min(vpWidth / imgW, vpHeight / imgH), 1.0f);
        float oneToOne = GetOneToOneZoom();   // displayZoom that reads as 100%
        if (cssFit <= oneToOne)
            return (cssFit, cssFit, true);     // larger than 1:1 -> fit to window
        return (cssFit, oneToOne, false);      // smaller than 1:1 -> show at 1:1
    }

    /// <summary>Auto-select fit vs 1:1 for the image that just decoded.</summary>
    private void AutoSelectZoom()
    {
        // Bail (do NOT set displayZoom) unless BOTH the image dimensions AND the
        // viewport geometry are known. A default fallback here is exactly what
        // causes the first-frame flash: the image would render at scale(1.0) for
        // one frame before the real zoom is computed. Leaving _zoomComputed false
        // keeps the image hidden until the real zoom is ready.
        if (stitchMode || vpWidth <= 0 || vpHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            _zoomComputed = false;
            return;
        }
        var z = ComputeAutoZoom(imageWidth, imageHeight);
        fitZoom = z.fit;
        zoomFitMode = z.fitMode;
        displayZoom = z.display;
        panX = 0;
        panY = 0;
        _zoomComputed = true;
    }

    /// <summary>
    /// Recompute fitZoom from current geometry and rescale the *current* mode
    /// (no mode change). Called when the viewport is (re)measured or on resize.
    /// </summary>
    private void RecomputeZoom()
    {
        if (stitchMode || vpWidth <= 0 || vpHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return;
        float cssFit = Math.Min(Math.Min(vpWidth / imageWidth, vpHeight / imageHeight), 1.0f);
        fitZoom = cssFit;
        if (zoomFitMode) displayZoom = cssFit;
        // Only clamp in fit mode. In 1:1 mode the user's chosen zoom may be
        // smaller than the new fitZoom (e.g. a small image in a large window),
        // and clamping would push it up — breaking 1:1.
        if (zoomFitMode) displayZoom = Math.Clamp(displayZoom, MinZoom, MaxZoom);
        ClampPan();
        _zoomComputed = true;
    }

    private async Task PreloadAdjacentAsync()
    {
        var paths = new List<string>(2);
        if (currentIndex > 0) paths.Add(fileList[currentIndex - 1]);
        if (currentIndex < fileList.Count - 1) paths.Add(fileList[currentIndex + 1]);
        foreach (var p in paths)
        {
            if (DecodeCache.Get(p).HasValue) continue;
            try
            {
                // P1: preload adjacent images via direct FileServer URL when
                // possible — keeps them instantly switchable without holding
                // multi-MB base64 strings in the cache.
                var serve = ImageProcessingService.GetDirectServeInfo(p);
                if (serve.canServe)
                {
                    // Mint a token for this page so switching to the adjacent
                    // image is instant, but only cache dimensions — the URL with
                    // its token must not persist cross-page (see SetDirectServe).
                    ServedUrl(p);
                    DecodeCache.SetDirectServe(p, serve.width, serve.height);
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(p);
                var result = await Task.Run(() =>
                    ImageProcessingService.DecodeImage(bytes, Path.GetFileName(p), cacheDir: FileSystem.GetScratchDirectory("DdsDecode")));
                DecodeCache.Set(p, result.DataUri, result.Width, result.Height);
            }
            catch { }
        }
        await CheckCanStitchAsync();
    }

    // ═══════════ Zoom ═══════════

    private void ClampPan()
    {
        if (imageWidth <= 0 || imageHeight <= 0) return;

        float vw = vpWidth > 0 ? vpWidth : 800f;
        float vh = vpHeight > 0 ? vpHeight : 600f;
        float dispW = imageWidth * displayZoom;
        float dispH = imageHeight * displayZoom;
        float maxX = Math.Max(0, (dispW - vw) / 2);
        float maxY = Math.Max(0, (dispH - vh) / 2);
        panX = Math.Clamp(panX, -maxX, maxX);
        panY = Math.Clamp(panY, -maxY, maxY);
    }

    private string GetDisplayZoom()
    {
        var zoom = stitchMode ? _stitchZoom : displayZoom;
        return $"{zoom * _dpr * 100:F0}%";
    }

    private string GetFitToggleText()
    {
        if (stitchMode) return _stitchZoom >= 1.0f ? "Fit" : "1:1";
        return zoomFitMode ? "1:1" : "Fit";
    }

    private string GetFitToggleTitle()
    {
        if (stitchMode) return _stitchZoom >= 1.0f ? "Fit to window" : "1:1";
        return zoomFitMode ? "1:1" : "Fit to window";
    }

    private void ExitFit()
    {
        zoomFitMode = false;
        panX = 0;
        panY = 0;
    }

    private async Task ZoomIn()
    {
        ResetHudTimer();
        if (stitchMode) { _stitchZoom = Math.Min(_stitchZoom * 1.5f, 5.0f); RecomputeStitchLayout(); StateHasChanged(); await AnchorStitchScroll(); return; }
        ExitFit(); displayZoom = Math.Min(displayZoom * ZoomStep, MaxZoom); ClampPan();
        if (_jsModule != null) await _jsModule.InvokeVoidAsync("showZoomPopup", GetDisplayZoom());
        StateHasChanged();
        await MaybeUpgradeDecode();
    }

    private async Task ZoomOut()
    {
        ResetHudTimer();
        if (stitchMode) { _stitchZoom = Math.Max(_stitchZoom / 1.5f, 0.1f); RecomputeStitchLayout(); StateHasChanged(); await AnchorStitchScroll(); return; }
        ExitFit(); displayZoom = Math.Max(displayZoom / ZoomStep, MinZoom); ClampPan();
        if (_jsModule != null) await _jsModule.InvokeVoidAsync("showZoomPopup", GetDisplayZoom());
        StateHasChanged();
    }

    private async Task ZoomFit()
    {
        if (stitchMode) { _stitchZoom = 1.0f; RecomputeStitchLayout(); StateHasChanged(); await AnchorStitchScroll(); return; }
        zoomFitMode = true; displayZoom = fitZoom; panX = 0; panY = 0;
    }

    private async Task ZoomActual()
    {
        if (stitchMode)
        {
            var cw = _stitchContentWidth > 0 ? _stitchContentWidth : (vpWidth > 0 ? vpWidth : 1);
            var iw = (currentIndex >= 0 && stitchImages != null && currentIndex < stitchImages.Count) ? stitchImages[currentIndex].Width : 0;
            _stitchZoom = iw > 0 ? (float)(iw / cw) : 1.0f;
            RecomputeStitchLayout(); StateHasChanged(); await AnchorStitchScroll(); return;
        }
        ExitFit();
        displayZoom = Math.Max(1.0f / _dpr, 0.01f);
        await MaybeUpgradeDecode();
    }

    private async Task ToggleFitActual()
    {
        ResetHudTimer();
        if (zoomFitMode) await ZoomActual(); else await ZoomFit();
    }

    private float GetOneToOneZoom() => Math.Max(1.0f / _dpr, 0.01f);

    // ═══════════ Wheel ═══════════

    private async void OnWheel(WheelEventArgs e)
    {
        ResetHudTimer();
        float cx = (float)e.ClientX - _vpOffX;
        float cy = (float)e.ClientY - _vpOffY;
        if (zoomFitMode) { zoomFitMode = false; panX = 0; panY = 0; }
        float oldZoom = displayZoom;
        float oldPanX = panX;
        float oldPanY = panY;
        displayZoom = e.DeltaY < 0 ? Math.Min(displayZoom * ZoomStep, MaxZoom) : Math.Max(displayZoom / ZoomStep, MinZoom);
        if (vpWidth > 0 && vpHeight > 0 && oldZoom > 0.001f)
        {
            float localX = (cx - vpWidth / 2f - oldPanX) / oldZoom;
            float localY = (cy - vpHeight / 2f - oldPanY) / oldZoom;
            panX = cx - vpWidth / 2f - localX * displayZoom;
            panY = cy - vpHeight / 2f - localY * displayZoom;
        }
        ClampPan();
        StateHasChanged();
        await MaybeUpgradeDecode();
    }

    private async Task OnDoubleClick(MouseEventArgs e)
    {
        ResetHudTimer();
        if (zoomFitMode) await ZoomActual(); else await ZoomFit();
    }

    // ═══════════ Drag ═══════════

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
        float desiredPanX = panAtDragStartX + (float)(e.ClientX - dragStartX);
        float desiredPanY = panAtDragStartY + (float)(e.ClientY - dragStartY);
        _dragDesiredPanX = desiredPanX;
        panX = desiredPanX;
        panY = desiredPanY;
        ClampPan();

        // Elastic overscroll / slide feedback in free mode:
        //   maxX > 0 (image wider than viewport, can pan):
        //     → damped overscroll transform + guide line (existing)
        //   maxX == 0 (image fits viewport, no pan possible):
        //     → full slide transform like fit mode (no damping, no line)
        // Uses DecodeCache dimensions instead of imageWidth to avoid races
        // when the field hasn't been updated yet (e.g. mid-navigation).
        if (!zoomFitMode && _jsModule != null)
        {
            float overscroll = desiredPanX - panX;
            if (Math.Abs(overscroll) > 0.5f)
            {
                var cached = DecodeCache.Get(filePath);
                float cachedW = cached.HasValue ? cached.Value.Width : 0;
                float vw = vpWidth > 0 ? vpWidth : 800f;
                float dispW = (cachedW > 0 ? cachedW : imageWidth) * displayZoom;
                float maxX = Math.Max(0, (dispW - vw) / 2);
                if (maxX > 0)
                {
                    float damped = overscroll * 0.3f;
                    float thresh = Math.Max(80f, vpWidth * 0.12f);
                    _ = _jsModule.InvokeVoidAsync("setSlideOverscroll", damped);
                    _ = _jsModule.InvokeVoidAsync("showOverscrollGuide", overscroll, thresh);
                }
                else
                {
                    // Full slide (like fit mode) — no damping, no guide line.
                    // C# is the sole controller here (JS pointermove doesn't
                    // set .img-slide in free mode), so no transform conflict.
                    _ = _jsModule.InvokeVoidAsync("setSlideTransform",
                        $"translateX({overscroll}px)");
                }
            }
        }

        StateHasChanged();
    }

    private void OnPointerUp(PointerEventArgs e)
    {
        isDragging = false;
        // Overscroll visual cleanup is handled in OnGestureRelease,
        // where we know whether to animate back or navigate.
        if (!zoomFitMode && _jsModule != null)
            _ = _jsModule.InvokeVoidAsync("hideOverscrollGuide");
    }

    // ═══════════ Touch ═══════════

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

    private async void OnTouchMove(TouchEventArgs e)
    {
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
                float mx = (float)((e.Touches[0].ClientX + e.Touches[1].ClientX) / 2) - _vpOffX;
                float my = (float)((e.Touches[0].ClientY + e.Touches[1].ClientY) / 2) - _vpOffY;
                if (vpWidth > 0 && vpHeight > 0 && oldZoom > 0.001f)
                {
                    float localX = (mx - vpWidth / 2f - panX) / oldZoom;
                    float localY = (my - vpHeight / 2f - panY) / oldZoom;
                    panX = mx - vpWidth / 2f - localX * displayZoom;
                    panY = my - vpHeight / 2f - localY * displayZoom;
                }
                ClampPan();
                StateHasChanged();
                await MaybeUpgradeDecode();
            }
            return;
        }

        if (e.Touches.Length == 1 && isTouchPan)
        {
            float cx = (float)e.Touches[0].ClientX;
            if (zoomFitMode)
            {
                touchSwipeDx = cx - touchStartX;
            }
            else
            {
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
        if (zoomFitMode && Math.Abs(touchSwipeDx) > 60)
        {
            _ = touchSwipeDx > 0 ? GoPrev() : GoNext();
        }
    }

    // ═══════════ Keyboard ═══════════

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        ResetHudTimer();
        switch (e.Key)
        {
            case "ArrowLeft": _ = GoPrev(); break;
            case "ArrowRight":
            case " ": _ = GoNext(); break;
            case "ArrowUp":
            case "=": await ZoomIn(); break;
            case "ArrowDown":
            case "-": await ZoomOut(); break;
            case "Escape": GoBack(); break;
        }
    }

    // ═══════════ Navigation ═══════════

    private void GoBack()
    {
        stitchMode = false; stitchImages = null;
        RevokeServedTokens();
        _ = MauiNav.GoBackAsync();
    }

    private async Task GoPrev()
    {
        if (!hasPrev || _isAnimating) return;
        _isAnimating = true;
        stitchMode = false; stitchImages = null;

        // Get target image URI for animation
        var targetPath = fileList[currentIndex - 1];
        var targetUri = await GetImageDataUriAsync(targetPath);

        // Apply the target image's fit/1:1 zoom *now*, before the transition, so
        // the new-image layer (and the src swap it performs) shows the incoming
        // image at its own correct scale from frame one — not the outgoing
        // image's zoom. The transition function itself is unchanged.
        // Save the old zoom so the front card starts the flip at the image's
        // own zoom and smoothly transitions to the target zoom via CSS
        // interpolation (no visible jump).
        var oldZoom = displayZoom;
        ApplyZoomFor(targetPath);

        // 3D cylinder transition. panX/panY passed so the front card's
        // transform uses the outgoing image's pan. outgoingScale (oldZoom)
        // lets the front start at the current zoom and interpolate to the
        // target zoom during the 480ms flip — no "缩放变化".
        if (_jsModule != null && targetUri != null && _navAnimationEnabled)
            await _jsModule.InvokeVoidAsync("cylinderTransition", targetUri, "prev", displayZoom, zoomFitMode, panX, panY, oldZoom);

        // Let the browser paint the animation's final frame before C#
        // modifies DOM state (currentIndex/filePath/LoadImageAsync trigger
        // Blazor re-renders that would otherwise compete with the paint).
        if (_jsModule != null)
            await _jsModule.InvokeVoidAsync("waitFrame");

        // Switch state
        currentIndex--; filePath = targetPath;
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        _isAnimating = false;
        ResetHudTimer();

        await ScrollFilmstripToCurrentAsync();
    }

    private async Task GoNext()
    {
        if (!hasNext || _isAnimating) return;
        _isAnimating = true;
        stitchMode = false; stitchImages = null;

        // Get target image URI for animation
        var targetPath = fileList[currentIndex + 1];
        var targetUri = await GetImageDataUriAsync(targetPath);

        // Apply the target image's fit/1:1 zoom *now*, before the transition, so
        // the new-image layer (and the src swap it performs) shows the incoming
        // image at its own correct scale from frame one — not the outgoing
        // image's zoom. The transition function itself is unchanged.
        var oldZoom = displayZoom;
        ApplyZoomFor(targetPath);

        // 3D cylinder transition. panX/panY/outgoingScale passed.
        if (_jsModule != null && targetUri != null && _navAnimationEnabled)
            await _jsModule.InvokeVoidAsync("cylinderTransition", targetUri, "next", displayZoom, zoomFitMode, panX, panY, oldZoom);

        // Let the browser paint the animation's final frame before C#
        // modifies DOM state.
        if (_jsModule != null)
            await _jsModule.InvokeVoidAsync("waitFrame");

        // Switch state
        currentIndex++; filePath = targetPath;
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        _isAnimating = false;
        ResetHudTimer();

        await ScrollFilmstripToCurrentAsync();
    }

    private async Task OnFileDropSelected(int index)
    {
        await OnFilmstripClick(index);
    }

    /// <summary>Get image source (data URI or FileServer URL) from cache or disk.</summary>
    private async Task<string?> GetImageDataUriAsync(string path)
    {
        // Check cache first
        var cached = DecodeCache.Get(path);
        if (cached.HasValue)
            return cached.Value.IsDirectServe ? ServedUrl(path) : cached.Value.DataUri;

        // Load into cache
        try
        {
            // P1: prefer serving the original file directly when the browser can
            // decode it natively — avoids a Skia decode + base64 for every image
            // touched during rapid navigation / flip-through.
            var serve = ImageProcessingService.GetDirectServeInfo(path);
            if (serve.canServe)
            {
                var url = ServedUrl(path);
                DecodeCache.SetDirectServe(path, serve.width, serve.height);
                return url;
            }

            var bytes = await File.ReadAllBytesAsync(path);
            var result = await Task.Run(() =>
                ImageProcessingService.DecodeImage(bytes, Path.GetFileName(path), cacheDir: FileSystem.GetScratchDirectory("DdsDecode")));
            DecodeCache.Set(path, result.DataUri, result.Width, result.Height);
            return result.DataUri;
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns a small (120px) data:URI thumbnail for <paramref name="path"/>,
    /// generating + caching it on first use (s_thumbCache). Used by the filmstrip
    /// flip-through so the rapid animation cards render instantly without
    /// fetching/decoding the full-size image over the loopback FileServer — which,
    /// after P1's streaming change, would stall the 110ms-per-card flip and show
    /// blank/janky frames. Cheap: decodes at most to a 1024px cap and emits a
    /// tiny data:URI. Returns "" if generation fails.
    /// </summary>
    private string GetThumbUri(string path)
    {
        if (s_thumbCache.TryGetValue(path, out var cached) && !string.IsNullOrEmpty(cached))
            return cached;
        try
        {
            var thumb = ImageProcessingService.GenerateThumbnail(path, 120);
            if (!string.IsNullOrEmpty(thumb))
            {
                lock (s_thumbCache) s_thumbCache[path] = thumb;
                return thumb;
            }
        }
        catch { }
        return "";
    }

    /// <summary>
    /// Returns a capped (≤768px) data:URI for one passing image in the
    /// filmstrip multi-slide animation. Cheap (decodes at most to a 768px cap)
    /// and cached, so the rapid visual pass never fetches/decodes the full-size
    /// original over the loopback FileServer. Falls back to the 120px filmstrip
    /// thumbnail if even the capped decode fails. Returns "" on total failure.
    /// </summary>
    // Decode cap (longest side, px) for the fast-pass images laid out during a
    // filmstrip-slide navigation. Kept well below the viewport so whipping
    // through several pictures stays cheap; the on-screen size is corrected in
    // OnFilmstripClick via the decode ratio so the thumbnails render at the
    // SAME size the real image would (true zoom ratio), not at native thumb size.
    private const int NavThumbCap = 768;

    private string GetNavSlideUri(string path)
    {
        if (s_navSlideCache.TryGetValue(path, out var cached) && !string.IsNullOrEmpty(cached))
            return cached;
        try
        {
            var thumb = ImageProcessingService.GenerateThumbnail(path, NavThumbCap);
            if (!string.IsNullOrEmpty(thumb))
            {
                lock (s_navSlideCache) s_navSlideCache[path] = thumb;
                return thumb;
            }
        }
        catch { }
        // Last-resort fallback so a broken decode never shows an empty <img>.
        return GetThumbUri(path);
    }

    // ═══════════ Slide Helpers ═══════════

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
                "document.querySelector('.img-slide')?.style.removeProperty('transform');document.querySelector('.img-slide')?.style.removeProperty('transition')");
    }

    private async Task SetSlideTransitionAsync(int durationMs)
    {
        if (_jsModule != null)
            await _jsModule.InvokeVoidAsync("setSlideTransition", durationMs);
        else
            await JS.InvokeVoidAsync("eval",
                $"document.querySelector('.img-slide')?.style.setProperty('transition','transform {durationMs}ms cubic-bezier(0.4,0,0.2,1)')");
    }

    // ═══════════ Stitch Mode ═══════════

    // Stitch image tokens live in _servedTokens and are revoked together with the
    // main viewer's tokens by RevokeServedTokens() (on dispose / go-back). Nothing
    // to revoke here beyond cancelling the background load loop.
    private void CancelStitchLoad()
    {
        _stitchCts?.Cancel();
        _stitchCts = null;
    }


    private async Task ToggleStitch()
    {
        stitchMode = !stitchMode;
        _stitchZoom = 1.0f;  // fixed — every image fills viewport width
        if (!stitchMode)
        {
            _stitchCts?.Cancel();
            CancelStitchLoad();
            stitchImages = null;
            stitchError = null;
            _stitchLoadedCount = 0;
            _stitchScrollPending = false;
            _stitchTop = Array.Empty<double>();
            _stitchTotalHeight = 0;
            // Re-center the filmstrip — its window may be stale after
            // stitch mode (especially if it was open before vpWidth was
            // measured, leaving _visStart=_visEnd=0).
            if (showFilmstrip && filmstripRef != null)
            {
                // Ensure the filmstrip has the current vpWidth before re-centering.
                await RefreshViewportMetricsAsync();
                await filmstripRef.ScrollToIndexAsync(currentIndex, false);
            }
            return;
        }

        var dims = new List<(string path, int w, int h)>();
        foreach (var p in fileList)
        {
            var c = DecodeCache.Get(p);
            if (c.HasValue) dims.Add((p, c.Value.Width, c.Value.Height));
            else
            {
                var d = await Task.Run(() => ImageProcessingService.GetImageDimensions(p));
                dims.Add((p, d.width, d.height));
            }
        }
        if (dims.Count < 2) { stitchError = "Need at least 2 images"; return; }

        stitchImages = dims.Select(d => new StitchImageInfo { Url = "", Width = d.w, Height = d.h, FileName = Path.GetFileName(d.path) }).ToList();
        _stitchLoadedCount = 0;
        stitchError = null;
        _stitchContentWidth = vpWidth > 0 ? vpWidth : 0;
        _stitchViewportH = vpHeight > 0 ? vpHeight : 0;
        RecomputeStitchLayout();
        _stitchVisibleStart = Math.Max(0, currentIndex - StitchWindowPad);
        _stitchVisibleEnd = Math.Min(stitchImages.Count - 1, currentIndex + StitchWindowPad);
        StateHasChanged();

        _stitchCts = new CancellationTokenSource();
        var cts = _stitchCts;
        try
        {
            int preS = Math.Max(0, currentIndex - 12), preE = Math.Min(stitchImages.Count - 1, currentIndex + 12);
            var prefill = new List<Task>();
            for (int i = preS; i <= preE; i++) prefill.Add(LoadStitchItemAsync(i, cts.Token));
            await Task.WhenAll(prefill);

            var m = await GetStitchMetrics();
            if (m.ClientWidth > 0) _stitchContentWidth = m.ClientWidth;
            if (m.ClientHeight > 0) _stitchViewportH = m.ClientHeight;
            RecomputeStitchLayout();
            StateHasChanged();

            _ = Task.Run(async () =>
            {
                try
                {
                    int n = stitchImages.Count;
                    var order = new List<int>(n) { currentIndex };
                    for (int d = 1; d < n; d++)
                    {
                        int up = currentIndex - d, down = currentIndex + d;
                        if (up >= 0) order.Add(up);
                        if (down < n) order.Add(down);
                    }
                    foreach (int i in order)
                    {
                        if (cts.IsCancellationRequested) return;
                        if (i >= preS && i <= preE) continue;
                        await LoadStitchItemAsync(i, cts.Token);
                        await Task.Delay(15, cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });

            await AnchorStitchScroll();
        }
        catch (OperationCanceledException) { }
        catch (NullReferenceException) { }
    }

    private async Task AnchorStitchScroll()
    {
        if (stitchImages == null || currentIndex < 0 || currentIndex >= stitchImages.Count) return;
        var m = await GetStitchMetrics();
        double vh = m.ClientHeight > 0 ? m.ClientHeight : (_stitchViewportH > 0 ? _stitchViewportH : (vpHeight > 0 ? vpHeight : 0));
        double cw = m.ClientWidth > 0 ? m.ClientWidth : (_stitchContentWidth > 0 ? _stitchContentWidth : (vpWidth > 0 ? vpWidth : 800));
        double w = cw * _stitchZoom;
        var si = stitchImages[currentIndex];
        double h = si.Width > 0 ? w * si.Height / si.Width : w * 0.75;
        double target = _stitchTop[currentIndex] + h / 2 - vh / 2;
        double max = _stitchTotalHeight - vh;
        if (max < 0) max = 0;
        target = Math.Max(0, Math.Min(target, max));
        UpdateStitchWindow(target, vh);
        StateHasChanged();
        await Task.Delay(1);
        if (_jsModule != null)
            await _jsModule.InvokeVoidAsync("setStitchScrollTop", target);
        else
            await JS.InvokeVoidAsync("eval",
                $"(function(){{var c=document.querySelector('.v-stitch-container');if(c)c.scrollTop={target.ToString("F1")};}})()");
    }

    private async Task LoadStitchItemAsync(int i, CancellationToken ct)
    {
        if (stitchImages == null || i < 0 || i >= stitchImages.Count) return;
        if (!string.IsNullOrEmpty(stitchImages[i].Url)) return;
        try
        {
            ct.ThrowIfCancellationRequested();
            var path = fileList[i];
            if (stitchImages == null) return;
            // Register the file with the local HTTP server and get a token-based
            // served URL. The browser streams the image over HTTP (Range-supported),
            // so the full file is NEVER read into a C#/JS buffer — this replaces the
            // previous createBlobUrl bridge which did streamRef.arrayBuffer() of the
            // entire image (memory explosion for large / many images).
            var url = ServedUrl(path);
            if (stitchImages == null) return;
            stitchImages[i].Url = url;
            _stitchLoadedCount++;
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) { throw; }
        catch { Debug.WriteLine($"[Stitch] Failed to register: {fileList[i]}"); }
    }

    private void RecomputeStitchLayout()
    {
        if (stitchImages == null) return;
        int n = stitchImages.Count;
        if (_stitchTop.Length != n) _stitchTop = new double[n];
        double w = _stitchContentWidth > 0 ? _stitchContentWidth * _stitchZoom : 0;
        double top = 0;
        for (int i = 0; i < n; i++)
        {
            _stitchTop[i] = top;
            var si = stitchImages[i];
            double h = w > 0 ? (si.Width > 0 ? w * si.Height / si.Width : w * 0.75) : 0;
            top += h + _stitchGap;
        }
        _stitchTotalHeight = top;
    }

    private void UpdateStitchWindow(double scrollTop, double vh)
    {
        if (stitchImages == null) return;
        int n = stitchImages.Count;
        if (n == 0) return;
        double w = _stitchContentWidth > 0 ? _stitchContentWidth * _stitchZoom : 0;
        double pad = vh * 0.5;
        double regionTop = scrollTop - pad;
        double regionBottom = scrollTop + vh + pad;
        int s = n - 1, e = 0;
        for (int i = 0; i < n; i++)
        {
            double h = w > 0 ? (stitchImages[i].Width > 0 ? w * stitchImages[i].Height / stitchImages[i].Width : w * 0.75) : 0;
            double bottom = _stitchTop[i] + h;
            if (bottom > regionTop) s = Math.Min(s, i);
            if (_stitchTop[i] < regionBottom) e = Math.Max(e, i);
        }
        if (s > e) s = e;
        s = Math.Max(0, s - StitchWindowPad);
        e = Math.Min(n - 1, e + StitchWindowPad);
        if (s != _stitchVisibleStart || e != _stitchVisibleEnd)
        {
            _stitchVisibleStart = s;
            _stitchVisibleEnd = e;
            StateHasChanged();
        }
    }

    private string? GetStitchSrc(int i) =>
        (stitchImages != null && i >= 0 && i < stitchImages.Count && !string.IsNullOrEmpty(stitchImages[i].Url))
            ? stitchImages[i].Url : null;

    private async Task OnStitchScroll()
    {
        if (stitchImages == null) return;
        if (_stitchScrollPending) return;
        _stitchScrollPending = true;
        try { await Task.Delay(16); } catch (TaskCanceledException) { }
        _stitchScrollPending = false;
        if (stitchImages == null) return;
        var m = await GetStitchMetrics();
        if (m.ClientWidth > 0) _stitchContentWidth = m.ClientWidth;
        if (m.ClientHeight > 0) _stitchViewportH = m.ClientHeight;
        RecomputeStitchLayout();
        UpdateStitchWindow(m.ScrollTop, m.ClientHeight);
    }

    private async Task<StitchMetrics> GetStitchMetrics()
    {
        if (_jsModule != null)
        {
            var arr = await _jsModule.InvokeAsync<double[]>("getStitchMetrics");
            return new StitchMetrics(arr[0], arr[1], arr[2]);
        }
        var r = await JS.InvokeAsync<double[]>("eval",
            "(() => { var c = document.querySelector('.v-stitch-container'); if(!c) return [0,0,0]; return [c.scrollTop, c.clientHeight, c.clientWidth]; })()");
        return new StitchMetrics(r[0], r[1], r[2]);
    }

    private record StitchMetrics(double ScrollTop, double ClientHeight, double ClientWidth);

    // ═══════════ Dispose ═══════════

    public async ValueTask DisposeAsync()
    {
        _hudTimer?.Dispose();
        _dotNetRef?.Dispose();

        if (_jsModule != null)
        {
            try { await _jsModule.InvokeVoidAsync("disposeResizeHandler"); } catch { }
            try { await _jsModule.InvokeVoidAsync("disposeGestureTracker"); } catch { }
            try { await _jsModule.DisposeAsync(); } catch { }
        }

        RevokeServedTokens();
        _stitchCts?.Cancel();
    }
}
