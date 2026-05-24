using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.Viewers.Image;

public static class ServiceExtensions
{
    public static IServiceCollection AddImageViewer(this IServiceCollection services)
    {
        services.AddSingleton<IViewProvider, ImageProvider>();
        services.AddSingleton<IFileViewer, ImageViewer>();
        return services;
    }
}
