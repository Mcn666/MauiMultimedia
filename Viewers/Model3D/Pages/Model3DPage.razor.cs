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
    private bool _threeReady;

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
            catch { }
        }

        if (!isLoading && string.IsNullOrEmpty(errorMessage) && !_isGlb && !_threeReady && _modelUrl != null && _jsModule != null)
        {
            _threeReady = true;
            await Task.Delay(1);
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            await _jsModule.InvokeVoidAsync("initThree", "three-canvas", _modelUrl, ext);
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
