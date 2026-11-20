using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Meshes;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary><c>mtk simplify input output</c>: simplifies the meshes of a model and writes it.</summary>
internal static class SimplifyCommand
{
    public static Command Create(IServiceProvider services)
    {
        Argument<FileInfo> input = new Argument<FileInfo>("input") { Description = "The model to simplify." }
            .AcceptExistingOnly();
        var output = new Argument<FileInfo>("output") { Description = "The file to write; its extension chooses the format." };
        var weld = new Option<double>("--weld") { Description = "Weld vertices closer than this, in mm.", DefaultValueFactory = _ => 0.01 };
        var pieces = new Option<double>("--min-piece") { Description = "Remove pieces smaller across than this, in mm." };
        var smooth = new Option<int>("--smooth") { Description = "Passes of smoothing." };
        var smoothMove = new Option<double>("--smooth-max-move") { Description = "How far smoothing may move a vertex, in mm.", DefaultValueFactory = _ => 5 };
        var smoothKeep = new Option<double>("--smooth-keep-angle") { Description = "Smoothing keeps edges sharper than this, in degrees." };
        var keep = new Option<double>("--keep") { Description = "The percentage of the triangles to keep.", DefaultValueFactory = _ => 100 };
        var error = new Option<double>("--max-error") { Description = "The furthest the reduction may move the surface, in mm; 0 for no limit.", DefaultValueFactory = _ => 2 };
        var recompute = new Option<double?>("--crease") { Description = "Recompute the normals, smooth below this angle in degrees." };
        var soften = new Option<double>("--soften") { Description = "Make one the normals of a vertex closer than this angle, in degrees." };
        var seams = new Option<bool>("--free-seams") { Description = "Let the reduction move the seams of the texture." };
        var merge = new Option<bool>("--merge") { Description = "Merge the meshes of a block with the same material." };

        var command = new Command("simplify", "Simplify the meshes of a model.")
        {
            input, output, weld, pieces, smooth, smoothMove, smoothKeep, keep, error, recompute, soften, seams, merge,
        };
        command.SetAction(parseResult => CommandErrors.Run(parseResult, console =>
        {
            FileInfo inputFile = parseResult.GetRequiredValue(input);
            FileInfo outputFile = parseResult.GetRequiredValue(output);
            var formats = services.GetRequiredService<DocumentFormats>();
            IDocumentReader reader = formats.ReaderFor(inputFile.FullName);
            IDocumentWriter writer = formats.WriterFor(outputFile.FullName);
            var warnings = new List<string>();
            Document document = reader.Read(inputFile.FullName, warnings);
            // As the application opens it: the node of a glTF file holding the whole model is the model, so that
            // writing it back does not wrap it once more.
            ModelPaths.UnwrapGltf(document, inputFile.FullName);
            CommandErrors.WriteWarnings(parseResult, warnings);

            double? crease = parseResult.GetValue(recompute);
            var options = new SimplifyOptions
            {
                WeldTolerance = parseResult.GetValue(weld),
                MinPieceSize = parseResult.GetValue(pieces),
                SmoothIterations = parseResult.GetValue(smooth),
                SmoothMaxMove = parseResult.GetValue(smoothMove),
                SmoothKeepAngle = parseResult.GetValue(smoothKeep),
                KeepPercent = parseResult.GetValue(keep),
                MaxError = parseResult.GetValue(error),
                Normals = crease is null ? NormalsMode.Keep : NormalsMode.Recompute,
                CreaseAngle = crease ?? 40,
                MergeByMaterial = parseResult.GetValue(merge),
                SoftenAngle = parseResult.GetValue(soften),
                KeepTextureSeams = !parseResult.GetValue(seams),
            };
            // The whole model, as the component of a reference to its root block.
            var whole = new BlockReference(document.Root, Core.Geometry.Transform.Identity);
            SimplifiedComponent result = SimplifiedComponent.Of(whole, options);
            new EditSession(document).Execute(result.Command());
            writer.Write(document, outputFile.FullName);

            CultureInfo culture = CultureInfo.InvariantCulture;
            console.WriteLine(string.Create(culture,
                $"Triangles {result.Before.Triangles} → {result.After.Triangles}, vertices {result.Before.Vertices} → {result.After.Vertices}, " +
                $"pieces {result.Before.Pieces} → {result.After.Pieces}, edges drawn {result.Before.Edges} → {result.After.Edges}, " +
                $"deviation max {result.MaxDeviation:0.0} mm, mean {result.MeanDeviation:0.00} mm."));
            return 0;
        }));
        return command;
    }
}
