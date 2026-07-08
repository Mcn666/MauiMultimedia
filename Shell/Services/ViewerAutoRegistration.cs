using System.Reflection;
using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 自动扫描并注册所有 IFileViewer、IItemPresenter 与 ISnapshotProvider 实现。
/// 构建时由 Shell.csproj 的 GenerateViewerEmbed 目标把所有 ProjectReference 写入
/// viewer_assemblies.txt 并嵌入为资源；运行时强制加载其中列出的程序集，再扫描全部已加载
/// 程序集以发现接口实现。
/// 不依赖任何命名空间约定——新增/移除查看器库只需在 .csproj 增删对应的 ProjectReference，
/// 无需任何手工注册代码，也不会因重命名命名空间而静默失效。
/// </summary>
public static class ViewerAutoRegistration
{
    public static IServiceCollection AutoRegisterViewers(this IServiceCollection services)
    {
        var fileViewerType = typeof(IFileViewer);
        var presenterType = typeof(IItemPresenter);
        var snapshotType = typeof(ISnapshotProvider);

        // 从嵌入资源读取程序集名称并强制加载（保证查看器进入 AppDomain，供下方扫描发现）。
        // 清单由 Shell.csproj 自动从所有 ProjectReference 生成，因此“增删 ProjectReference = 增删登记”。
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
                    if (name.Length > 0)
                    {
                        try { Assembly.Load(new AssemblyName(name)); } catch { }
                    }
                }
            }
        }
        catch { }

        // 扫描所有已加载程序集，发现实现了查看器接口的具体类型（不依赖命名空间前缀）
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            var n = asm.GetName().Name;
            if (n == null) continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; } // 个别框架程序集可能拒绝反射，跳过不影响其他

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (fileViewerType.IsAssignableFrom(type))
                    services.AddSingleton(fileViewerType, type);
                if (presenterType.IsAssignableFrom(type) || snapshotType.IsAssignableFrom(type))
                {
                    // 同一实例同时服务 IItemPresenter 与 ISnapshotProvider，
                    // 避免快照事件/状态分散到不同实例。
                    services.AddSingleton(type);
                    if (presenterType.IsAssignableFrom(type))
                        services.AddSingleton(presenterType, sp => sp.GetRequiredService(type));
                    if (snapshotType.IsAssignableFrom(type))
                        services.AddSingleton(snapshotType, sp => sp.GetRequiredService(type));
                }
            }
        }

        return services;
    }
}
