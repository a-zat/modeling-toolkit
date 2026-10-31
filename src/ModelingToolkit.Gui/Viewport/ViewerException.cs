namespace ModelingToolkit.Gui.Viewport;

/// <summary>The native viewer failed: OpenCASCADE raised an error, or the view could not be shown in its window.</summary>
public sealed class ViewerException(string message) : Exception(message);
