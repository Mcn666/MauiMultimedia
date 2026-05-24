using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.Viewers.Text;

public static class ServiceExtensions
{
    public static IServiceCollection AddTextViewer(this IServiceCollection services)
    {
        services.AddSingleton<IViewProvider, TextProvider>();
        services.AddSingleton<IFileViewer, TextViewer>();
        return services;
    }
}
