using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary><c>mtk stats file</c>: prints what a model contains and how large it is.</summary>
internal static class StatsCommand
{
    public static Command Create(IServiceProvider services)
    {
        Argument<FileInfo> file = new Argument<FileInfo>("file") { Description = "The model to inspect." }
            .AcceptExistingOnly();

        var command = new Command("stats", "Show the content and the size of a model.") { file };
        command.SetAction(parseResult => CommandErrors.Run(parseResult, console =>
        {
            FileInfo modelFile = parseResult.GetRequiredValue(file);
            IDocumentReader reader = services.GetRequiredService<DocumentFormats>().ReaderFor(modelFile.FullName);
            var warnings = new List<string>();
            DocumentStatistics statistics = DocumentStatistics.Of(reader.Read(modelFile.FullName, warnings));
            CommandErrors.WriteWarnings(parseResult, warnings);

            console.WriteLine($"{modelFile.Name} ({reader.FormatName})");
            Row(console, "Blocks", Count(statistics.Blocks));
            Row(console, "Block references", Count(statistics.BlockReferences));
            Row(console, "Meshes", $"{Count(statistics.Meshes)} ({Count(statistics.PlacedMeshes)} placed)");
            Row(console, "Vertices", Count(statistics.Vertices));
            Row(console, "Triangles", $"{Count(statistics.Triangles)} ({Count(statistics.PlacedTriangles)} placed)");
            if (statistics.BrepShapes > 0)
            {
                Row(console, "B-rep shapes",
                    $"{Count(statistics.BrepShapes)} ({Count(statistics.PlacedBrepShapes)} placed)");
            }
            Row(console, "Materials", Count(statistics.Materials));
            Row(console, "Textures", Count(statistics.Textures));
            BoundingBox box = statistics.BoundingBox;
            if (box.IsEmpty)
            {
                Row(console, "Bounds (mm)", "empty");
            }
            else
            {
                Row(console, "Bounds (mm)", $"{Point(box.Min)} to {Point(box.Max)}");
                Row(console, "Size (mm)",
                    $"{Length(box.Max.X - box.Min.X)} × {Length(box.Max.Y - box.Min.Y)} × {Length(box.Max.Z - box.Min.Z)}");
            }
            return 0;
        }));
        return command;
    }

    private static void Row(TextWriter console, string label, string value) => console.WriteLine($"  {label,-18}{value}");

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Length(double value) => value.ToString("N1", CultureInfo.InvariantCulture);

    private static string Point(Point3d point) => $"({Length(point.X)}, {Length(point.Y)}, {Length(point.Z)})";
}
