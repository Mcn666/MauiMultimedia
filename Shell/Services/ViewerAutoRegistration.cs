using System.Reflection;
using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 自动扫描并注册所有 IFileViewer 和 IViewProvider 实现。
/// 构建时自动嵌入查看器程序集清单，运行时直接从嵌入资源读取。
/// 新增查看器库只需在 .csproj 添加 ProjectReference。
/// </summary>
public static class ViewerAutoRegistration
{
    public static IServiceCollection AutoRegisterViewers(this IServiceCollection services)
    {
        var fileViewerType = typeof(IFileViewer);
        var viewProviderType = typeof(IViewProvider);
        const string viewerPrefix = "MauiMultimedia.Viewers.";

        // 从嵌入资源读取查看器程序集名称并加载
        var shellAsm = typeof(ViewerAutoRegistration).Assembly;
        try
        {
            using var stream = shellAsm.GetManifestResourceStream("viewer_assemblies.txt");
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var name = line.Trim();
                    if (name.Length > 0 && name.StartsWith(viewerPrefix))
                    {
                        try { Assembly.Load(new AssemblyName(name)); } catch { }
                    }
                }
            }
        }
        catch { }

        // 注册已加载查看器中的类型
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            var n = asm.GetName().Name;
            if (n == null || !n.StartsWith(viewerPrefix)) continue;
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (fileViewerType.IsAssignableFrom(type))
                    services.AddSingleton(fileViewerType, type);
                if (viewProviderType.IsAssignableFrom(type))
                    services.AddSingleton(viewProviderType, type);
            }
        }

        return services;
    }
}
