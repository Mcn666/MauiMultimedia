using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Core.Models;
using MauiMultimedia.Viewers.Model3D.Pages;

namespace MauiMultimedia.Viewers.Model3D;

public class Model3DViewer : IFileViewer
{
    private static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".stl", ".obj"
    };

    public string DisplayName => "3D 模型查看器";
    public Type ComponentType => typeof(Model3DPage);

    public bool CanHandle(FileSystemItem item) =>
        !item.IsFolder && Exts.Contains(Path.GetExtension(item.Name));
}
