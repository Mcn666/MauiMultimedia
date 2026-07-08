using System.Reflection;
using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.Shell.Services;

/// <summary>
/// 自动扫描并注册所有 IFileViewer、IItemPresenter 与 ISnapshotProvider 实现。
/// 构建时由 Shell.csproj 的 GenerateViewerEmbed 目标把所有 ProjectReference 写入
/// viewer_assemblies.txt 并嵌入为资源；运行时强制加载其中列出的程序集，再扫描这些程序集
/// （外加宿主自身 Shell）以发现接口实现。
/// 不依赖任何命名空间约定——新增/移除查看器库只需在 .csproj 增删对应的 ProjectReference，
/// 无需任何手工注册代码，也不会因重命名命名空间而静默失效。
/// 只扫描“确定包含查看器”的程序集，避免对框架/MAUI 等数百个程序集物化反射元数据
/// （既拖慢启动，又徒增常驻内存）。
/// </summary>
public static class ViewerAutoRegistration
{
    public static IServiceCollection AutoRegisterViewers(this IServiceCollection services)
    {
        var fileViewerType = typeof(IFileViewer);
        var presenterType = typeof(IItemPresenter);
        var snapshotType = typeof(ISnapshotProvider);

        // 要扫描的程序集集合：先加入宿主 Shell 自身，再强制加载清单列出的查看器程序集。
        // 清单由 csproj 自动从所有 ProjectReference 生成，已精确覆盖全部查看器，
        // 因此无需遍历整个 AppDomain（那样会对数百个框架程序集物化反射元数据）。
        var assembliesToScan = new HashSet<Assembly>();
        var shellAsm = typeof(ViewerAutoRegistration).Assembly;
        assembliesToScan.Add(shellAsm);

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
                    if (name.Length == 0) continue;
                    try
                    {
                        // 强制加载清单程序集，保证其进入 AppDomain 并被下方扫描发现。
                        var asm = Assembly.Load(new AssemblyName(name));
                        assembliesToScan.Add(asm);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 仅扫描已确定的程序集集合（宿主 + 清单查看器），不碰框架程序集。
        foreach (var asm in assembliesToScan)
        {
            if (asm.IsDynamic) continue;

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
