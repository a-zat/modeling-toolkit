using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>The camera of the 3D view, behind an interface so that view models stay testable.</summary>
public interface IViewCamera
{
    /// <summary>Shows the whole model.</summary>
    void FitAll();

    /// <summary>Turns to a standard view, showing the whole model.</summary>
    void ShowView(StandardView view);
}
