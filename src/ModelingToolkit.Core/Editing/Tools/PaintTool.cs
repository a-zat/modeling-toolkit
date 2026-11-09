using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Paints with a material, the default one for null: a click paints the face of a shape of the block being edited, or
/// with Shift all the faces of the shape, or a mesh. A material that the document does not have yet is added to it in
/// the same command.
/// </summary>
public sealed class PaintTool : Tool
{
    public override string Name => "Paint";

    /// <summary>The material to paint with; null for the default one, which takes the paint away.</summary>
    public Material? Material
    {
        get;
        set
        {
            if (!ReferenceEquals(field, value))
            {
                field = value;
                MaterialChanged?.Invoke(this, EventArgs.Empty);
                ShowHint();
            }
        }
    }

    public event EventHandler? MaterialChanged;

    protected override void OnActivated() => ShowHint();

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        IToolHost host = Host;
        if (input.Pick.Path is not { } path || host.Context.Within(path) is not { Count: > 0 } within)
        {
            return;
        }
        IEditCommand? paint = within[0] switch
        {
            BrepEntity shape when within.Count == 1 && input.Pick.Element is { Kind: ElementKind.Face } face =>
                new PaintFacesCommand(shape, input.Modifiers.HasFlag(ToolModifiers.Shift)
                    ? Enumerable.Range(0, shape.Kernel.CountElements(shape.Shape).Faces)
                    : [face.Index], Material),
            MeshEntity mesh when within.Count == 1 => new SetMeshMaterialCommand(mesh, Material),
            _ => null,
        };
        if (paint is null)
        {
            host.Report("Double-click a block to enter it and paint its faces.");
            return;
        }
        Document? document = host.Context.Block.Document;
        host.Session.Execute(Material is { } material && document is not null && !document.Materials.Contains(material)
            ? new CompositeCommand("Paint", [new AddMaterialCommand(document, material), paint])
            : paint);
    }

    private void ShowHint()
    {
        if (IsActive)
        {
            Hint = $"Click a face to paint it {(Material is { } material ? $"with {material.Name}" : "with the default material")} · Shift paints the whole shape";
        }
    }
}
