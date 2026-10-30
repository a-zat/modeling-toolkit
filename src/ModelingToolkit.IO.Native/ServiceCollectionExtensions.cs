using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.IO;

namespace ModelingToolkit.IO.Native;

/// <summary>Registration of the native file format, shared by the applications.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the reader and the writer of <c>.mtk</c> files.</summary>
    public static IServiceCollection AddNativeFileFormat(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentReader, NativeDocumentReader>();
        services.AddSingleton<IDocumentWriter, NativeDocumentWriter>();
        return services;
    }
}
