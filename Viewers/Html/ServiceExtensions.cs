using MauiMultimedia.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MauiMultimedia.Viewers.Html;

public static class ServiceExtensions
{
    public static IServiceCollection AddHtmlViewer(this IServiceCollection services)
    {
        services.AddSingleton<IViewProvider, HtmlProvider>();
        services.AddSingleton<IFileViewer, HtmlViewer>();
        return services;
    }
}
