namespace ModelingToolkit.Core.Editing;

/// <summary>A command refused its change, such as one whose result is not a valid shape; nothing was changed.</summary>
public sealed class EditRefusedException : Exception
{
    public EditRefusedException()
    {
    }

    public EditRefusedException(string message)
        : base(message)
    {
    }

    public EditRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
