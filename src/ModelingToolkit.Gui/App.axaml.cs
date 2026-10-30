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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
#if DEBUG
            // F12 opens the inspector of the visual tree and of the styles.
            _devTools = this.AttachDevTools();
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }
}
