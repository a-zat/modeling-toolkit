using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelingToolkit.App.ViewModels;
using ModelingToolkit.Core.IO;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.App;

/// <summary>Composition of the desktop application, shared by <c>Program</c> and the tests.</summary>
internal static class AppHost
{
    public static IHost Build()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Services.AddOpenCascade().AddNativeFileFormat().AddSingleton<DocumentFormats>();
        builder.Services.AddSingleton<Appearance>();
        builder.Services.AddSingleton<IAppearance>(services => services.GetRequiredService<Appearance>());
        builder.Services.AddTransient<MainWindowViewModel>();

        return builder.Build();
    }
}
