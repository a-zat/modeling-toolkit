using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.App.ViewModels;
using ModelingToolkit.App.Views;
#if DEBUG
using RolandUI.DevScope;
#endif

namespace ModelingToolkit.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
#if DEBUG
    private IDisposable? _devTools;
#endif

    public App(IServiceProvider services) => _services = services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var viewModel = _services.GetRequiredService<MainWindowViewModel>();
        viewModel.ApplyAppearance();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            _services.GetRequiredService<Appearance>().ThemeReplaced += (_, _) => ReplaceMainWindow(desktop);
#if DEBUG
            // F12 opens the inspector of the visual tree and of the styles.
            _devTools = this.AttachDevTools();
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

    // A new theme brings new control templates: the window is created again, in the same place, to use them.
    private static void ReplaceMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is not { } previous)
        {
            return;
        }

        var window = new MainWindow
        {
            DataContext = previous.DataContext,
            Width = previous.Width,
            Height = previous.Height,
            Position = previous.Position,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowState = previous.WindowState,
        };
        desktop.MainWindow = window;
        window.Show();
        previous.Close();
    }
}
