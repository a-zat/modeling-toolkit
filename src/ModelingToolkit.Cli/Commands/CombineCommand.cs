using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary>
/// <c>mtk combine operation first second output</c>: a boolean operation on the shapes of two models, each made of a
/// single B-rep shape, such as those of <c>mtk make</c>, written as a model.
/// </summary>
internal static class CombineCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("combine", "Combine the shapes of two models with a boolean operation.");
        command.Subcommands.Add(Operation(services, "union", "What is in either shape.", "first", "second",
            (kernel, first, second) => kernel.Union(first, second)));
        command.Subcommands.Add(Operation(services, "subtract", "What is in the shape and not in the tool.", "shape",
            "tool", (kernel, shape, tool) => kernel.Subtract(shape, tool)));
        command.Subcommands.Add(Operation(services, "intersect", "What is in both shapes.", "first", "second",
            (kernel, first, second) => kernel.Intersect(first, second)));
        return command;
    }

    private static Command Operation(
        IServiceProvider services,
        string name,
        string description,
        string firstName,
        string secondName,
        Func<IGeometryKernel, IKernelShape, IKernelShape, IKernelShape> combine)
    {
        Argument<FileInfo> first = new Argument<FileInfo>(firstName) { Description = $"The model of the {firstName}." }
            .AcceptExistingOnly();
        Argument<FileInfo> second = new Argument<FileInfo>(secondName)
        {
            Description = $"The model of the {secondName}.",
        }.AcceptExistingOnly();
        var output = new Argument<FileInfo>("output")
        {
            Description = "The file to write; its extension chooses the format.",
        };

        var command = new Command(name, description) { first, second, output };
        command.SetAction(parseResult => CommandErrors.Run(parseResult, console =>
        {
            FileInfo outputFile = parseResult.GetRequiredValue(output);
            var formats = services.GetRequiredService<DocumentFormats>();
            IDocumentWriter writer = formats.WriterFor(outputFile.FullName); // checked before the work starts
            var kernel = services.GetRequiredService<IGeometryKernel>();

            BrepEntity a = ShapeOf(services, parseResult, parseResult.GetRequiredValue(first));
            BrepEntity b = ShapeOf(services, parseResult, parseResult.GetRequiredValue(second));
            using IKernelShape result = combine(kernel, a.Shape, b.Shape);
            MakeCommand.Write(services, result, char.ToUpperInvariant(name[0]) + name[1..], outputFile);

            console.WriteLine($"Combined {parseResult.GetRequiredValue(first).Name} and " +
                $"{parseResult.GetRequiredValue(second).Name} in {outputFile.Name} ({writer.FormatName}).");
            return 0;
        }));
        return command;
    }

    private static BrepEntity ShapeOf(IServiceProvider services, ParseResult parseResult, FileInfo file)
    {
        var warnings = new List<string>();
        Document document = services.GetRequiredService<DocumentFormats>()
            .ReaderFor(file.FullName).Read(file.FullName, warnings);
        CommandErrors.WriteWarnings(parseResult, warnings);

        return document.Root.Entities.Count == 1 && document.Root.Entities[0] is BrepEntity shape
            ? shape
            : throw new InvalidDataException($"'{file.Name}' must hold a single B-rep shape, as made by 'mtk make'.");
    }
}
