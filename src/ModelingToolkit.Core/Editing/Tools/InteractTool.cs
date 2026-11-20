using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Works what can be worked in the model, as a person would: a click on a door opens it, or closes it if open. What can
/// be worked, and how, is given: <paramref name="canWork"/> tells whether something can be worked at a path, from the
/// model, and <paramref name="work"/> works it, telling whether it did; both are given the path and the point clicked.
/// </summary>
public sealed class InteractTool(Func<IReadOnlyList<Entity>?, Point3d, bool> canWork, Func<IReadOnlyList<Entity>?, Point3d, bool> work) : Tool
{
    private const string StartHint = "Click what opens or closes, such as a door, to work it";
    private const string OverHint = "Click to open or close it";

    private readonly Func<IReadOnlyList<Entity>?, Point3d, bool> _canWork = canWork ?? throw new ArgumentNullException(nameof(canWork));
    private readonly Func<IReadOnlyList<Entity>?, Point3d, bool> _work = work ?? throw new ArgumentNullException(nameof(work));

    public override string Name => "Interact";

    protected override void OnActivated() => Hint = StartHint;

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Hint = _canWork(input.Pick.Path, input.Pick.Point) ? OverHint : StartHint;
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Pick.Path is not null && !_work(input.Pick.Path, input.Pick.Point))
        {
            Host.Report("Nothing there can be used.");
        }
    }
}
