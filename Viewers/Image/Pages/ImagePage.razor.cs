using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Viewers.Image.Components;
using MauiMultimedia.Viewers.Image.Services;

namespace MauiMultimedia.Viewers.Image.Pages;

public partial class ImagePage : ComponentBase, IAsyncDisposable
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

    // ── File state ──
    private string filePath = "";
    private string fileName = "";
    private string? imageSource;
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
        public string BlobUrl { get; set; } = "";
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
    // True ONLY once a REAL fit/1:1 zoom has been computed (both the image
    // dimensions AND the viewport geometry were known). Drives ZoomReady: the
    // image stays hidden until this is true, so it can never flash a default
    // 1.0 scale (200% on HiDPI) on the first frame.
    private bool _zoomComputed;

    // ── Drag ──
    private float panX, panY;
    private bool isDragging;
    private float dragStartX, dragStartY, panAtDragStartX, panAtDragStartY;

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
    private string? _imageToken;

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
                        _dotNetRef, ".gesture-layer", 80);

                    // Position the filmstrip at the current image *instantly* on
                    // open. The child snaps scrollLeft (no animation) and
                    // suppresses its own scroll-driven window recompute, so we
                    // never animate 0 -> currentIndex or spam thumbnail loads.
                    // Call the child directly (not via the _filmstripBuilt-gated
                    // wrapper) so open positioning can't be skipped by build
                    // timing — the child guards on Items.Count itself.
                    if (!stitchMode && showFilmstrip && currentIndex >= 0 && filmstripRef != null)
                        await filmstripRef.ScrollToIndexAsync(currentIndex, false);
                }
            }
            catch { }

            StartHudTimer();

            // Measure the viewport, then auto-select fit/1:1 for the current image
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
                    "(() => { var v = document.querySelector('.image-viewport'); return v ? [v.offsetWidth, v.offsetHeight, window.devicePixelRatio || 1] : [0,0,1]; })()");
            vpWidth = (float)dims[0];
            vpHeight = (float)dims[1];
            _dpr = (float)dims[2];
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

    [JSInvokable]
    public async Task OnGestureRelease(double offsetX, double velocity)
    {
        if (_isAnimating) return;

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
            // Spring back to 0
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("springStart", ".img-slide",
                    offsetX, 0, velocity,
                    new { stiffness = 400, damping = 30, mass = 1 });
            }
            else
            {
                await ClearSlideTransformAsync();
            }
            return;
        }

        // Navigate
        if (toPrev && hasPrev) await DoSpringNavigate(-1, offsetX, velocity);
        else if (!toPrev && hasNext) await DoSpringNavigate(1, offsetX, velocity);
        else
        {
            // Boundary — spring back
            if (_jsModule != null)
            {
                await _jsModule.InvokeVoidAsync("springStart", ".img-slide",
                    offsetX, 0, velocity,
                    new { stiffness = 400, damping = 30, mass = 1 });
            }
            else
            {
                await ClearSlideTransformAsync();
            }
        }
    }

    private async Task DoSpringNavigate(int direction, double fromX, double velocity)
    {
        // direction: -1 = prev, 1 = next
        _isAnimating = true;

        double targetX = direction > 0 ? -vpWidth : vpWidth;

        // Animate out
        if (_jsModule != null)
        {
            await _jsModule.InvokeVoidAsync("springStart", ".img-slide",
                fromX, targetX, velocity,
                new { stiffness = 250, damping = 25, mass = 1 });
        }
        else
        {
            // Fallback: use CSS transition
            await SetSlideTransitionAsync(280);
            await SetSlideTransformAsync($"{targetX}px");
            await Task.Delay(300);
        }

        // Switch image
        await ClearSlideTransformAsync();
        currentIndex += direction;
        filePath = fileList[currentIndex];
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();

        // Position off-screen opposite side, animate in
        double startX = direction > 0 ? vpWidth : -vpWidth;
        await SetSlideTransformAsync($"{startX}px");
        StateHasChanged();
        await Task.Delay(16);

        if (_jsModule != null)
        {
            await _jsModule.InvokeVoidAsync("springStart", ".img-slide",
                startX, 0, 0,
                new { stiffness = 250, damping = 25, mass = 1 });
        }
        else
        {
            await SetSlideTransitionAsync(280);
            await SetSlideTransformAsync("0px");
            await Task.Delay(300);
        }

        await ClearSlideTransformAsync();
        _isAnimating = false;

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
                var cached = DecodeCache.Get(path);
                if (cached.HasValue)
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

        // Pick 2-4 evenly-spaced intermediate images
        int steps = Math.Min(dist, 4);
        var imageUris = new string?[steps];
        for (int s = 1; s <= steps; s++)
        {
            int idx = currentIndex + dir * (int)Math.Round((double)dist * s / steps);
            if (idx == currentIndex) idx += dir;
            if (idx < 0 || idx >= fileList.Count) { imageUris[s - 1] = null; continue; }
            imageUris[s - 1] = await GetImageDataUriAsync(fileList[idx]);
        }

        // Apply the target image's fit/1:1 zoom before the transition so the
        // real wrap is pinned to it the moment the cards are removed.
        ApplyZoomFor(fileList[index]);

        // Animate rapid card flip
        if (_jsModule != null && _navAnimationEnabled)
            await _jsModule.InvokeVoidAsync("flipThroughTransition",
                imageUris.Where(u => u != null).ToArray(),
                dir > 0 ? "next" : "prev",
                displayZoom, zoomFitMode);

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
        if (showDetails) showFilmstrip = false;
        else showFilmstrip = showToolbar;
        _ = RecomputeZoomForChromeAsync();
    }
    private void CloseDetails() { showDetails = false; showFilmstrip = showToolbar; _ = RecomputeZoomForChromeAsync(); }

    private async Task LoadImageAsync()
    {
        isLoading = true;
        errorMessage = null;
        imageSource = null;

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
            var cached = DecodeCache.Get(filePath);
            if (cached.HasValue)
            {
                ApplyDecoded(cached.Value.DataUri, cached.Value.Width, cached.Value.Height);
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
                    try
                    {
                        _imageToken = FileServer.RegisterFile(filePath);
                        imageSource = $"{FileServer.BaseUrl}/file?token={_imageToken}";
                        var bytes = await File.ReadAllBytesAsync(filePath);
                        var dims = ImageProcessingService.GetImageDimensions(bytes);
                        imageWidth = dims.width;
                        imageHeight = dims.height;
                        AutoSelectZoom();
                    }
                    catch
                    {
                        _imageToken = null;
                        imageSource = null;
                        errorMessage = $"Cannot decode: {fileName}";
                    }
                }
            });

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
        displayZoom = Math.Clamp(displayZoom, MinZoom, MaxZoom);
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
                var bytes = await File.ReadAllBytesAsync(p);
                var result = await Task.Run(() =>
                    ImageProcessingService.DecodeImage(bytes, Path.GetFileName(p)));
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
    }

    private async Task ToggleFitActual()
    {
        ResetHudTimer();
        if (zoomFitMode) await ZoomActual(); else await ZoomFit();
    }

    private float GetOneToOneZoom() => Math.Max(1.0f / _dpr, 0.01f);

    // ═══════════ Wheel ═══════════

    private void OnWheel(WheelEventArgs e)
    {
        ResetHudTimer();
        float cx = (float)e.ClientX;
        float cy = (float)e.ClientY;
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
        panX = panAtDragStartX + (float)(e.ClientX - dragStartX);
        panY = panAtDragStartY + (float)(e.ClientY - dragStartY);
        ClampPan();
        StateHasChanged();
    }

    private void OnPointerUp(PointerEventArgs e) => isDragging = false;

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

    private void OnTouchMove(TouchEventArgs e)
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
        if (_imageToken != null) { try { FileServer.UnregisterFile(_imageToken); } catch { } _imageToken = null; }
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
        ApplyZoomFor(targetPath);

        // 3D cylinder transition (pass the target image's zoom so the real
        // wrap can be pinned to it the moment the clones are removed)
        if (_jsModule != null && targetUri != null && _navAnimationEnabled)
            await _jsModule.InvokeVoidAsync("cylinderTransition", targetUri, "prev", displayZoom, zoomFitMode);

        // Switch state
        currentIndex--; filePath = targetPath;
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        _isAnimating = false;

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
        ApplyZoomFor(targetPath);

        // 3D cylinder transition (pass the target image's zoom so the real
        // wrap can be pinned to it the moment the clones are removed)
        if (_jsModule != null && targetUri != null && _navAnimationEnabled)
            await _jsModule.InvokeVoidAsync("cylinderTransition", targetUri, "next", displayZoom, zoomFitMode);

        // Switch state
        currentIndex++; filePath = targetPath;
        fileName = Path.GetFileName(filePath);
        ResetView();
        await LoadImageAsync();
        _isAnimating = false;

        await ScrollFilmstripToCurrentAsync();
    }

    /// <summary>Get image data URI from cache or load from disk.</summary>
    private async Task<string?> GetImageDataUriAsync(string path)
    {
        // Check cache first
        var cached = DecodeCache.Get(path);
        if (cached.HasValue) return cached.Value.DataUri;

        // Load into cache
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var result = await Task.Run(() =>
                ImageProcessingService.DecodeImage(bytes, Path.GetFileName(path)));
            DecodeCache.Set(path, result.DataUri, result.Width, result.Height);
            return result.DataUri;
        }
        catch { return null; }
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

    private async Task RevokeBlobUrls()
    {
        _stitchCts?.Cancel();
        _stitchCts = null;
        if (stitchImages != null)
        {
            var urls = stitchImages.Select(si => si.BlobUrl).Where(u => !string.IsNullOrEmpty(u)).ToArray();
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

    private async Task StitchGapUp()
    {
        _stitchGap = Math.Min(_stitchGap + 2, 20);
        RecomputeStitchLayout();
        StateHasChanged();
        await AnchorStitchScroll();
    }

    private async Task StitchGapDown()
    {
        _stitchGap = Math.Max(_stitchGap - 2, 0);
        RecomputeStitchLayout();
        StateHasChanged();
        await AnchorStitchScroll();
    }

    private async Task ToggleStitch()
    {
        stitchMode = !stitchMode;
        _stitchZoom = Math.Clamp(displayZoom, 0.3f, 3.0f);
        if (!stitchMode)
        {
            _stitchCts?.Cancel();
            await RevokeBlobUrls();
            stitchImages = null;
            stitchError = null;
            _stitchLoadedCount = 0;
            _stitchScrollPending = false;
            _stitchTop = Array.Empty<double>();
            _stitchTotalHeight = 0;
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

        stitchImages = dims.Select(d => new StitchImageInfo { BlobUrl = "", Width = d.w, Height = d.h, FileName = Path.GetFileName(d.path) }).ToList();
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
        if (!string.IsNullOrEmpty(stitchImages[i].BlobUrl)) return;
        try
        {
            ct.ThrowIfCancellationRequested();
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
        catch { Debug.WriteLine($"[Stitch] Failed to load: {fileList[i]}"); }
    }

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
        (stitchImages != null && i >= 0 && i < stitchImages.Count && !string.IsNullOrEmpty(stitchImages[i].BlobUrl))
            ? stitchImages[i].BlobUrl : null;

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
            try { await _jsModule.InvokeVoidAsync("disposeGestureTracker"); } catch { }
            try { await _jsModule.DisposeAsync(); } catch { }
        }

        if (_imageToken != null)
        {
            try { FileServer.UnregisterFile(_imageToken); } catch { }
        }

        _stitchCts?.Cancel();
        if (stitchImages != null)
        {
            var urls = stitchImages.Select(si => si.BlobUrl).Where(u => !string.IsNullOrEmpty(u)).ToArray();
            if (urls.Length > 0 && _jsModule != null)
            {
                try { await _jsModule.InvokeVoidAsync("revokeBlobUrls", urls); } catch { }
            }
        }
    }
}
