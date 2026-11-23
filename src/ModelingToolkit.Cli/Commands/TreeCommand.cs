using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary><c>mtk tree file</c>: the structure of a model, as the outline shows it, with the paths that name its entities.</summary>
internal static class TreeCommand
{
    public static Command Create(IServiceProvider services)
    {
        Argument<FileInfo> input = new Argument<FileInfo>("file") { Description = "The model to show." }.AcceptExistingOnly();
        var depth = new Option<int>("--depth") { Description = "How deep to go into the blocks.", DefaultValueFactory = _ => 2 };
        var command = new Command("tree", "Show the structure of a model and the paths of its entities.") { input, depth };
        command.SetAction(parseResult => CommandErrors.Run(parseResult, console =>
        {
            FileInfo file = parseResult.GetRequiredValue(input);
            var formats = services.GetRequiredService<DocumentFormats>();
            var warnings = new List<string>();
            Document document = formats.ReaderFor(file.FullName).Read(file.FullName, warnings);
            CommandErrors.WriteWarnings(parseResult, warnings);
            int maxDepth = parseResult.GetValue(depth);
            CultureInfo culture = CultureInfo.InvariantCulture;
            // Where the origin of its block is in the model, as the moves of `mtk edit` give it.
            string Placed(BlockReference reference, Core.Geometry.Transform placement)
            {
                Core.Geometry.Point3d at = (placement * reference.Transform).Apply(Core.Geometry.Point3d.Origin);
                string follows = document.AttachmentOf(reference) is { IsActive: true } attachment
                    ? $", follows {attachment.Anchor.Name ?? "an anchor"}"
                    : string.Empty;
                return string.Create(culture, $"component of {reference.Block.Name}, at {Math.Round(at.X, 1) + 0.0:0.#}, {Math.Round(at.Y, 1) + 0.0:0.#}, {Math.Round(at.Z, 1) + 0.0:0.#}{follows}");
            }

            // Where an anchor is in the model, and the way it faces.
            string Anchored(AnchorEntity anchor, Core.Geometry.Transform placement)
            {
                Core.Geometry.Transform frame = placement * anchor.Placement;
                Core.Geometry.Point3d at = frame.Apply(Core.Geometry.Point3d.Origin);
                Core.Geometry.Vector3d ahead = frame.ApplyVector(Core.Geometry.Vector3d.UnitX);
                double degrees = Math.Round(Math.Atan2(ahead.Y, ahead.X) * 180 / Math.PI, 1) + 0.0;
                return string.Create(culture,
                    $"{(anchor.Kind == AnchorKind.PointOfView ? "point of view" : "anchor")}, at {Math.Round(at.X, 1) + 0.0:0.#}, {Math.Round(at.Y, 1) + 0.0:0.#}, {Math.Round(at.Z, 1) + 0.0:0.#}, facing {degrees:0.#}°");
            }

            void Write(Block block, string prefix, int level, Core.Geometry.Transform placement)
            {
                foreach (Entity entity in block.Entities)
                {
                    string path = prefix.Length == 0 ? ModelPaths.StepOf(entity) : $"{prefix}/{ModelPaths.StepOf(entity)}";
                    string kind = entity switch
                    {
                        BlockReference reference => Placed(reference, placement),
                        MeshEntity mesh => string.Create(culture, $"mesh, {mesh.Triangles.Length:N0} triangles"),
                        BrepEntity => "shape",
                        AnchorEntity anchor => Anchored(anchor, placement),
                        _ => entity.GetType().Name,
                    };
                    string layer = entity.Layer is { } on ? $", on {on.Name}" : "";
                    console.WriteLine($"{new string(' ', 2 * level)}{path}  ({kind}{layer}{(entity.IsHidden ? ", hidden" : "")})");
                    if (entity is BlockReference inner && level + 1 < maxDepth)
                    {
                        Write(inner.Block, path, level + 1, placement * inner.Transform);
                    }
                }
            }
            Write(document.Root, string.Empty, 0, Core.Geometry.Transform.Identity);
            return 0;
        }));
        return command;
    }
}
