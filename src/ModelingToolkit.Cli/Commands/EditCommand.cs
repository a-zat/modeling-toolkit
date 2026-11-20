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
          anchor=PATH@NAME,X,Y,Z[,DEGREES][,local]
                                   add an anchor to a component, at a point of the model, facing a direction
                                   (degrees from X around the vertical); with local, in the coordinates of its
                                   block, as for a part scaled to nothing until an animation shows it
          view=PATH@NAME,X,Y,Z[,DEGREES][,local]
                                   add a point of view to a component: the eyes at the point, looking that way
          attach=PATH@ANCHOR       make a component follow an anchor, the path of the anchor beginning where the
                                   component is; detach=PATH stops it
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
            // As the application opens it: the node of a glTF file holding the whole model is the model, so that
            // writing it back does not wrap it once more.
            ModelPaths.UnwrapGltf(document, inputFile.FullName);
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
                // At the point of the model, whatever wraps the block of the model.
                Point3d point = Point(value);
                Transform wrapping = InferenceEngine.PlacementOf(ModelPaths.Wrappers(document));
                session.Execute(new SetTransformCommand("Move", import.Reference,
                    wrapping.Inverse() * Transform.Translation(point.X, point.Y, point.Z) * import.Reference.Transform));
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
                session.Execute(new RenameCommand(entity, value ?? throw new ArgumentException("rename needs @NAME."), document));
                return $"Renamed {target} to {value}.";
            case "hide" or "show":
                session.Execute(new SetHiddenCommand([entity], verb == "hide"));
                return $"{(verb == "hide" ? "Hid" : "Showed")} {target}.";
            case "delete":
                session.Execute(new DeleteCommand(entity));
                return $"Deleted {target}.";
            case "anchor" or "view":
            {
                if (entity is not BlockReference component)
                {
                    throw new ArgumentException($"{target} is not a component: only components have anchors.");
                }
                string[] parts = (value ?? throw new ArgumentException($"{verb} needs @NAME,X,Y,Z.")).Split(',');
                bool local = parts[^1].Trim() == "local";
                if (local)
                {
                    parts = parts[..^1];
                }
                if (parts.Length is not (4 or 5) || string.IsNullOrWhiteSpace(parts[0]))
                {
                    throw new ArgumentException($"\"{value}\" is not NAME,X,Y,Z[,DEGREES][,local].");
                }
                Point3d point = Point(string.Join(',', parts[1..4]));
                double degrees = parts.Length == 5 ? double.Parse(parts[4], CultureInfo.InvariantCulture) : 0;
                // In the coordinates of the block of the component, Z up, X the way given.
                Transform toBlock = local ? Transform.Identity : (placement * component.Transform).Inverse();
                double angle = degrees * Math.PI / 180;
                Vector3d x = toBlock.ApplyVector(new Vector3d(Math.Cos(angle), Math.Sin(angle), 0)).Normalized();
                Vector3d z = toBlock.ApplyVector(Vector3d.UnitZ);
                z = (z - x * z.Dot(x)).Normalized();
                Vector3d y = z.Cross(x);
                Point3d o = toBlock.Apply(point);
                var frame = new Transform(x.X, y.X, z.X, o.X, x.Y, y.Y, z.Y, o.Y, x.Z, y.Z, z.Z, o.Z);
                var anchor = new AnchorEntity(frame, verb == "view" ? AnchorKind.PointOfView : AnchorKind.Attach)
                {
                    Name = parts[0].Trim(),
                };
                session.Execute(new AddEntityCommand(verb == "view" ? "Add point of view" : "Add anchor", component.Block, anchor));
                return $"Added {(verb == "view" ? "the point of view" : "the anchor")} {anchor.Name} to {target}.";
            }
            case "attach":
            {
                if (entity is not BlockReference follower)
                {
                    throw new ArgumentException($"{target} is not a component: only components follow anchors.");
                }
                string anchorPath = value ?? throw new ArgumentException("attach needs @ANCHOR.");
                IReadOnlyList<Entity> reached = ModelPaths.Find(document, anchorPath)
                    ?? throw new ArgumentException($"Nothing is at \"{anchorPath}\".");
                if (reached is not [.., AnchorEntity anchor])
                {
                    throw new ArgumentException($"{anchorPath} is not an anchor.");
                }
                int shared = path.Count - 1;
                if (reached.Count <= shared || !path.Take(shared).SequenceEqual(reached.Take(shared)))
                {
                    throw new ArgumentException($"{anchorPath} is not in the block where {target} is.");
                }
                BlockReference[] chain = [.. reached.Skip(shared).Take(reached.Count - shared - 1).Cast<BlockReference>()];
                var attachment = new Attachment(follower, chain, anchor, Attachment.OffsetOf(follower, chain, anchor));
                session.Execute(new AttachCommand("Attach", document, attachment, attach: true));
                return $"{target} follows {anchorPath}.";
            }
            case "detach":
            {
                if (entity is not BlockReference follower || document.AttachmentOf(follower) is not { } attachment)
                {
                    throw new ArgumentException($"{target} follows no anchor.");
                }
                session.Execute(new AttachCommand("Detach", document, attachment, attach: false));
                return $"{target} no longer follows an anchor.";
            }
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
