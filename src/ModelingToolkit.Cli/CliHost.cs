using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelingToolkit.Cli.Commands;
using ModelingToolkit.Core.IO;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Cli;

/// <summary>Composition of the command line tool, shared by <c>Program</c> and the tests.</summary>
internal static class CliHost
{
    public static IHost Build()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Standard output is reserved for command results: diagnostics go to standard error, one line each.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

        builder.Services.AddOpenCascade().AddNativeFileFormat().AddSingleton<DocumentFormats>();

        return builder.Build();
    }

    public static RootCommand CreateRootCommand(IServiceProvider services)
    {
        var rootCommand = new RootCommand("Modeling Toolkit: import, process and export 3D models.");
        rootCommand.Subcommands.Add(InfoCommand.Create(services));
        rootCommand.Subcommands.Add(ConvertCommand.Create(services));
        rootCommand.Subcommands.Add(StatsCommand.Create(services));
        return rootCommand;
    }
}
