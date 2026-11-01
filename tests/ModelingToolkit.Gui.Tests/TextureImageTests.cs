using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.Tests;

/// <summary>Textures reach the viewer as RGBA pixels, row after row from the top.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class TextureImageTests(HeadlessSession headless)
{
    [Fact]
    public async Task DecodesImagesIntoRgbaRowsFromTheTop()
    {
        // 2 x 2 pixels: red and green on the top row, blue and half transparent white below.
        byte[] expected = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 128];

        TextureImage? image = await headless.Session.Dispatch(
            () => TextureImage.Decode(new Texture("Quadrants", "image/png", Png(expected, 2, 2))),
            TestContext.Current.CancellationToken);

        Assert.NotNull(image);
        Assert.Equal((2, 2), (image.Width, image.Height));
        Assert.Equal(expected[..12], image.Pixels[..12]);
        // Not premultiplied by its alpha, though the trip through premultiplied pixels may round its colour.
        Assert.Equal(128, image.Pixels[15]);
        Assert.All(image.Pixels[12..15], component => Assert.InRange(component, 254, 255));
    }

    [Fact]
    public async Task ImagesThatCannotBeDecodedHaveNoPixels()
    {
        TextureImage? image = await headless.Session.Dispatch(
            () => TextureImage.Decode(new Texture("Broken", "image/png", [1, 2, 3, 4])),
            TestContext.Current.CancellationToken);

        Assert.Null(image);
    }

    private static byte[] Png(byte[] rgba, int width, int height)
    {
        using var bitmap = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            for (int row = 0; row < height; row++)
            {
                Marshal.Copy(rgba, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
