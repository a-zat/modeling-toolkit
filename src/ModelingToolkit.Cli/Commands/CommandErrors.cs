using System.CommandLine;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Cli.Commands;

internal static class CommandErrors
{
    /// <summary>
    /// Runs the body of a command, reporting the expected failures (missing, unreadable or invalid files, unsupported
    /// formats, invalid dimensions) as one line on standard error with exit code 1, instead of an exception and its
    /// stack trace.
    /// </summary>
    public static int Run(ParseResult parseResult, Func<TextWriter, int> body)
    {
        try
        {
            return body(parseResult.InvocationConfiguration.Output);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or NotSupportedException
                                              or ArgumentException or GeometryKernelException)
        {
            parseResult.InvocationConfiguration.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    /// <summary>Reports what a reader left out, one line each on standard error.</summary>
    public static void WriteWarnings(ParseResult parseResult, IEnumerable<string> warnings)
    {
        foreach (string warning in warnings)
        {
            parseResult.InvocationConfiguration.Error.WriteLine($"Warning: {warning}");
        }
    }
}
