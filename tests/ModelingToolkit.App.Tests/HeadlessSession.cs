using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.Hosting;

namespace ModelingToolkit.App.Tests;

/// <summary>
/// The application without a screen, rendered by Skia as on a real window. Avalonia allows one application per
/// process, so the tests that need it share this session.
/// </summary>
/// <remarks>
/// <para>
/// The application is shared by all the tests (<see cref="AvaloniaTestIsolationLevel.PerAssembly"/>): with the default
/// isolation, Avalonia resets it after each dispatch and waits for the work still queued, which never ends when a theme
/// has left work running in the background.
/// </para>
/// <para>
/// The session is never disposed: with Avalonia 12.1.4, <see cref="HeadlessUnitTestSession.Dispose"/> does not return,
/// even for a session that ran nothing. Its loop runs on a thread pool thread, which ends with the test process.
/// </para>
/// </remarks>
public sealed class HeadlessSession
{
    public static IHost Host { get; } = AppHost.Build();

    public HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.StartNew(typeof(HeadlessSession), AvaloniaTestIsolationLevel.PerAssembly);

    // Looked up by HeadlessUnitTestSession.StartNew.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new App(Host.Services))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSession>
{
    public const string Name = "Avalonia";
}
