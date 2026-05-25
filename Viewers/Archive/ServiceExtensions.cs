using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.Viewers.Archive;

public static class ServiceExtensions
{
    public static IServiceCollection AddArchiveViewer(this IServiceCollection services)
    {
        services.AddSingleton<IViewProvider, ArchiveProvider>();
        services.AddSingleton<IFileViewer, ArchiveViewer>();
        return services;
    }
}
