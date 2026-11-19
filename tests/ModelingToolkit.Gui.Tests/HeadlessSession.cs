using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(ModelingToolkit.Gui.Tests.HeadlessAssemblyFixture))]

namespace ModelingToolkit.Gui.Tests;

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
    public static ServiceProvider Services { get; } = AppHost.Build();

    /// <summary>
    /// The session, started before any test runs, by the assembly fixture below, and with its application made at once: the dispatcher of
    /// Avalonia belongs to the thread that asks for it first, and a test that touched it before, from its own thread,
    /// would keep the application from being made, as it did on Windows.
    /// </summary>
    // Lazy, not made by the type initializer: the thread of the session reads the members of this class while it makes
    // the application, which would wait for that initializer to end.
    private static readonly Lazy<HeadlessUnitTestSession> SharedSession = new(Start);

    public static HeadlessUnitTestSession Shared => SharedSession.Value;

#pragma warning disable CA1822 // Read through the fixture by the tests
    public HeadlessUnitTestSession Session => Shared;
#pragma warning restore CA1822

    private static HeadlessUnitTestSession Start()
    {
        HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(HeadlessSession), AvaloniaTestIsolationLevel.PerAssembly);
        session.Dispatch(() => 0, CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }


    // Looked up by HeadlessUnitTestSession.StartNew.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new App(Services))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSession>
{
    public const string Name = "Avalonia";
}

/// <summary>Starts the session before any test runs, whichever order and in parallel they run in.</summary>
public sealed class HeadlessAssemblyFixture
{
    public HeadlessAssemblyFixture() => _ = HeadlessSession.Shared;
}
