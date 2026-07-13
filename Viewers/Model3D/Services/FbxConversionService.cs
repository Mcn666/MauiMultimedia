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

        // 必须传入应用私有目录内的缓存目录（由 IFileSystemService.GetScratchDirectory 提供），
        // 禁止退回系统 Temp，否则转换产物会逃逸到沙盒之外。
        if (string.IsNullOrEmpty(cacheDir))
            throw new ArgumentNullException(nameof(cacheDir),
                "转换 GLB 需要提供 cacheDir（应用私有目录内的临时目录），请勿传空或依赖系统 Temp。");
        var cacheKey = Path.GetFullPath(filePath).Replace(':', '_').Replace('\\', '_').Replace('/', '_');
        var workDir = Path.Combine(cacheDir, "MauiMM_ModelConvert");
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
