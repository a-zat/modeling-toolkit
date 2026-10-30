using System.CommandLine;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Cli.Commands;

/// <summary><c>mtk info</c>: prints the versions of the toolkit, of the .NET runtime and of the geometry kernel.</summary>
internal static class InfoCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("info", "Show version information about the toolkit and its geometry kernel.");
        command.SetAction(_ =>
        {
            KernelInfo kernel = services.GetRequiredService<IGeometryKernel>().Info;
            string version = typeof(InfoCommand).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

            Console.WriteLine($"Modeling Toolkit {version}");
            Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.RuntimeIdentifier})");
            Console.WriteLine($"Kernel:  {kernel.Name} {kernel.Version}");
            return 0;
        });
        return command;
    }
}
