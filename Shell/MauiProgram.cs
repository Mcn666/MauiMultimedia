using Microsoft.Extensions.Logging;
using MauiMultimedia.Core.Abstractions;
using MauiMultimedia.Shell.Services;

namespace MauiMultimedia.Shell
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
            builder.Services.AddSingleton<FileBrowserStateService>();
            builder.Services.AddSingleton<IFileNavigationState>(sp => sp.GetRequiredService<FileBrowserStateService>());
            builder.Services.AddSingleton<AliasService>();
            builder.Services.AutoRegisterViewers();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
