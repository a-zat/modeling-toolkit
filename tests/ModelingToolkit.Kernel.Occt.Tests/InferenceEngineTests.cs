using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class InferenceEngineTests
{
    private readonly OcctKernel _kernel = new();
    private readonly InferenceEngine _engine = new();
    private readonly BrepEntity _box;
    private readonly IReadOnlyList<Entity> _path;

    public InferenceEngineTests()
    {
        _box = new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100));
        var block = new Block("Box");
        block.Entities.Add(_box);
        // The box placed 1000 mm along X.
        _path = [new BlockReference(block, Transform.Translation(1000, 0, 0)), _box];
    }

    // Looking straight down at a point of the ground, from 1 m above it, one unit of the screen being 1 mm.
    private static PointerRay Down(double x, double y, double z = 0) =>
        new(new Point3d(x, y, z + 1000), new Vector3d(0, 0, -1), 1, 0);

    private static ToolPick Nothing(PointerRay ray) => new(null, null, default, default, ray);

    private ToolPick On(ElementKind kind, int index, Point3d point) =>
        new(_path, new ElementReference(_path, kind, index), point, default, Down(point.X, point.Y, point.Z));

    private int Find(ElementKind kind, Func<int, bool> match)
    {
        ShapeTopology topology = _kernel.CountElements(_box.Shape);
        int count = kind switch { ElementKind.Face => topology.Faces, ElementKind.Edge => topology.Edges, _ => topology.Vertices };
        return Enumerable.Range(0, count).First(match);
    }

    [Fact]
    public void Over_nothing_the_point_is_where_the_ray_meets_the_ground_or_the_origin_when_close()
    {
        Inference ground = _engine.Infer(Nothing(Down(120, 250)), Transform.Identity);
        Assert.Equal(new Inference(InferenceKind.Ground, new Point3d(120, 250, 0), new Plane(Vector3d.UnitZ, 0)), ground);

        Assert.Equal(InferenceKind.Origin, _engine.Infer(Nothing(Down(3, -4)), Transform.Identity).Kind);
        Assert.Equal(new Point3d(0, 0, 0), _engine.Infer(Nothing(Down(3, -4)), Transform.Identity).Point);

        var upwards = new PointerRay(new Point3d(0, 0, 10), new Vector3d(0, 0, 1), 1, 0);
        Assert.Equal(Inference.Nothing, _engine.Infer(Nothing(upwards), Transform.Identity));

        // In a block entered, on its ground and at its origin.
        Inference inBlock = _engine.Infer(Nothing(Down(1002, 1, 50)), Transform.Translation(1000, 0, 50));
        Assert.Equal(new Point3d(1000, 0, 50), inBlock.Point);
        Assert.Equal(InferenceKind.Origin, inBlock.Kind);
    }

    [Fact]
    public void Drawing_from_a_point_snaps_along_the_axes_or_a_locked_direction()
    {
        var from = new Point3d(100, 100, 0);

        Inference x = _engine.Infer(Nothing(Down(500, 104)), Transform.Identity, from);
        Assert.Equal(InferenceKind.AxisX, x.Kind);
        Assert.Equal(new Point3d(500, 100, 0), x.Point);

        Inference free = _engine.Infer(Nothing(Down(500, 300)), Transform.Identity, from);
        Assert.Equal(InferenceKind.Ground, free.Kind);

        // Locked on Z: the point of the vertical line closest to the ray.
        var sideways = new PointerRay(new Point3d(100, -1000, 250), new Vector3d(0, 1, 0), 1, 0);
        Inference up = _engine.Infer(Nothing(sideways), Transform.Identity, from, Vector3d.UnitZ);
        Assert.Equal(InferenceKind.AxisZ, up.Kind);
        Assert.Equal(250, up.Point.Z, precision: 9);
        Assert.Equal(100, up.Point.X, precision: 9);
    }

    [Fact]
    public void Vertices_middles_and_points_of_edges_are_exact_and_in_the_model_space()
    {
        int corner = Find(ElementKind.Vertex, v => _kernel.GetVertexPoint(_box.Shape, v) == new Point3d(100, 100, 100));
        Inference endpoint = _engine.Infer(On(ElementKind.Vertex, corner, new Point3d(1099.7, 100.2, 100)), Transform.Identity);
        Assert.Equal(new Inference(InferenceKind.Endpoint, new Point3d(1100, 100, 100)), endpoint);

        // The top edge along X at y = 0.
        int edge = Find(ElementKind.Edge, e =>
        {
            EdgeInfo info = _kernel.GetEdgeInfo(_box.Shape, e);
            return info.Middle.Y == 0 && info.Middle.Z == 100 && Math.Abs(info.Middle.X - 50) < 1e-9;
        });
        Inference middle = _engine.Infer(On(ElementKind.Edge, edge, new Point3d(1053, 0.3, 100)), Transform.Identity);
        Assert.Equal(InferenceKind.Midpoint, middle.Kind);
        Assert.Equal(new Point3d(1050, 0, 100), middle.Point);

        Inference on = _engine.Infer(On(ElementKind.Edge, edge, new Point3d(1020, 0.3, 100)), Transform.Identity);
        Assert.Equal(InferenceKind.OnEdge, on.Kind);
        Assert.Equal(1020, on.Point.X, precision: 9);
        Assert.Equal(0, on.Point.Y, precision: 9);
        Assert.Equal(new Vector3d(1, 0, 0), new Vector3d(Math.Abs(_engine.Reference!.Value.X), 0, 0));
    }

    [Fact]
    public void A_point_on_a_flat_face_is_on_its_plane_which_is_drawn_on()
    {
        int top = Find(ElementKind.Face, f => _kernel.GetFaceInfo(_box.Shape, f).Plane is { Normal.Z: > 0.5 });

        Inference onTop = _engine.Infer(On(ElementKind.Face, top, new Point3d(1030, 60, 100.001)), Transform.Identity);

        Assert.Equal(InferenceKind.OnFace, onTop.Kind);
        Assert.Equal(new Point3d(1030, 60, 100), onTop.Point);
        Assert.Equal(new Plane(Vector3d.UnitZ, 100), onTop.Plane);
    }

    [Fact]
    public void After_pointing_at_an_edge_a_line_snaps_parallel_or_perpendicular_to_it()
    {
        // A diagonal line on the ground, pointed at.
        IKernelShape lines = _kernel.AddEdges(null, [new LineCurve(new(0, 0, 0), new(300, 300, 0))]).Shape;
        var entity = new BrepEntity(_kernel, lines);
        IReadOnlyList<Entity> path = [entity];
        _engine.Infer(new ToolPick(path, new ElementReference(path, ElementKind.Edge, 0), new(100, 100, 0), default,
            Down(100, 100)), Transform.Identity);

        var from = new Point3d(0, 500, 0);
        Inference parallel = _engine.Infer(Nothing(Down(203, 700)), Transform.Identity, from);
        Assert.Equal(InferenceKind.Parallel, parallel.Kind);
        Assert.Equal(parallel.Point.X - from.X, parallel.Point.Y - from.Y, precision: 9);

        Inference perpendicular = _engine.Infer(Nothing(Down(203, 300)), Transform.Identity, from);
        Assert.Equal(InferenceKind.Perpendicular, perpendicular.Kind);
        Assert.Equal(-(perpendicular.Point.X - from.X), perpendicular.Point.Y - from.Y, precision: 9);
    }

    [Fact]
    public void On_a_face_a_line_snaps_only_along_directions_in_its_plane()
    {
        int top = Find(ElementKind.Face, f => _kernel.GetFaceInfo(_box.Shape, f).Plane is { Normal.Z: > 0.5 });
        var from = new Point3d(1030, 50, 100);
        // A ray that passes right over the vertical through the point, 200 mm above the face, and lands on the face.
        var origin = new Point3d(910, -40, 700);
        var landing = new Point3d(1090, 95, 100);
        var ray = new PointerRay(origin, (landing - origin).Normalized(), 1, 0);
        var pick = new ToolPick(_path, new ElementReference(_path, ElementKind.Face, top), landing, default, ray);

        Inference inferred = _engine.Infer(pick, Transform.Identity, from);

        Assert.Equal(InferenceKind.OnFace, inferred.Kind);
        Assert.Equal(100, inferred.Point.Z, precision: 9);
        Assert.Equal(InferenceKind.AxisZ, _engine.Infer(Nothing(ray), Transform.Identity, from).Kind); // in the air
    }

    [Fact]
    public void Vertices_near_the_pointer_are_snapped_to_unless_hidden()
    {
        Point3d[] vertices = [new(100, 100, 0), new(1050, 50, 0)];

        Inference near = _engine.Infer(Nothing(Down(104, 97)), Transform.Identity, vertices: vertices);
        Assert.Equal(new Inference(InferenceKind.Endpoint, new Point3d(100, 100, 0)), near);
        Assert.Equal(InferenceKind.Ground, _engine.Infer(Nothing(Down(120, 100)), Transform.Identity, vertices: vertices).Kind);

        // At a grazing angle, a vertex a few units away on the screen is much deeper than the point under the pointer.
        var grazing = new PointerRay(new Point3d(100, -20000, 300), (new Point3d(100, 103, 0) - new Point3d(100, -20000, 300)).Normalized(), 1, 0);
        Assert.Equal(InferenceKind.Endpoint, _engine.Infer(Nothing(grazing), Transform.Identity, vertices: vertices).Kind);

        // Under the top of the box: hidden by it.
        int top = Find(ElementKind.Face, f => _kernel.GetFaceInfo(_box.Shape, f).Plane is { Normal.Z: > 0.5 });
        Inference onTop = _engine.Infer(On(ElementKind.Face, top, new Point3d(1051, 50, 100)), Transform.Identity,
            vertices: vertices);
        Assert.Equal(InferenceKind.OnFace, onTop.Kind);
    }

    [Fact]
    public void The_pointer_snaps_to_a_guide_and_where_two_guides_cross()
    {
        _engine.Guides = [new GuideLine(new Point3d(0, 50, 0), Vector3d.UnitX), new GuideLine(new Point3d(500, 0, 0), Vector3d.UnitY)];

        Inference on = _engine.Infer(Nothing(Down(300, 54)), Transform.Identity);
        Assert.Equal((InferenceKind.OnGuide, new Point3d(300, 50, 0)), (on.Kind, Round(on.Point)));

        Inference crossing = _engine.Infer(Nothing(Down(504, 53)), Transform.Identity);
        Assert.Equal((InferenceKind.Intersection, new Point3d(500, 50, 0)), (crossing.Kind, Round(crossing.Point)));

        Assert.Equal(InferenceKind.Ground, _engine.Infer(Nothing(Down(300, 80)), Transform.Identity).Kind);
    }

    [Fact]
    public void After_pointing_at_a_circle_or_a_face_bounded_by_one_its_centre_is_snapped_to()
    {
        // A cylinder 50 mm in radius and 100 mm high, at the origin.
        var cylinder = new BrepEntity(_kernel, _kernel.MakeCylinder(50, 100));
        IReadOnlyList<Entity> path = [cylinder];
        ShapeTopology topology = _kernel.CountElements(cylinder.Shape);
        int rim = Enumerable.Range(0, topology.Edges).First(edge =>
            _kernel.GetEdgeInfo(cylinder.Shape, edge) is { Circle: not null, Start.Z: > 99 });
        int bottom = Enumerable.Range(0, topology.Faces).First(face =>
            _kernel.GetFaceInfo(cylinder.Shape, face).Plane is { Normal.Z: < -0.5 });

        // Near the centre at first, nothing marks it.
        Assert.NotEqual(InferenceKind.CircleCentre, _engine.Infer(Nothing(Down(3, 2, 100)), Transform.Identity).Kind);

        _engine.Infer(new ToolPick(path, new ElementReference(path, ElementKind.Edge, rim), new(50, 0, 100), default,
            Down(50, 0, 100)), Transform.Identity);
        Inference top = _engine.Infer(Nothing(Down(3, 2, 100)), Transform.Identity);
        Assert.Equal((InferenceKind.CircleCentre, new Point3d(0, 0, 100)), (top.Kind, Round(top.Point)));

        // A face woken by pointing at it, from below.
        var engine = new InferenceEngine();
        var up = new PointerRay(new Point3d(20, 0, -1000), new Vector3d(0, 0, 1), 1, 0);
        engine.Infer(new ToolPick(path, new ElementReference(path, ElementKind.Face, bottom), new(20, 0, 0), default, up),
            Transform.Identity);
        Inference low = engine.Infer(Nothing(new PointerRay(new Point3d(2, 1, -1000), new Vector3d(0, 0, 1), 1, 0)),
            Transform.Identity);
        Assert.Equal((InferenceKind.CircleCentre, new Point3d(0, 0, 0)), (low.Kind, Round(low.Point)));
    }

    private static Point3d Round(Point3d point) => new(Math.Round(point.X, 6), Math.Round(point.Y, 6), Math.Round(point.Z, 6));
}
