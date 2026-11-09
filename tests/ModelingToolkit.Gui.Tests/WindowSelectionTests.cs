using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The faces and edges a window of the view selects, seen from above, one unit of the view being 1 mm.</summary>
public sealed class WindowSelectionTests
{
    private readonly OcctKernel _kernel = new();

    private static (double X, double Y)[] FromAbove(Point3d[] points) => [.. points.Select(point => (point.X, point.Y))];

    [Fact]
    public void A_window_holds_points_and_touches_segments_and_triangles()
    {
        var window = new WindowArea(0, 0, 10, 10);
        Assert.True(window.Holds((5, 5)));
        Assert.False(window.Holds((double.NaN, 5)));
        Assert.True(window.Touches((-5, 5), (15, 5)));
        Assert.False(window.Touches((-5, -1), (15, -1)));
        Assert.True(window.Touches(((-100, -100), (100, -100), (0, 100)))); // the window inside the triangle
        Assert.False(window.Touches(((20, 20), (30, 20), (20, 30))));
    }

    [Fact]
    public void Left_to_right_a_window_selects_what_it_holds_and_right_to_left_what_it_touches()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100));
        var document = new Document();
        document.Root.Entities.Add(box);
        var scene = ViewportScene.Of(document);

        List<ElementReference> all = scene.ElementsIn([], FromAbove, new WindowArea(-10, -10, 110, 110), crossing: false);
        Assert.Equal(6, all.Count(element => element.Kind == ElementKind.Face));
        Assert.Equal(12, all.Count(element => element.Kind == ElementKind.Edge));

        Assert.Single(scene.ElementsIn([], FromAbove, new WindowArea(-10, -10, 50, 110), crossing: false),
            element => element.Kind == ElementKind.Face); // the side at x = 0, seen edge on
        List<ElementReference> touched = scene.ElementsIn([], FromAbove, new WindowArea(-10, -10, 50, 110), crossing: true);
        Assert.Equal(5, touched.Count(element => element.Kind == ElementKind.Face)); // all but the side at x = 100
        Assert.Equal(8, touched.Count(element => element.Kind == ElementKind.Edge));
    }

    [Fact]
    public void Elements_are_selected_in_place_of_the_others_or_after_them()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100));
        var document = new Document();
        document.Root.Entities.Add(box);
        var selection = new ElementSelection(document);
        ElementReference Face(int index) => new([box], ElementKind.Face, index);

        selection.Select([Face(0), Face(1)], add: false);
        selection.Select([Face(1), Face(2)], add: true);
        Assert.Equal([Face(0), Face(1), Face(2)], selection.Elements);
        selection.Select([Face(3)], add: false);
        Assert.Equal([Face(3)], selection.Elements);
    }
}
