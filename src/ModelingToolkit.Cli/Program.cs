using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Cli;

using ServiceProvider services = CliHost.Build();
return CliHost.CreateRootCommand(services).Parse(args).Invoke();
