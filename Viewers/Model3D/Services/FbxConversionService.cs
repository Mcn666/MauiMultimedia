using System.IO;
using Assimp;

namespace MauiMultimedia.Viewers.Model3D.Services;

/// <summary>
/// 3D 模型格式转换服务。先尝试 AssimpNet，失败后回退到内置 FBX 6.x 解析器。
/// 转换结果缓存到临时目录。
/// </summary>
public static class FbxConversionService
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> ConvertibleExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".dae", ".3ds", ".obj", ".blend", ".x"
    };

    public static bool NeedsConversion(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ConvertibleExts.Contains(ext);
    }

    public static string? ConvertToGlb(string filePath, string? cacheDir = null)
    {
        if (!File.Exists(filePath)) return null;

        var cacheKey = Path.GetFullPath(filePath).Replace(':', '_').Replace('\\', '_').Replace('/', '_');
        var workDir = Path.Combine(cacheDir ?? Path.GetTempPath(), "MauiMM_ModelConvert");
        Directory.CreateDirectory(workDir);
        var glbPath = Path.Combine(workDir, cacheKey + ".glb");

        if (File.Exists(glbPath))
        {
            if (File.GetLastWriteTimeUtc(glbPath) >= File.GetLastWriteTimeUtc(filePath)) return glbPath;
        }

        lock (_lock)
        {
            if (File.Exists(glbPath))
            {
                if (File.GetLastWriteTimeUtc(glbPath) >= File.GetLastWriteTimeUtc(filePath)) return glbPath;
            }

            // Attempt 1: AssimpNet
            try
            {
                using var ctx = new Assimp.AssimpContext();
                var scene = ctx.ImportFile(filePath, Assimp.PostProcessSteps.Triangulate | Assimp.PostProcessSteps.GenerateSmoothNormals | Assimp.PostProcessSteps.FlipUVs);
                if (scene != null && scene.HasMeshes && ctx.ExportFile(scene, glbPath, "glb2", Assimp.PostProcessSteps.None))
                    return glbPath;
            }
            catch { }

            // Attempt 2: Built-in FBX 6.x binary converter
            try
            {
                var result = FbxBinaryConverter.ConvertToGlb(filePath, glbPath);
                if (result != null) return result;
            }
            catch { }

            try { if (File.Exists(glbPath)) File.Delete(glbPath); } catch { }
            return null;
        }
    }
}
