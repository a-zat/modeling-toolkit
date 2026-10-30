using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelingToolkit.Cli.Commands;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Kernel.Occt;

HostApplicationBuilder builder = Host.CreateApplicationBuilder();

// Standard output is reserved for command results: diagnostics go to standard error.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<IGeometryKernel, OcctKernel>();

using IHost host = builder.Build();

var rootCommand = new RootCommand("Modeling Toolkit: import, process and export 3D models.");
rootCommand.Subcommands.Add(InfoCommand.Create(host.Services));

return rootCommand.Parse(args).Invoke();
