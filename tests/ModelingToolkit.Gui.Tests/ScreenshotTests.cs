using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Views;

namespace ModelingToolkit.Gui.Tests;

/// <summary>
/// Renders the main window, dark and light, with a model open, into artifacts/screenshots: the look of the application
/// at a glance. The test fails if a variant cannot be rendered.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ScreenshotTests(HeadlessSession headless)
{
    [Fact]
    public async Task BothVariantsAreRendered()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Paths.RepositoryRoot, "artifacts", "screenshots")).FullName;

        int rendered = await headless.Session.Dispatch(async () =>
        {
            var viewModel = HeadlessSession.Services.GetRequiredService<MainWindowViewModel>();
            await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
            // The whole tree open, with a mesh selected: references and meshes at a glance.
            ModelNodeViewModel[] nodes = [.. Flatten(viewModel.ModelTree.Model!)];
            foreach (ModelNodeViewModel node in nodes)
            {
                node.IsExpanded = true;
            }
            viewModel.ModelTree.SelectedNode = nodes.First(node => node.IsMesh);

            int count = 0;
            foreach (bool dark in new[] { true, false })
            {
                viewModel.IsDark = dark;

                var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
                window.Show();
                WriteableBitmap? frame = window.CaptureRenderedFrame();
                window.Close();

                Assert.NotNull(frame);
                frame.Save(Path.Combine(folder, $"main-{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
                count++;
            }

            return count;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, rendered);
    }

    private static IEnumerable<ModelNodeViewModel> Flatten(ModelNodeViewModel node) =>
        [node, .. node.Children.SelectMany(Flatten)];
}
