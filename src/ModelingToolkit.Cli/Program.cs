using Microsoft.Extensions.Hosting;
using ModelingToolkit.Cli;

using IHost host = CliHost.Build();
return CliHost.CreateRootCommand(host.Services).Parse(args).Invoke();
