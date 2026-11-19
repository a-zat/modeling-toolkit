using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModelingToolkit.Gui;

/// <summary>
/// The icon of the application in the Dock of macOS, set while it runs: a bundle has its own, but run as a plain
/// executable, as with dotnet run, the Dock would show the generic one.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint bytes, nuint length);

    /// <summary>Shows a PNG image as the icon of the application; on the main thread, once the application is up.</summary>
    public static unsafe void Set(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        fixed (byte* bytes = png)
        {
            nint data = Send(GetClass("NSData"), Selector("dataWithBytes:length:"), (nint)bytes, (nuint)png.Length);
            nint image = Send(Send(GetClass("NSImage"), Selector("alloc")), Selector("initWithData:"), data);
            if (image == 0)
            {
                return;
            }
            Send(Send(GetClass("NSApplication"), Selector("sharedApplication")), Selector("setApplicationIconImage:"), image);
            Send(image, Selector("release"));
        }
    }
}
