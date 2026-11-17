using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Works what can be worked in the model, as a person would: a click on a door opens it, or closes it if open. What can
/// be worked, and how, is given: <paramref name="canWork"/> tells whether something can be worked at a path, from the
/// model, and <paramref name="work"/> works it, telling whether it did.
/// </summary>
public sealed class InteractTool(Func<IReadOnlyList<Entity>?, bool> canWork, Func<IReadOnlyList<Entity>?, bool> work) : Tool
{
    private const string StartHint = "Click what opens or closes, such as a door, to work it";
    private const string OverHint = "Click to open or close it";

    private readonly Func<IReadOnlyList<Entity>?, bool> _canWork = canWork ?? throw new ArgumentNullException(nameof(canWork));
    private readonly Func<IReadOnlyList<Entity>?, bool> _work = work ?? throw new ArgumentNullException(nameof(work));

    public override string Name => "Interact";

    protected override void OnActivated() => Hint = StartHint;

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Hint = _canWork(input.Pick.Path) ? OverHint : StartHint;
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Pick.Path is not null && !_work(input.Pick.Path))
        {
            Host.Report("Nothing there opens or closes.");
        }
    }
}
