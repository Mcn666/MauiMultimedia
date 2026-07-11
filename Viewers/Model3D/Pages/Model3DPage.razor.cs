using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Viewers.Image.Services;
using MauiMultimedia.Viewers.Model3D.Services;

namespace MauiMultimedia.Viewers.Model3D.Pages;

public partial class Model3DPage : ComponentBase, IAsyncDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IFileServerService FileServer { get; set; } = null!;

    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".stl", ".obj", ".fbx", ".pmx", ".vrm"
    };

    private IJSObjectReference? _jsModule;
    private string filePath = "";
    private string fileName = "";
    private bool isLoading = true;
    private string? errorMessage;
    private string? _modelUrl;
    private bool _isGlb;
    private bool _scriptsReady;
    private List<string> fileList = new();
    private int currentIndex = -1;
    private string? _dirToken;
    private string? _textureDataJson;

    private sealed record ScriptLoadStatus(
        bool Ok,
        System.Collections.Generic.List<string> Failed,
        bool Three,
        bool ModelViewer);

    protected override async Task OnInitializedAsync()
    {
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
                var asm = typeof(Model3DPage).Assembly.GetName().Name!;
                _jsModule = await JS.InvokeAsync<IJSObjectReference>("import",
                    $"./_content/{asm}/Pages/Model3DPage.razor.js");
            }
            catch (Exception ex)
            {
                errorMessage = $"加载 3D 查看器脚本失败：{ex.Message}";
                StateHasChanged();
                return;
            }
        }

        if (!isLoading && string.IsNullOrEmpty(errorMessage) && _jsModule != null && !_scriptsReady)
        {
            _scriptsReady = true;
            try
            {
                if (_isGlb)
                {
                    var status = await _jsModule.InvokeAsync<ScriptLoadStatus>("ensureScriptsLoaded");
                    if (!status.Ok || !status.ModelViewer)
                    {
                        var failed = status.Failed.Count > 0 ? "脚本[" + string.Join(",", status.Failed) + "]加载失败；" : "";
                        var mv = !status.ModelViewer ? "<model-viewer> 未注册；" : "";
                        errorMessage = $"3D 引擎加载失败：{failed}{mv}";
                        StateHasChanged();
                        return;
                    }
                }
                else
                {
                    await _jsModule.InvokeVoidAsync("initThree", "three-canvas", _modelUrl, Path.GetExtension(filePath).ToLowerInvariant(), _textureDataJson);
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"加载 3D 引擎失败：{ex.Message}";
                StateHasChanged();
                return;
            }
        }
    }

    private async Task LoadAsync(string? path = null)
    {
        if (path != null) filePath = path;
        else
        {
            var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
            filePath = NavState.CurrentFilePath
                ?? uri.Query.TrimStart('?').Split('&')
                    .Select(p => p.Split('=', 2))
                    .Where(kv => kv.Length == 2 && kv[0] == "path")
                    .Select(kv => Uri.UnescapeDataString(kv[1]))
                    .FirstOrDefault() ?? "";
        }

        if (string.IsNullOrEmpty(filePath))
        {
            errorMessage = "未指定文件路径";
            isLoading = false;
            return;
        }

        fileName = Path.GetFileName(filePath);
        if (path == null)
            currentIndex = fileList.FindIndex(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        // ── 自动转换旧版 3D 格式到 GLB ──
        string modelPath = filePath;
        if (!ext.Equals(".glb") && !ext.Equals(".gltf") && !ext.Equals(".vrm") &&
            !ext.Equals(".stl") && !ext.Equals(".pmx") && !ext.Equals(".fbx") && !ext.Equals(".obj"))
        {
            var converted = FbxConversionService.ConvertToGlb(filePath);
            if (converted != null)
            {
                modelPath = converted;
                ext = ".glb";
            }
        }

        _isGlb = ext == ".glb" || ext == ".gltf" || ext == ".vrm";

        // Release old model URL and register new directory token
        _modelUrl = null;
        _scriptsReady = false;

        isLoading = true;
        errorMessage = null;
        try
        {
            // ── 只预加载 DDS 贴图（PNG/JPG 由虚拟主机或 FileServer 直接加载） ──
            string? textureDataJson = null;
            var texDir = Path.GetDirectoryName(modelPath)!;
            var texMap = new Dictionary<string, string>();
            foreach (var texFile in Directory.GetFiles(texDir, "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(texFile).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var result = ImageProcessingService.DecodeDds(texFile);
                        if (result.dataUri != null)
                            texMap[Path.GetFileName(texFile)] = result.dataUri;
                    }
                    catch { }
                }
            }
            // OBJ 模型：同名的 .mtl 材质文件（纯文本，极小）
            if (ext == ".obj")
            {
                var mtlPath = Path.ChangeExtension(modelPath, ".mtl");
                if (File.Exists(mtlPath))
                {
                    try { texMap["__mtl__"] = await File.ReadAllTextAsync(mtlPath); }
                    catch { }
                }
            }
            if (texMap.Count > 0)
                textureDataJson = System.Text.Json.JsonSerializer.Serialize(texMap);
            _textureDataJson = textureDataJson;

            // ── 构建模型 URL（使用 FileServer 目录令牌） ──
            var modelDir = Path.GetDirectoryName(modelPath)!;
            _dirToken = FileServer.RegisterDirectory(modelDir);
            var modelName = Path.GetFileName(modelPath);
            _modelUrl = $"{FileServer.BaseUrl}/dir/{_dirToken}/{Uri.EscapeDataString(modelName)}";
        }
        catch (Exception ex)
        {
            errorMessage = $"读取模型失败：{ex.Message}";
        }
        finally { isLoading = false; StateHasChanged(); }
    }

    private async Task OnFileSelected(int index)
    {
        if (index < 0 || index >= fileList.Count || index == currentIndex) return;
        currentIndex = index;
        await LoadAsync(fileList[currentIndex]);
    }

    private void GoBack() { _ = MauiNav.GoBackAsync(); }

    public async ValueTask DisposeAsync()
    {
        if (_dirToken != null)
        {
            try { FileServer.UnregisterDirectory(_dirToken); }
            catch { }
            _dirToken = null;
        }

        if (_jsModule != null)
        {
            try { await _jsModule.DisposeAsync(); }
            catch { }
        }
    }
}
