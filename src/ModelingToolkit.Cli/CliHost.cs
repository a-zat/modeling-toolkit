using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Cli.Commands;
using ModelingToolkit.Core.IO;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Cli;

/// <summary>Composition of the command line tool, shared by <c>Program</c> and the tests.</summary>
internal static class CliHost
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddOpenCascade().AddNativeFileFormat().AddSingleton<DocumentFormats>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public static RootCommand CreateRootCommand(IServiceProvider services)
    {
        var rootCommand = new RootCommand("Modeling Toolkit: import, process and export 3D models.");
        rootCommand.Subcommands.Add(InfoCommand.Create(services));
        rootCommand.Subcommands.Add(ConvertCommand.Create(services));
        rootCommand.Subcommands.Add(StatsCommand.Create(services));
        rootCommand.Subcommands.Add(MakeCommand.Create(services));
        rootCommand.Subcommands.Add(CombineCommand.Create(services));
        rootCommand.Subcommands.Add(SimplifyCommand.Create(services));
        return rootCommand;
    }
}
