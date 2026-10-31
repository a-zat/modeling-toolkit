using Avalonia.Controls;
using Avalonia.Styling;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The colours of the 3D view come from resources of the theme, which must exist in both variants.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class ViewportColorsTests(HeadlessSession headless)
{
    [Fact]
    public async Task EveryStyleOfTheViewCubeHasItsColoursInBothVariants()
    {
        int found = await headless.Session.Dispatch(() =>
        {
            // In a window: a control finds the resources of the application through its parents.
            var view = new ViewportView();
            var window = new Window { Content = view };
            window.Show();
            int count = 0;
            foreach (ViewCubeStyle style in Enum.GetValues<ViewCubeStyle>())
            {
                view.CubeStyle = style;
                foreach (ThemeVariant theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
                {
                    ViewportColors? colors = view.ReadColors(theme);
                    Assert.NotNull(colors);
                    Assert.True(colors.CubeFace.A > 0, $"The ViewCube {style} is invisible in the {theme} variant.");
                    count++;
                }
            }

            window.Close();
            return count;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(6, found);
    }
}
