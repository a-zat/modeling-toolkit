namespace ModelingToolkit.Gui.Viewport;

/// <summary>How a person walks through the model in the walk mode, in millimetres and seconds.</summary>
/// <param name="EyeHeight">Of the eyes above the floor.</param>
/// <param name="Speed">Of a walk, in millimetres per second.</param>
/// <param name="RunFactor">How much faster running is than walking.</param>
/// <param name="FieldOfView">Vertical, in degrees.</param>
/// <param name="LookSensitivity">The turn of a drag, in radians per point of the screen.</param>
/// <param name="CollisionRadius">How near the eyes come to what they bump into.</param>
public sealed record WalkSettings(
    double EyeHeight, double Speed, double RunFactor, double FieldOfView, double LookSensitivity, double CollisionRadius)
{
    /// <summary>A person of average height, walking, with the eyes of a room.</summary>
    public static WalkSettings Default { get; } = new(
        WalkCamera.EyeHeight, WalkCamera.Speed, WalkCamera.RunFactor, WalkCamera.FieldOfView, WalkCamera.LookSensitivity,
        WalkCollider.Radius);
}
