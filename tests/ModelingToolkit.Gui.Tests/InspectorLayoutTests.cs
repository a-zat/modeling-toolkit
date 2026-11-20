using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Views;

namespace ModelingToolkit.Gui.Tests;

[Collection(HeadlessCollection.Name)]
public sealed class InspectorLayoutTests(HeadlessSession headless)
{
    [Fact]
    public async Task The_outline_comes_back_at_the_height_the_splitter_gave_it_when_the_inspector_is_closed_and_opened_again()
    {
        (double[] Heights, bool Visible) result = await headless.Session.Dispatch<(double[], bool)>(async () =>
        {
            var viewModel = HeadlessSession.Services.GetRequiredService<MainWindowViewModel>();
            await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
            var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
            window.Show();
            Grid inspector = window.GetVisualDescendants().OfType<Grid>().First(grid => grid.Name == "Inspector");
            GridSplitter splitter = inspector.GetVisualChildren().OfType<GridSplitter>().First(item => Grid.GetRow(item) == 2);
            double[] Actual() => [.. inspector.RowDefinitions.Select(row => row.ActualHeight)];
            void Layout()
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
            }
            void Drag(double dy)
            {
                Point middle = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
                window.MouseMove(middle);
                window.MouseDown(middle, MouseButton.Left);
                for (int step = 1; step <= 10; step++)
                {
                    window.MouseMove(new Point(middle.X, middle.Y + dy * step / 10));
                }
                window.MouseUp(new Point(middle.X, middle.Y + dy), MouseButton.Left);
                Layout();
            }

            // Dragged down a little, then closed and opened: the same.
            Drag(+100);
            double[] dragged = Actual();
            viewModel.ToggleInspectorCommand.Execute(null);
            Layout();
            viewModel.ToggleInspectorCommand.Execute(null);
            Layout();
            double[] reopened = Actual();

            // Dragged up to nothing, then closed and opened: shown again, not a line.
            Drag(-2000);
            viewModel.ToggleInspectorCommand.Execute(null);
            Layout();
            viewModel.ToggleInspectorCommand.Execute(null);
            Layout();
            double[] fromNothing = Actual();
            window.Close();
            return ([dragged[1], reopened[1], fromNothing[1]], viewModel.IsOutlineExpanded);
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Visible);
        Assert.Equal(result.Heights[0], result.Heights[1], 1);
        Assert.True(result.Heights[2] > 50, $"the outline is {result.Heights[2]} points high");
    }
}
