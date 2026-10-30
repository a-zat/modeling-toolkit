using System.CommandLine;
using Microsoft.Extensions.Hosting;

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

    private string[] StatisticsWithoutTitle(string path)
    {
        (int exitCode, string output, _) = Run("stats", path);
        Assert.Equal(0, exitCode);
        return output.ReplaceLineEndings("\n").TrimEnd().Split('\n')[1..];
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "assets", "gltf", name);
}
