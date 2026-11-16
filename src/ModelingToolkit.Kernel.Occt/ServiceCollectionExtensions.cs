using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Kernel.Occt;

/// <summary>Registration of the OpenCASCADE services, shared by the applications.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the OpenCASCADE geometry kernel and the glTF reader and writer.</summary>
    public static IServiceCollection AddOpenCascade(this IServiceCollection services)
    {
        services.AddSingleton<IGeometryKernel, OcctKernel>();
        services.AddSingleton<IDocumentReader, GltfReader>();
        services.AddSingleton<IDocumentWriter, GltfWriter>();
        return services;
    }
}
