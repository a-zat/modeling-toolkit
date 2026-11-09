using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Gui.Tests;

/// <summary>Cut, copy and paste of the entity selected.</summary>
public sealed class ClipboardTests : IDisposable
{
    private readonly OcctKernel _kernel = new();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mtk");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task A_copy_is_pasted_where_it_was_selected_for_the_move_tool()
    {
        MainWindowViewModel viewModel = await Open();
        Entity box = viewModel.Document!.Root.Entities[0];
        Assert.False(viewModel.CopyCommand.CanExecute(null));
        Assert.False(viewModel.PasteCommand.CanExecute(null));

        viewModel.ModelTree.SelectCommand.Execute(new[] { box });
        viewModel.CopyCommand.Execute(null);
        viewModel.PasteCommand.Execute(null);

        Assert.Equal(2, viewModel.Document.Root.Entities.Count);
        Entity pasted = viewModel.Document.Root.Entities[1];
        Assert.Equal("Box", pasted.Name);
        Assert.Equal(box.GetBoundingBox(), pasted.GetBoundingBox());
        Assert.Same(pasted, viewModel.ModelTree.SelectedNode?.Entity);
        Assert.IsType<MoveTool>(viewModel.ActiveTool);
        Assert.Contains("Paste", viewModel.UndoTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cut_removes_the_entity_and_paste_puts_it_back()
    {
        MainWindowViewModel viewModel = await Open();
        Entity box = viewModel.Document!.Root.Entities[0];
        viewModel.ModelTree.SelectCommand.Execute(new[] { box });

        viewModel.CutCommand.Execute(null);
        Assert.Empty(viewModel.Document.Root.Entities);
        viewModel.PasteCommand.Execute(null);

        Assert.Equal(box.GetBoundingBox(), Assert.Single(viewModel.Document.Root.Entities).GetBoundingBox());
    }

    private async Task<MainWindowViewModel> Open()
    {
        var document = new Document();
        document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100)) { Name = "Box" });
        new NativeDocumentWriter().Write(document, _path, null, CancellationToken.None);
        var viewModel = new MainWindowViewModel(
            new DocumentFormats([new NativeDocumentReader(_kernel)], []), new NoAppearance(), _kernel);
        await viewModel.LoadAsync(_path);
        return viewModel;
    }

    private sealed class NoAppearance : IAppearance
    {
        public void Apply(bool dark)
        {
        }
    }
}
