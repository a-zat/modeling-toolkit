using Avalonia.Input;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The faces, edges and vertices selected with clicks of the 3D view, and their properties.</summary>
public sealed class ElementSelectionTests : IDisposable
{
    private readonly OcctKernel _kernel = new();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mtk");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task AClickedFaceIsSelectedAloneWithItsAreaAndPlane()
    {
        (MainWindowViewModel viewModel, BrepEntity box) = await OpenBox();
        viewModel.ModelTree.SelectCommand.Execute(new Entity[] { box });
        ElementReference top = Face(box, normalZ: 1);

        viewModel.ViewInputCommand.Execute(Click(top));

        Assert.Equal([top], viewModel.SelectedElements);
        Assert.Null(viewModel.ModelTree.SelectedNode);
        Assert.False(viewModel.DeleteCommand.CanExecute(null));
        PropertiesViewModel properties = viewModel.Properties;
        Assert.True(properties.HasSelection);
        Assert.False(properties.IsEditable);
        Assert.Equal(("Face", "of Box"), (properties.Title, properties.Kind));
        Assert.Equal(
        [
            new PropertyRow("Area", "10,000.0 mm²"),
            new PropertyRow("Surface", "Plane"),
            new PropertyRow("Plane", "z = 100.0 mm"),
            new PropertyRow("Normal", "(0.000, 0.000, 1.000)"),
        ], properties.Rows);
    }

    [Fact]
    public async Task ShiftAddsAnElementOrTakesItOut()
    {
        (MainWindowViewModel viewModel, BrepEntity box) = await OpenBox();
        ElementReference first = new([box], ElementKind.Edge, 0), second = new([box], ElementKind.Edge, 1);
        ElementReference vertex = new([box], ElementKind.Vertex, 0);

        viewModel.ViewInputCommand.Execute(Click(first));
        Assert.Equal([new PropertyRow("Length", "100.0 mm"), new PropertyRow("Curve", "Line")], viewModel.Properties.Rows);
        viewModel.ViewInputCommand.Execute(Click(second, KeyModifiers.Shift));

        Assert.Equal([first, second], viewModel.SelectedElements);
        Assert.Equal(("2 elements", "Selection"), (viewModel.Properties.Title, viewModel.Properties.Kind));
        Assert.Equal([new PropertyRow("Edges", "2"), new PropertyRow("Total length", "200.0 mm")],
            viewModel.Properties.Rows);

        viewModel.ViewInputCommand.Execute(Click(vertex, KeyModifiers.Shift));
        viewModel.ViewInputCommand.Execute(Click(first, KeyModifiers.Shift));
        Assert.Equal([second, vertex], viewModel.SelectedElements);
        Assert.Equal([new PropertyRow("Edges", "1"), new PropertyRow("Vertices", "1")], viewModel.Properties.Rows);
    }

    [Fact]
    public async Task AClickOnNothingClearsTheSelection()
    {
        (MainWindowViewModel viewModel, BrepEntity box) = await OpenBox();
        viewModel.ViewInputCommand.Execute(Click(new ElementReference([box], ElementKind.Vertex, 0)));
        Assert.StartsWith("Position", viewModel.Properties.Rows[0].Name, StringComparison.Ordinal);

        viewModel.ViewInputCommand.Execute(new ViewportPick(null, null, default, default, KeyModifiers.None));

        Assert.Empty(viewModel.SelectedElements);
        Assert.Equal("Model", viewModel.Properties.Kind);
    }

    [Fact]
    public async Task SelectingANodeOfTheTreeUnselectsTheElements()
    {
        (MainWindowViewModel viewModel, BrepEntity box) = await OpenBox();
        viewModel.ViewInputCommand.Execute(Click(Face(box, normalZ: 1)));

        viewModel.ModelTree.SelectCommand.Execute(new Entity[] { box });

        Assert.Empty(viewModel.SelectedElements);
        Assert.Equal("Box", viewModel.Properties.Title);
        Assert.True(viewModel.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReplacingTheShapeUnselectsItsElementsAndUndoDoesNotBringThemBack()
    {
        (MainWindowViewModel viewModel, BrepEntity box) = await OpenBox();
        viewModel.ViewInputCommand.Execute(Click(Face(box, normalZ: 1)));

        viewModel.Edit(new Core.Editing.ReplaceShapeCommand("Shrink", box, _kernel.MakeBox(10, 10, 10)));

        Assert.Empty(viewModel.SelectedElements);
        Assert.Equal("Model", viewModel.Properties.Kind);
        viewModel.UndoCommand.Execute(null);
        Assert.Empty(viewModel.SelectedElements);
    }

    [Fact]
    public async Task TheFacesSelectedOrAWholeShapeArePaintedFromTheProperties()
    {
        (MainWindowViewModel viewModel, BrepEntity box) = await OpenBox();
        var red = new Material("Red");
        viewModel.Document!.Materials.Add(red);
        ElementReference top = Face(box, normalZ: 1);
        viewModel.ViewInputCommand.Execute(Click(top));
        viewModel.ViewInputCommand.Execute(Click(new ElementReference([box], ElementKind.Edge, 0), KeyModifiers.Shift));
        PropertiesViewModel properties = viewModel.Properties;
        Assert.True(properties.HasMaterial); // the face, not the edge

        properties.Paint(red);

        Assert.Equal([top.Index], box.FaceMaterials.Keys);
        Assert.Same(red, box.MaterialOf(top.Index));

        viewModel.ModelTree.SelectCommand.Execute(new Entity[] { box });
        Assert.True(properties.IsMixedMaterial);
        Assert.Equal("Mixed", properties.MaterialName);
        Assert.Null(properties.Material);
        properties.Paint(red);
        Assert.Equal(6, box.FaceMaterials.Count);
        Assert.False(properties.IsMixedMaterial);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal([top.Index], box.FaceMaterials.Keys);

        viewModel.ViewInputCommand.Execute(Click(new ElementReference([box], ElementKind.Vertex, 0)));
        Assert.False(properties.HasMaterial);
    }

    private static ViewportPick Click(ElementReference element, KeyModifiers modifiers = KeyModifiers.None) =>
        new(element.Path, element, default, default, modifiers);

    private ElementReference Face(BrepEntity box, double normalZ) => new([box], ElementKind.Face,
        Enumerable.Range(0, 6).Single(face => _kernel.GetFaceInfo(box.Shape, face).Plane?.Normal.Z == normalZ));

    private async Task<(MainWindowViewModel, BrepEntity)> OpenBox()
    {
        var document = new Document();
        document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100)) { Name = "Box" });
        new NativeDocumentWriter().Write(document, _path, null, CancellationToken.None);

        var viewModel = new MainWindowViewModel(
            new DocumentFormats([new NativeDocumentReader(_kernel)], []), new NoAppearance());
        await viewModel.LoadAsync(_path);
        return (viewModel, (BrepEntity)viewModel.Document!.Root.Entities[0]);
    }

    private sealed class NoAppearance : IAppearance
    {
        public void Apply(bool dark)
        {
        }
    }
}
