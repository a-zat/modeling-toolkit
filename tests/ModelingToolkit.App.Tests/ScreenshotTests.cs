using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.App.ViewModels;
using ModelingToolkit.App.Views;

namespace ModelingToolkit.App.Tests;

/// <summary>
/// Renders both layouts in every theme, dark and light, with a model open, into artifacts/screenshots: the images of
/// the prototype (#10), to compare at a glance. The test fails if a combination cannot be rendered.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ScreenshotTests(HeadlessSession headless)
{
    [Fact]
    public async Task EveryLayoutAndThemeIsRendered()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Paths.RepositoryRoot, "artifacts", "screenshots")).FullName;

        int rendered = await headless.Session.Dispatch(async () =>
        {
            var viewModel = HeadlessSession.Host.Services.GetRequiredService<MainWindowViewModel>();
            await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
            viewModel.ModelTree.SelectedNode = viewModel.ModelTree.Roots[0].Children[0];

            int count = 0;
            foreach (LayoutKind layout in viewModel.Layouts)
            {
                foreach (ThemeKind theme in viewModel.Themes)
                {
                    foreach (bool dark in new[] { true, false })
                    {
                        viewModel.Layout = layout;
                        viewModel.Theme = theme;
                        viewModel.IsDark = dark;

                        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
                        window.Show();
                        WriteableBitmap? frame = window.CaptureRenderedFrame();
                        window.Close();

                        Assert.NotNull(frame);
                        frame.Save(Path.Combine(folder, FileName(layout, theme, dark)), PngBitmapEncoderOptions.Default);
                        count++;
                    }
                }
            }

            return count;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2 * 5 * 2, rendered);
    }

    private static string FileName(LayoutKind layout, ThemeKind theme, bool dark) =>
        $"{(layout == LayoutKind.Classic ? "a" : "b")}-{theme.ToString().ToLowerInvariant()}-{(dark ? "dark" : "light")}.png";
}
