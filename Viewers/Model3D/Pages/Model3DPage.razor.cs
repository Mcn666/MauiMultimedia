using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Utils;
using MauiMultimedia.Viewers.Shared.Services;
using MauiMultimedia.Viewers.Model3D.Services;

namespace MauiMultimedia.Viewers.Model3D.Pages;

public partial class Model3DPage : ComponentBase, IAsyncDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IFileServerService FileServer { get; set; } = null!;
    [Inject] private IFileSystemService FileSystem { get; set; } = null!;

    private static readonly HashSet<string> Exts = Model3DConstants.Exts;

    private IJSObjectReference? _jsModule;
    private string filePath = "";
    private string fileName = "";
    private bool isLoading = true;
    private string? errorMessage;
    private string? _modelUrl;
    private bool _viewerReady;
    private string _modelExt = "";
    private List<string> fileList = new();
    private int currentIndex = -1;
    private string? _dirToken;
    private string? _texDirToken;
    // 纹理 scratch 目录的物理路径：仅在创建时赋值（Model3DTextures/{guid}），
    // 用于加载前/销毁时删除磁盘上的 PNG。模型目录(_dirToken)指向用户真实文件或 FBX 转换目录，
    // 只注销令牌、绝不删物理文件，故不在这里记录其路径。
    private string? _texDirPath;
    private string? _textureDataJson;
    private bool _lightsOn = true;
    private bool _gridOn = true;

    protected override async Task OnInitializedAsync()
    {
        // 启动清扫：删除历史遗留的纹理 scratch 子目录。此前版本只注销 FileServer 令牌而不删
        // 物理文件，多次切换会在 Model3DTextures 下累积大量 {guid} 目录。该目录专用、其下皆为
        // 本组件生成的 {guid} 子目录，整体清空即可；跨进程的服务端注册不会持久化，故旧目录均为孤儿。
        try
        {
            var texRoot = FileSystem.GetScratchDirectory("Model3DTextures");
            if (Directory.Exists(texRoot))
            {
                foreach (var d in Directory.GetDirectories(texRoot))
                {
                    try { Directory.Delete(d, recursive: true); }
                    catch { }
                }
            }
        }
        catch { }

        fileList = NavState.CurrentDirectoryFiles?
            .Where(f => Exts.Contains(Path.GetExtension(f)))
            .Select(f => { try { return Path.GetFullPath(f); } catch { return f; } })
            .ToList() ?? new();
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

        if (!isLoading && string.IsNullOrEmpty(errorMessage) && _jsModule != null && !_viewerReady)
        {
            _viewerReady = true;
            try
            {
                // 查看器（renderer/scene/controls/动画循环）只初始化一次；
                // 之后切换文件只调用 loadModel，复用同一套渲染管线，避免重复创建 WebGL 上下文 / 动画循环
                // 导致多套循环抢同一块 canvas、旧场景残留而“丢失部分模型”。
                await _jsModule.InvokeVoidAsync("initViewer", "three-canvas");
                await _jsModule.InvokeVoidAsync("loadModel", _modelUrl, _modelExt, _textureDataJson);
                // 应用工具栏初始开关状态（灯光 / 网格）
                await _jsModule.InvokeVoidAsync("setLights", _lightsOn);
                await _jsModule.InvokeVoidAsync("setGrid", _gridOn);
            }
            catch (Exception ex)
            {
                _viewerReady = false;
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
            var converted = FbxConversionService.ConvertToGlb(filePath, FileSystem.GetScratchDirectory("ModelConvert"));
            if (converted != null)
            {
                modelPath = converted;
                ext = ".glb";
            }
        }

        _modelExt = ext;   // 实际交给渲染器的模型扩展名（转换后的 .glb 也算）

        // 释放上一次加载注册的目录令牌，避免每次切换都泄漏一个目录注册
        ReleaseLoadTokens();

        // Release old model URL and register new directory token
        _modelUrl = null;

        isLoading = true;
        errorMessage = null;
        try
        {
            // ── 只预解码 DDS 贴图 ──
            // 关键性能修复：之前把每个 DDS 解成 PNG 的 base64 data URI 直接塞进 JSON，
            // 多个大贴图会让 JSON 膨胀到几十 MB，跨线程传输 + 在 WebView 主线程
            // JSON.parse 会把整个界面卡死。现改为：后台线程解码成磁盘 PNG 文件，
            // 通过 FileServer 目录以 URL 提供，JSON 里只放短 URL，THREE 异步拉取。
            string? textureDataJson = null;
            string? newTexDirToken = null;
            var texMap = new Dictionary<string, string>(); // 原文件名(.dds) -> 解码后 PNG 的 URL
            await Task.Run(() =>
            {
                try
                {
                    var texDir = Path.GetDirectoryName(modelPath)!;
                    // 仅扫描模型同级目录（贴图通常与模型同目录），避免递归扫描整个子树
                    // 解码无关 DDS 造成的不必要卡顿
                    var ddsFiles = Directory.GetFiles(texDir, "*.dds", SearchOption.TopDirectoryOnly);
                    // 重置：本次若无 DDS 贴图，则不持有任何待回收的纹理目录（避免误删上次的）
                    _texDirPath = null;
                    if (ddsFiles.Length > 0)
                    {
                        var texScratch = Path.Combine(
                            FileSystem.GetScratchDirectory("Model3DTextures"),
                            Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(texScratch);
                        _texDirPath = texScratch;
                        newTexDirToken = FileServer.RegisterDirectory(texScratch, "image/png");
                        foreach (var texFile in ddsFiles)
                        {
                            var pngName = Path.ChangeExtension(Path.GetFileName(texFile), ".png");
                            var pngPath = Path.Combine(texScratch, pngName);
                            try
                            {
                                if (DdsDecoder.DecodeDdsToFile(texFile, pngPath))
                                {
                                    texMap[Path.GetFileName(texFile)] =
                                        $"{FileServer.BaseUrl}/dir/{newTexDirToken}/{Uri.EscapeDataString(pngName)}";
                                }
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
                            try { texMap["__mtl__"] = File.ReadAllText(mtlPath); }
                            catch { }
                        }
                    }
                    if (texMap.Count > 0)
                        textureDataJson = System.Text.Json.JsonSerializer.Serialize(texMap);
                }
                catch { }
            });
            _texDirToken = newTexDirToken;
            _textureDataJson = textureDataJson;

            // ── 构建模型 URL（使用 FileServer 目录令牌） ──
            // 3D 查看器为目录声明默认 MIME，填补标准表未覆盖的模型格式（如 .fbx/.pmx）。
            var modelDir = Path.GetDirectoryName(modelPath)!;
            _dirToken = FileServer.RegisterDirectory(modelDir, "model/gltf-binary");
            var modelName = Path.GetFileName(modelPath);
            _modelUrl = $"{FileServer.BaseUrl}/dir/{_dirToken}/{Uri.EscapeDataString(modelName)}";
        }
        catch (Exception ex)
        {
            errorMessage = $"读取模型失败：{ex.Message}";
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
            // 查看器已初始化时，切换文件只需加载新模型（复用同一渲染管线，不再重建 WebGL 上下文）
            if (_viewerReady && _jsModule != null && !string.IsNullOrEmpty(_modelUrl))
            {
                try
                {
                    await _jsModule.InvokeVoidAsync("loadModel", _modelUrl, _modelExt, _textureDataJson);
                }
                catch (Exception ex)
                {
                    errorMessage = $"加载模型失败：{ex.Message}";
                    StateHasChanged();
                }
            }
        }
    }

    // 释放上一次加载时注册的目录令牌（模型目录 + DDS 纹理目录），并删除磁盘上的纹理 scratch
    // 目录，防止每次切换模型泄漏一个目录注册 / 累积一堆解码出的 PNG。
    // 注意：模型目录(_dirToken)指向用户真实文件目录或 FBX 转换目录，只注销令牌、绝不删除物理文件。
    private void ReleaseLoadTokens()
    {
        if (_texDirToken != null)
        {
            try { FileServer.UnregisterDirectory(_texDirToken); }
            catch { }
            _texDirToken = null;
        }
        if (_texDirPath != null)
        {
            DeleteTextureDir(_texDirPath);
            _texDirPath = null;
        }
        if (_dirToken != null)
        {
            try { FileServer.UnregisterDirectory(_dirToken); }
            catch { }
            _dirToken = null;
        }
    }

    // 删除磁盘上的纹理 scratch 子目录（含解码出的 PNG）。仅删纹理目录，绝不碰模型目录。
    // 文件被占用/权限不足等异常一律忽略：下次加载前会再尝试回收，不会因此崩溃。
    private static void DeleteTextureDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    private async Task OnFileSelected(int index)
    {
        if (index < 0 || index >= fileList.Count || index == currentIndex) return;
        currentIndex = index;
        await LoadAsync(fileList[currentIndex]);
    }

    private void GoBack() { _ = MauiNav.GoBackAsync(); }

    // 工具栏：灯光开关（关灯 = 无光照 unlit 预览）
    private async Task ToggleLights()
    {
        _lightsOn = !_lightsOn;
        if (_viewerReady && _jsModule != null)
        {
            try { await _jsModule.InvokeVoidAsync("setLights", _lightsOn); }
            catch { }
        }
        StateHasChanged();
    }

    // 工具栏：网格地面（GridHelper）显隐开关
    private async Task ToggleGrid()
    {
        _gridOn = !_gridOn;
        if (_viewerReady && _jsModule != null)
        {
            try { await _jsModule.InvokeVoidAsync("setGrid", _gridOn); }
            catch { }
        }
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseLoadTokens();

        if (_jsModule != null)
        {
            try { await _jsModule.DisposeAsync(); }
            catch { }
        }
    }
}
