using System.IO;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MauiMultimedia.Core.Abstractions;

namespace MauiMultimedia.Viewers.Model3D.Pages;

public partial class Model3DPage : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IFileNavigationState NavState { get; set; } = null!;
    [Inject] private IMauiNavigation MauiNav { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private IJSObjectReference? _jsModule;
    private string filePath = "";
    private string fileName = "";
    private bool isLoading = true;
    private string? errorMessage;
    private string? _modelUrl;
    private bool _isGlb;
    private bool _scriptsReady;

    private sealed record ScriptLoadStatus(
        bool Ok,
        System.Collections.Generic.List<string> Failed,
        bool Three,
        bool ModelViewer);

    protected override async Task OnInitializedAsync()
    {
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
                    // GLB 路径：仅需 <model-viewer> 自定义元素就绪，由组件内 <model-viewer> 自动升级
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
                    // STL/OBJ 路径：initThree 内部会先 await ensureScriptsLoaded 并校验 THREE，失败抛异常
                    await _jsModule.InvokeVoidAsync("initThree", "three-canvas", _modelUrl, Path.GetExtension(filePath).ToLowerInvariant());
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

    private async Task LoadAsync()
    {
        var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
        filePath = NavState.CurrentFilePath
            ?? uri.Query.TrimStart('?').Split('&')
                .Select(p => p.Split('=', 2))
                .Where(kv => kv.Length == 2 && kv[0] == "path")
                .Select(kv => Uri.UnescapeDataString(kv[1]))
                .FirstOrDefault() ?? "";
        fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        _isGlb = ext == ".glb" || ext == ".gltf";

        if (string.IsNullOrEmpty(filePath))
        {
            errorMessage = "未指定文件路径";
            isLoading = false;
            return;
        }

        isLoading = true;
        errorMessage = null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var mime = ext switch
            {
                ".gltf" => "model/gltf+json",
                ".glb" => "model/gltf-binary",
                ".stl" => "application/sla",
                ".obj" => "text/plain",
                _ => "application/octet-stream"
            };
            var streamRef = new DotNetStreamReference(new MemoryStream(bytes));
            _modelUrl = await JS.InvokeAsync<string>("createBlobUrl", streamRef, mime);
        }
        catch (Exception ex)
        {
            errorMessage = $"读取模型失败：{ex.Message}";
        }
        finally { isLoading = false; StateHasChanged(); }
    }

    private void GoBack() { _ = MauiNav.GoBackAsync(); }
}
