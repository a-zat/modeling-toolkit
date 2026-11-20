using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Views;

namespace ModelingToolkit.Gui.Tests;

[Collection(HeadlessCollection.Name)]
public sealed class OutlineMenuTests(HeadlessSession headless)
{
    [Fact]
    public async Task A_right_click_on_the_project_opens_no_menu_and_the_outline_still_answers()
    {
        (bool ProjectMenu, bool EntityMenu, string? Selected) result = await headless.Session.Dispatch<(bool, bool, string?)>(async () =>
        {
            var viewModel = HeadlessSession.Services.GetRequiredService<MainWindowViewModel>();
            await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
            var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
            window.Show();
            void Layout()
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame();
            }
            Layout();
            Grid RowOf(Func<ModelNodeViewModel, bool> which) => window.GetVisualDescendants().OfType<Grid>()
                .First(grid => grid.ContextMenu is not null && grid.DataContext is ModelNodeViewModel node && which(node));
            void Click(Grid row, MouseButton button)
            {
                Point middle = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
                window.MouseMove(middle);
                window.MouseDown(middle, button);
                window.MouseUp(middle, button);
                Layout();
            }

            Grid project = RowOf(node => node.IsModel);
            Click(project, MouseButton.Right);
            bool projectMenu = project.ContextMenu!.IsOpen;

            Grid entity = RowOf(node => node.HasEntity);
            Click(entity, MouseButton.Left);
            string? selected = viewModel.ModelTree.SelectedNode?.Name;
            Click(entity, MouseButton.Right);
            bool entityMenu = entity.ContextMenu!.IsOpen;
            entity.ContextMenu.Close();
            window.Close();
            return (projectMenu, entityMenu, selected);
        }, TestContext.Current.CancellationToken);

        Assert.False(result.ProjectMenu);
        Assert.True(result.EntityMenu);
        Assert.NotNull(result.Selected);
    }

    [Fact]
    public async Task Selecting_other_components_leaves_what_a_component_follows_as_it_is()
    {
        string folder = Directory.CreateTempSubdirectory("mtk-follows-").FullName;
        try
        {
            // A table with an anchor at its end, and a chair following it.
            var document = new Document();
            var top = new Block("Top");
            top.Entities.Add(new MeshEntity([new(0, 0, 760), new(900, 0, 760), new(900, 900, 760)], [new(0, 1, 2)]));
            var end = new AnchorEntity(Transform.Translation(900, 450, 0)) { Name = "End" };
            top.Entities.Add(end);
            var seat = new Block("Seat");
            seat.Entities.Add(new MeshEntity([new(0, 0, 450), new(400, 0, 450), new(400, 400, 450)], [new(0, 1, 2)]));
            document.Blocks.AddRange([top, seat]);
            var table = new BlockReference(top, Transform.Identity) { Name = "Table" };
            var chair = new BlockReference(seat, Transform.Translation(1000, 250, 0)) { Name = "Chair" };
            document.Root.Entities.Add(table);
            document.Root.Entities.Add(chair);
            document.Attach(new Attachment(chair, [table], end, Attachment.OffsetOf(chair, [table], end)));
            string path = Path.Combine(folder, "dining.mtk");
            new NativeDocumentWriter().Write(document, path);

            (int Attachments, string? Follows, bool Modified) result = await headless.Session.Dispatch<(int, string?, bool)>(async () =>
            {
                var viewModel = HeadlessSession.Services.GetRequiredService<MainWindowViewModel>();
                await viewModel.LoadAsync(path);
                var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
                window.Show();
                void Layout()
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.CaptureRenderedFrame();
                }
                Entity Named(string name) => viewModel.Document!.Root.Entities.First(entity => entity.Name == name);
                foreach (string name in (string[])["Chair", "Table", "Chair", "Table", "Chair"])
                {
                    viewModel.ModelTree.SelectCommand.Execute(new List<Entity> { Named(name) });
                    Layout();
                }
                // The inspector closed, as the walk does, and opened again.
                viewModel.ToggleInspectorCommand.Execute(null);
                Layout();
                viewModel.IsWalking = true;
                Layout();
                viewModel.IsWalking = false;
                viewModel.ToggleInspectorCommand.Execute(null);
                Layout();
                (int, string?, bool) seen = (viewModel.Document!.Attachments.Count, viewModel.Properties.Attached?.Label,
                    viewModel.IsModified);
                window.Close();
                return seen;
            }, TestContext.Current.CancellationToken);

            Assert.Equal(1, result.Attachments);
            Assert.Equal("End of Table", result.Follows);
            Assert.False(result.Modified);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
