using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.TextViewer;

public static class ServiceExtensions
{
    public static IServiceCollection AddTextViewer(this IServiceCollection services)
    {
        services.AddSingleton<IViewProvider, TextViewerProvider>();
        services.AddSingleton<IFileViewer, TextViewer>();
        return services;
    }
}
