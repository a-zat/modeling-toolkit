using Avalonia.Input;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Gui.Tests;

/// <summary>Entering the blocks of references and leaving them, from the 3D view, the model tree and the keyboard.</summary>
public sealed class EditContextTests : IDisposable
{
    private readonly OcctKernel _kernel = new();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mtk");
    private MainWindowViewModel _viewModel = null!;
    private BlockReference _left = null!, _right = null!;
    private BrepEntity _box = null!;

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task AClickOnAReferenceSelectsItAndADoubleClickEntersIt()
    {
        await OpenParts();

        _viewModel.PickCommand.Execute(Click(_left));
        Assert.Equal([_left], _viewModel.ModelTree.SelectedNode?.Path);
        Assert.Empty(_viewModel.SelectedElements);
        Assert.False(_viewModel.IsEditingBlock);

        _viewModel.PickCommand.Execute(Click(_left, clicks: 2));

        Assert.True(_viewModel.IsEditingBlock);
        Assert.Equal("Left", _viewModel.ContextName);
        Assert.Equal([_left], _viewModel.Context!.Path);
        Assert.Null(_viewModel.ModelTree.SelectedNode);
    }

    [Fact]
    public async Task InABlockAClickSelectsAnElementOfItsShapes()
    {
        await OpenParts();
        _viewModel.PickCommand.Execute(Click(_left, clicks: 2));

        _viewModel.PickCommand.Execute(Click(_left));

        Assert.Equal([Face(_left)], _viewModel.SelectedElements);
        Assert.True(_viewModel.IsEditingBlock);
    }

    [Fact]
    public async Task AClickOutsideTheBlockLeavesItAndSelectsWhatWasClicked()
    {
        await OpenParts();
        _viewModel.PickCommand.Execute(Click(_left, clicks: 2));
        _viewModel.PickCommand.Execute(Click(_left));

        _viewModel.PickCommand.Execute(Click(_right));

        Assert.False(_viewModel.IsEditingBlock);
        Assert.Empty(_viewModel.SelectedElements);
        Assert.Equal([_right], _viewModel.ModelTree.SelectedNode?.Path);
    }

    [Fact]
    public async Task AClickOnNothingOrEscapeLeavesTheBlock()
    {
        await OpenParts();
        _viewModel.PickCommand.Execute(Click(_left, clicks: 2));
        _viewModel.PickCommand.Execute(new ViewportPick(null, null, default, default, KeyModifiers.None));
        Assert.False(_viewModel.IsEditingBlock);

        _viewModel.PickCommand.Execute(Click(_left, clicks: 2));
        _viewModel.PickCommand.Execute(Click(_left));
        _viewModel.LeaveContextCommand.Execute(null);

        Assert.False(_viewModel.IsEditingBlock);
        Assert.Empty(_viewModel.SelectedElements);
        Assert.Equal(string.Empty, _viewModel.ContextName);
    }

    [Fact]
    public async Task ADoubleClickOnANodeOfAReferenceEntersItAndSelectingANodeOutsideLeaves()
    {
        await OpenParts();
        ModelNodeViewModel right = _viewModel.ModelTree.Roots[0].Children[1];

        _viewModel.ModelTree.EnterCommand!.Execute(right);
        Assert.Equal([_right], _viewModel.Context!.Path);
        _viewModel.ModelTree.SelectCommand.Execute(new Entity[] { _right, _box });
        Assert.True(_viewModel.IsEditingBlock); // inside

        _viewModel.ModelTree.SelectCommand.Execute(new Entity[] { _left });

        Assert.False(_viewModel.IsEditingBlock);
        Assert.Equal([_left], _viewModel.ModelTree.SelectedNode?.Path);
    }

    private ElementReference Face(BlockReference reference) => new([reference, _box], ElementKind.Face, 0);

    private ViewportPick Click(BlockReference reference, int clicks = 1) =>
        new([reference, _box], Face(reference), default, default, KeyModifiers.None, clicks);

    private async Task OpenParts()
    {
        var part = new Block("Part") { Entities = { new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)) } };
        var document = new Document();
        document.Blocks.Add(part);
        document.Root.Entities.Add(new BlockReference(part, Transform.Identity) { Name = "Left" });
        document.Root.Entities.Add(new BlockReference(part, Transform.Translation(50, 0, 0)) { Name = "Right" });
        new NativeDocumentWriter().Write(document, _path, null, CancellationToken.None);

        _viewModel = new MainWindowViewModel(
            new DocumentFormats([new NativeDocumentReader(_kernel)], []), new NoAppearance());
        await _viewModel.LoadAsync(_path);
        Document read = _viewModel.Document!;
        _left = (BlockReference)read.Root.Entities[0];
        _right = (BlockReference)read.Root.Entities[1];
        _box = (BrepEntity)_left.Block.Entities[0];
    }

    private sealed class NoAppearance : IAppearance
    {
        public void Apply(bool dark)
        {
        }
    }
}
