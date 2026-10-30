using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class OcctKernelTests
{
    private readonly OcctKernel _kernel = new();

    [Fact]
    public void Info_reports_the_OpenCASCADE_version()
    {
        Assert.Equal("OpenCASCADE", _kernel.Info.Name);
        Assert.Matches(@"^\d+\.\d+\.\d+", _kernel.Info.Version);
    }

    [Fact]
    public void Box_volume_is_the_product_of_its_dimensions()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);

        Assert.Equal(6000, _kernel.ComputeVolume(box), precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MakeBox_rejects_invalid_dimensions_with_the_native_error_message(double size)
    {
        var exception = Assert.Throws<ArgumentException>(() => _kernel.MakeBox(size, 1, 1));

        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public void A_disposed_shape_cannot_be_used()
    {
        IKernelShape box = _kernel.MakeBox(1, 1, 1);
        box.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _kernel.ComputeVolume(box));
    }
}
