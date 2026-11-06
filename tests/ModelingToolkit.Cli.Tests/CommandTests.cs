using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Tests;

// Runs the commands in process, capturing what they write.
public sealed class CommandTests : IDisposable
{
    private readonly IHost _host = CliHost.Build();
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-cli-tests-").FullName;

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void A_glTF_model_converted_to_the_native_format_keeps_its_content()
    {
        string native = Path.Combine(_folder, "truck.mtk");

        (int exitCode, string output, _) = Run("convert", Asset("CesiumMilkTruck.glb"), native);
        Assert.Equal(0, exitCode);
        Assert.Equal("Converted CesiumMilkTruck.glb (glTF) to truck.mtk (Modeling Toolkit).", output.Trim());

        string[] original = StatisticsWithoutTitle(Asset("CesiumMilkTruck.glb"));
        string[] converted = StatisticsWithoutTitle(native);
        Assert.Equal(original, converted);
        Assert.Contains("  Meshes            4 (5 placed)", original);
    }

    [Fact]
    public void Statistics_describe_the_model()
    {
        (int exitCode, string output, _) = Run("stats", Asset("Box.glb"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
            Box.glb (glTF)
              Blocks            1
              Block references  1
              Meshes            1 (1 placed)
              Vertices          24
              Triangles         12 (12 placed)
              Materials         1
              Textures          0
              Bounds (mm)       (-500.0, -500.0, -500.0) to (500.0, 500.0, 500.0)
              Size (mm)         1,000.0 × 1,000.0 × 1,000.0
            """,
            output.TrimEnd().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Unsupported_formats_are_reported_without_writing_anything()
    {
        string output = Path.Combine(_folder, "truck.obj");

        (int exitCode, _, string error) = Run("convert", Asset("CesiumMilkTruck.glb"), output);

        Assert.Equal(1, exitCode);
        Assert.Equal("Error: '.obj' files cannot be written. Supported: .mtk.", error.Trim());
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void What_a_model_leaves_out_is_reported_on_standard_error()
    {
        string path = Path.Combine(_folder, "basisu.gltf");
        const string extensions = """
            "extensionsUsed": ["KHR_texture_basisu"], "extensionsRequired": ["KHR_texture_basisu"], "asset"
            """;
        File.WriteAllText(path, File.ReadAllText(Asset("SimpleMeshes.gltf")).Replace("\"asset\"", extensions));

        (int exitCode, string output, string error) = Run("stats", path);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("basisu.gltf (glTF)", output);
        Assert.Equal(
            "Warning: Unsupported glTF extension KHR_texture_basisu: "
            + "the textures or materials that need it are left out.",
            error.Trim());
    }

    [Fact]
    public void A_shape_made_by_the_command_is_a_model_of_a_single_B_rep_shape()
    {
        string path = Path.Combine(_folder, "box.mtk");

        (int exitCode, string output, _) = Run("make", "box", "100", "50", "30", path);

        Assert.Equal(0, exitCode);
        Assert.Equal("Made a box in box.mtk (Modeling Toolkit).", output.Trim());
        string[] statistics = StatisticsWithoutTitle(path);
        Assert.Contains("  B-rep shapes      1 (1 placed)", statistics);
        Assert.Contains("  Size (mm)         100.0 × 50.0 × 30.0", statistics);
        Assert.Equal("Box", Assert.IsType<BrepEntity>(Assert.Single(Read(path).Root.Entities)).Name);
    }

    [Fact]
    public void Every_primitive_can_be_made_and_put_where_it_is_wanted()
    {
        (string[] Args, double Volume)[] shapes =
        [
            (["cylinder", "10", "40"], Math.PI * 10 * 10 * 40),
            (["sphere", "25"], 4.0 / 3 * Math.PI * 25 * 25 * 25),
            (["cone", "30", "10", "60"], Math.PI * 60 / 3 * (30 * 30 + 30 * 10 + 10 * 10)),
            (["torus", "50", "10"], 2 * Math.PI * Math.PI * 50 * 10 * 10),
        ];
        var kernel = _host.Services.GetRequiredService<IGeometryKernel>();

        foreach ((string[] args, double volume) in shapes)
        {
            string path = Path.Combine(_folder, $"{args[0]}.mtk");

            (int exitCode, _, string error) = Run(["make", .. args, path, "--at", "100,200,-300.5"]);

            Assert.True(exitCode == 0, error);
            var shape = Assert.IsType<BrepEntity>(Assert.Single(Read(path).Root.Entities));
            Assert.Equal(volume, kernel.ComputeVolume(shape.Shape), precision: 3);
            Assert.InRange(shape.GetBoundingBox().Min.X, 100 - 60 - 1e-6, 100);
            Assert.InRange(shape.GetBoundingBox().Max.Z, -300.5, -300.5 + 60 + 1e-6);
        }
    }

    [Fact]
    public void Bounds_never_print_a_negative_zero()
    {
        string path = Path.Combine(_folder, "cylinder.mtk");
        Assert.Equal(0, Run("make", "cylinder", "10", "40", path).ExitCode);

        string[] statistics = StatisticsWithoutTitle(path);

        Assert.Contains("  Bounds (mm)       (-10.0, -10.0, 0.0) to (10.0, 10.0, 40.0)", statistics);
    }

    [Fact]
    public void Shapes_are_combined_by_boolean_operations()
    {
        string box = Path.Combine(_folder, "box.mtk");
        string drill = Path.Combine(_folder, "drill.mtk");
        string drilled = Path.Combine(_folder, "drilled.mtk");
        string both = Path.Combine(_folder, "both.mtk");
        string common = Path.Combine(_folder, "common.mtk");
        Assert.Equal(0, Run("make", "box", "100", "100", "100", box).ExitCode);
        Assert.Equal(0, Run("make", "cylinder", "20", "120", drill, "--at", "50,50,-10").ExitCode);

        (int exitCode, string output, string error) = Run("combine", "subtract", box, drill, drilled);
        Assert.True(exitCode == 0, error);
        Assert.Equal("Combined box.mtk and drill.mtk in drilled.mtk (Modeling Toolkit).", output.Trim());
        Assert.Equal(0, Run("combine", "union", box, drill, both).ExitCode);
        Assert.Equal(0, Run("combine", "intersect", box, drill, common).ExitCode);

        var kernel = _host.Services.GetRequiredService<IGeometryKernel>();
        double Volume(string path) => kernel.ComputeVolume(((BrepEntity)Read(path).Root.Entities[0]).Shape);
        double hole = Math.PI * 20 * 20 * 100;
        Assert.Equal(1e6 - hole, Volume(drilled), precision: 2);
        Assert.Equal(1e6 + Math.PI * 20 * 20 * 20, Volume(both), precision: 2);
        Assert.Equal(hole, Volume(common), precision: 2);
        Assert.Equal("Subtract", ((BrepEntity)Read(drilled).Root.Entities[0]).Name);
    }

    [Fact]
    public void Shapes_that_cannot_be_made_or_combined_are_reported_without_writing_anything()
    {
        string sphere = Path.Combine(_folder, "sphere.mtk");
        string far = Path.Combine(_folder, "far.mtk");
        string box = Path.Combine(_folder, "box.mtk");
        string result = Path.Combine(_folder, "result.mtk");
        Assert.Equal(0, Run("make", "sphere", "10", sphere).ExitCode);
        Assert.Equal(0, Run("make", "box", "10", "10", "10", box, "--at", "100,0,0").ExitCode);

        (int zero, _, string zeroError) = Run("make", "sphere", "0", result);
        (int badPlace, _, string badPlaceError) = Run("make", "sphere", "1", result, "--at", "1,2");
        (int badFormat, _, string badFormatError) = Run("make", "sphere", "1", Path.Combine(_folder, "sphere.obj"));
        (int empty, _, string emptyError) = Run("combine", "intersect", sphere, box, result);
        (int notAShape, _, string notAShapeError) =
            Run("combine", "union", Asset("Box.glb"), sphere, result);

        Assert.Equal([1, 1, 1, 1, 1], [zero, badPlace, badFormat, empty, notAShape]);
        Assert.Contains("Sphere radius", zeroError);
        Assert.Contains("--at takes three numbers", badPlaceError);
        Assert.Contains("'.obj' files cannot be written", badFormatError);
        Assert.Contains("empty", emptyError);
        Assert.Contains("must hold a single B-rep shape", notAShapeError);
        Assert.False(File.Exists(result));
        Assert.False(File.Exists(Path.Combine(_folder, "sphere.obj")));
    }

    [Fact]
    public void Missing_files_are_reported()
    {
        (int exitCode, _, string error) = Run("stats", Path.Combine(_folder, "missing.glb"));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("missing.glb", error);
    }

    private (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliHost.CreateRootCommand(_host.Services)
            .Parse(args)
            .Invoke(new InvocationConfiguration { Output = output, Error = error });
        return (exitCode, output.ToString(), error.ToString());
    }

    private Document Read(string path) =>
        _host.Services.GetRequiredService<DocumentFormats>().ReaderFor(path).Read(path, []);

    private string[] StatisticsWithoutTitle(string path)
    {
        (int exitCode, string output, _) = Run("stats", path);
        Assert.Equal(0, exitCode);
        return output.ReplaceLineEndings("\n").TrimEnd().Split('\n')[1..];
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "assets", "gltf", name);
}
