namespace MauiMultimedia.Viewers.Model3D;

/// <summary>
/// 3D 模型查看器支持的扩展名常量。
/// </summary>
public static class Model3DConstants
{
    public static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".stl", ".obj", ".fbx", ".pmx", ".vrm"
    };
}
