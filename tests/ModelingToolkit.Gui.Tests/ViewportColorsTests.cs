using Avalonia.Controls;
using Avalonia.Styling;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The colours of the 3D view come from resources of the theme, which must exist in both variants.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class ViewportColorsTests(HeadlessSession headless)
{
    [Fact]
    public async Task BothVariantsHaveTheColoursOfTheView()
    {
        int found = await headless.Session.Dispatch(() =>
        {
            // In a window: a control finds the resources of the application through its parents.
            var view = new ViewportView();
            var window = new Window { Content = view };
            window.Show();
            int count = 0;
            foreach (ThemeVariant theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                Assert.NotNull(view.ReadColors(theme));
                count++;
            }

            window.Close();
            return count;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, found);
    }
}
