using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Meshes;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary>
/// <c>mtk edit input output operation...</c>: edits a model as the application does, by its commands, one operation
/// after the other, and writes it: a pipeline without the user interface.
/// </summary>
internal static class EditCommand
{
    private const string Help = """
        Operations, in order; PATH names an entity as `mtk tree` shows it ("." for the model), lengths in mm:
          import=FILE[@X,Y,Z]      bring a model in as a component, placed at a point
          move=PATH@DX,DY,DZ       move an entity
          rotate=PATH@DEGREES      turn an entity around the vertical through the middle of its bottom
          rename=PATH@NAME         rename an entity
          hide=PATH  show=PATH     hide or show an entity
          delete=PATH              delete an entity
          simplify=PATH@PRESET     simplify the meshes of an entity (presets: clean-up, fewer-lines,
                                   furniture-at-room-scale, background-object)
        """;

    public static Command Create(IServiceProvider services)
    {
        Argument<FileInfo> input = new Argument<FileInfo>("input") { Description = "The model to edit." }.AcceptExistingOnly();
        var output = new Argument<FileInfo>("output") { Description = "The file to write; its extension chooses the format." };
        var operations = new Argument<string[]>("operations") { Description = Help, Arity = ArgumentArity.OneOrMore };
        var command = new Command("edit", "Edit a model by a sequence of operations, as the application does.")
        {
            input, output, operations,
        };
        command.SetAction(parseResult => CommandErrors.Run(parseResult, console =>
        {
            FileInfo inputFile = parseResult.GetRequiredValue(input);
            FileInfo outputFile = parseResult.GetRequiredValue(output);
            var formats = services.GetRequiredService<DocumentFormats>();
            IDocumentWriter writer = formats.WriterFor(outputFile.FullName);
            var warnings = new List<string>();
            Document document = formats.ReaderFor(inputFile.FullName).Read(inputFile.FullName, warnings);
            var session = new EditSession(document);
            foreach (string operation in parseResult.GetRequiredValue(operations))
            {
                console.WriteLine(Run(operation, document, session, formats, warnings));
            }
            CommandErrors.WriteWarnings(parseResult, warnings);
            writer.Write(document, outputFile.FullName);
            console.WriteLine($"Wrote {outputFile.Name}.");
            return 0;
        }));
        return command;
    }

    private static string Run(string operation, Document document, EditSession session, DocumentFormats formats, List<string> warnings)
    {
        int equals = operation.IndexOf('=', StringComparison.Ordinal);
        string verb = (equals < 0 ? operation : operation[..equals]).Trim().ToLowerInvariant();
        string rest = equals < 0 ? string.Empty : operation[(equals + 1)..];
        int at = rest.LastIndexOf('@');
        string target = at < 0 ? rest : rest[..at];
        string? value = at < 0 ? null : rest[(at + 1)..];

        if (verb == "import")
        {
            var imported = formats.ReaderFor(Path.GetFullPath(target)).Read(Path.GetFullPath(target), warnings);
            var import = new ImportCommand(document, ModelPaths.ModelBlock(document), imported, Path.GetFileNameWithoutExtension(target));
            session.Execute(import);
            if (value is not null)
            {
                Point3d point = Point(value);
                session.Execute(new SetTransformCommand("Move", import.Reference,
                    Transform.Translation(point.X, point.Y, point.Z) * import.Reference.Transform));
            }
            return $"Imported {target} as {import.Reference.Block.Name}.";
        }

        IReadOnlyList<Entity> path = ModelPaths.Find(document, target)
            ?? throw new ArgumentException($"Nothing is at \"{target}\"; `mtk tree` shows the paths.");
        if (verb == "simplify")
        {
            SimplifyPreset preset = SimplifyPreset.Named(value ?? "clean-up")
                ?? throw new ArgumentException($"There is no preset \"{value}\".");
            Entity whole = path.Count == 0 || path[^1] is BlockReference { } top && ReferenceEquals(top.Block, ModelPaths.ModelBlock(document)) && target.Trim() == "."
                ? new BlockReference(ModelPaths.ModelBlock(document), Transform.Identity)
                : path[^1];
            SimplifiedComponent result = SimplifiedComponent.Of(whole, preset.Options);
            session.Execute(result.Command());
            return string.Create(CultureInfo.InvariantCulture,
                $"Simplified {target}: {result.Before.Triangles:N0} → {result.After.Triangles:N0} triangles, edges drawn {result.Before.Edges:N0} → {result.After.Edges:N0}, moved at most {result.MaxDeviation:0.0} mm.");
        }
        if (target.Trim() == ".")
        {
            throw new ArgumentException($"The model itself cannot be {verb}d.");
        }
        Entity entity = path[^1];
        Transform placement = InferenceEngine.PlacementOf(path.Take(path.Count - 1).ToList());
        switch (verb)
        {
            case "move":
            {
                Point3d by = Point(value ?? throw new ArgumentException("move needs @DX,DY,DZ."));
                session.Execute(TransformCommands.Of("Move", entity,
                    placement.Inverse() * Transform.Translation(by.X, by.Y, by.Z) * placement));
                return $"Moved {target}.";
            }
            case "rotate":
            {
                double degrees = double.Parse(value ?? throw new ArgumentException("rotate needs @DEGREES."), CultureInfo.InvariantCulture);
                var handles = new SelectionHandles(entity, placement);
                // Around the vertical through the middle of the bottom of its box.
                Point3d bottom = handles.IsEmpty ? Point3d.Origin : Point3d.Origin + handles.Corners.Take(4)
                    .Aggregate(default(Vector3d), (sum, corner) => sum + (corner - Point3d.Origin)) / 4;
                Vector3d up = placement.ApplyVector(Vector3d.UnitZ).Normalized();
                session.Execute(TransformCommands.Of("Rotate", entity,
                    placement.Inverse() * Transform.Rotation(bottom, up, degrees * Math.PI / 180) * placement));
                return $"Rotated {target}.";
            }
            case "rename":
                session.Execute(new RenameCommand(entity, value ?? throw new ArgumentException("rename needs @NAME.")));
                return $"Renamed {target} to {value}.";
            case "hide" or "show":
                session.Execute(new SetHiddenCommand([entity], verb == "hide"));
                return $"{(verb == "hide" ? "Hid" : "Showed")} {target}.";
            case "delete":
                session.Execute(new DeleteCommand(entity));
                return $"Deleted {target}.";
            default:
                throw new ArgumentException($"There is no operation \"{verb}\".");
        }
    }

    private static Point3d Point(string text)
    {
        double[] values = [.. text.Split(',').Select(part => double.Parse(part, CultureInfo.InvariantCulture))];
        return values.Length == 3
            ? new Point3d(values[0], values[1], values[2])
            : throw new ArgumentException($"\"{text}\" is not X,Y,Z.");
    }
}
