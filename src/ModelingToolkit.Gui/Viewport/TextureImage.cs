using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// The image of a texture as the viewer takes it: pixels of 4 bytes, RGBA in sRGB without premultiplied alpha, row
/// after row from the top one.
/// </summary>
internal sealed record TextureImage(byte[] Pixels, int Width, int Height)
{
    /// <summary>The longest side the viewer accepts, as graphics cards.</summary>
    public const int MaxSize = 16384;

    /// <summary>
    /// Decodes the image of a texture, in any format Avalonia reads (PNG, JPEG, WebP...); null when it cannot, or when
    /// the image is too large. It works on any thread, once Avalonia is initialized.
    /// </summary>
    public static TextureImage? Decode(Texture texture)
    {
        try
        {
            using var stream = new MemoryStream(texture.Data, writable: false);
            using var bitmap = new Bitmap(stream);
            PixelSize size = bitmap.PixelSize;
            if (size.Width > MaxSize || size.Height > MaxSize)
            {
                return null;
            }

            using var converted = new WriteableBitmap(size, bitmap.Dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using ILockedFramebuffer buffer = converted.Lock();
            bitmap.CopyPixels(buffer);

            int rowLength = size.Width * 4;
            var pixels = new byte[rowLength * size.Height];
            for (int row = 0; row < size.Height; row++)
            {
                Marshal.Copy(buffer.Address + row * buffer.RowBytes, pixels, row * rowLength, rowLength);
            }

            return new TextureImage(pixels, size.Width, size.Height);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // Avalonia throws ArgumentException for data it cannot decode.
            return null;
        }
    }
}
