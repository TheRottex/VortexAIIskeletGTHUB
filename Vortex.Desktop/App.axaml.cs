using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Vortex.Desktop.Services;
using Vortex.Desktop.ViewModels;

namespace Vortex.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var backend = new BackendClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5000") });
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(backend) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
