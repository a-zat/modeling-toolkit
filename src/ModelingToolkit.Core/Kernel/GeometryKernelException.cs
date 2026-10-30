namespace ModelingToolkit.Core.Kernel;

/// <summary>Thrown when a geometry kernel fails to perform an operation.</summary>
public class GeometryKernelException : Exception
{
    public GeometryKernelException()
    {
    }

    public GeometryKernelException(string message)
        : base(message)
    {
    }

    public GeometryKernelException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
