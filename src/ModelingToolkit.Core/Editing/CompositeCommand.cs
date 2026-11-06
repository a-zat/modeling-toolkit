namespace ModelingToolkit.Core.Editing;

/// <summary>Commands applied in order and reverted in the opposite order, as one.</summary>
public sealed class CompositeCommand : IEditCommand
{
    private readonly IEditCommand[] _commands;

    public CompositeCommand(string name, IEnumerable<IEditCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(commands);
        Name = name;
        _commands = [.. commands];
    }

    public string Name { get; }

    /// <summary>Applies the commands; if one refuses, those applied before it are reverted.</summary>
    public void Apply()
    {
        for (int index = 0; index < _commands.Length; index++)
        {
            try
            {
                _commands[index].Apply();
            }
            catch (EditRefusedException)
            {
                for (int applied = index - 1; applied >= 0; applied--)
                {
                    _commands[applied].Revert();
                }
                throw;
            }
        }
    }

    public void Revert()
    {
        for (int index = _commands.Length - 1; index >= 0; index--)
        {
            _commands[index].Revert();
        }
    }
}
