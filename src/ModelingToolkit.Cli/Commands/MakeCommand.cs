using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary>
/// <c>mtk make shape dimensions... output</c>: builds a primitive of exact geometry and writes it as a model, which
/// the application shows and <c>mtk combine</c> takes as an operand.
/// </summary>
internal static class MakeCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("make", "Build a shape of exact geometry and save it as a model.");
        command.Subcommands.Add(Shape(services, "box", "A box filling [0, dx] × [0, dy] × [0, dz].",
            ["dx", "dy", "dz"], (kernel, size) => kernel.MakeBox(size[0], size[1], size[2])));
        command.Subcommands.Add(Shape(services, "cylinder", "A cylinder around the Z axis, from z = 0 to z = height.",
            ["radius", "height"], (kernel, size) => kernel.MakeCylinder(size[0], size[1])));
        command.Subcommands.Add(Shape(services, "sphere", "A sphere centred at the origin.", ["radius"],
            (kernel, size) => kernel.MakeSphere(size[0])));
        command.Subcommands.Add(Shape(services, "cone",
            "A cone around the Z axis, from z = 0 to z = height, with a radius at each end that may be zero.",
            ["bottom-radius", "top-radius", "height"], (kernel, size) => kernel.MakeCone(size[0], size[1], size[2])));
        command.Subcommands.Add(Shape(services, "torus", "A torus centred at the origin, around the Z axis.",
            ["major-radius", "minor-radius"], (kernel, size) => kernel.MakeTorus(size[0], size[1])));
        return command;
    }

    /// <summary>Writes a document that holds one shape.</summary>
    internal static void Write(IServiceProvider services, IKernelShape shape, string name, FileInfo output)
    {
        var document = new Document();
        var kernel = services.GetRequiredService<IGeometryKernel>();
        document.Root.Entities.Add(new BrepEntity(kernel, shape) { Name = name });
        services.GetRequiredService<DocumentFormats>().WriterFor(output.FullName).Write(document, output.FullName);
    }

    private static Command Shape(
        IServiceProvider services,
        string name,
        string description,
        string[] dimensions,
        Func<IGeometryKernel, double[], IKernelShape> make)
    {
        Argument<double>[] sizes = [.. dimensions.Select(dimension =>
            new Argument<double>(dimension) { Description = $"The {dimension.Replace('-', ' ')}, in millimetres." })];
        var output = new Argument<FileInfo>("output")
        {
            Description = "The file to write; its extension chooses the format.",
        };
        var at = new Option<string>("--at")
        {
            Description = "Where to put the shape, in millimetres: x,y,z.",
            DefaultValueFactory = _ => "0,0,0",
        };

        var command = new Command(name, description);
        foreach (Argument<double> size in sizes)
        {
            command.Arguments.Add(size);
        }
        command.Arguments.Add(output);
        command.Options.Add(at);

        command.SetAction(parseResult => CommandErrors.Run(parseResult, console =>
        {
            FileInfo outputFile = parseResult.GetRequiredValue(output);
            var formats = services.GetRequiredService<DocumentFormats>();
            IDocumentWriter writer = formats.WriterFor(outputFile.FullName); // checked before the work starts
            Transform placement = Placement(parseResult.GetRequiredValue(at));
            var kernel = services.GetRequiredService<IGeometryKernel>();

            using IKernelShape made = make(kernel, [.. sizes.Select(size => parseResult.GetRequiredValue(size))]);
            using IKernelShape placed = placement.IsIdentity ? made : kernel.Place(made, placement);
            Write(services, placed, char.ToUpperInvariant(name[0]) + name[1..], outputFile);

            console.WriteLine($"Made a {name} in {outputFile.Name} ({writer.FormatName}).");
            return 0;
        }));
        return command;
    }

    private static Transform Placement(string text)
    {
        string[] parts = text.Split(',');
        double[] values = new double[3];
        if (parts.Length != 3 || parts.Where((part, index) => !double.TryParse(
                part, NumberStyles.Float, CultureInfo.InvariantCulture, out values[index])).Any())
        {
            throw new ArgumentException(
                $"--at takes three numbers separated by commas, such as 50,50,-10, not '{text}'.");
        }
        return Transform.Translation(values[0], values[1], values[2]);
    }
}
