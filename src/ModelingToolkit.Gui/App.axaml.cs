using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Views;
#if DEBUG
using RolandUI.DevScope;
#endif

namespace ModelingToolkit.Gui;

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
#if !DEBUG
        viewModel.AsksToSaveOnClose = true;
#endif
        viewModel.NewCommand.Execute(null); // an empty model to draw in, which opening a file replaces

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;

            // A model given on the command line opens once the window is shown, as with File > Open. Options, such as
            // those that macOS may add, are not models.
            if (desktop.Args?.FirstOrDefault(argument => !argument.StartsWith('-')) is { } path)
            {
                window.Opened += async (_, _) => await viewModel.LoadAsync(Path.GetFullPath(path));
            }
#if DEBUG
            // F12 opens the inspector of the visual tree and of the styles.
            _devTools = this.AttachDevTools();
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }
}
