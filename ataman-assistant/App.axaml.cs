using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ataman.Core;
using Ataman.Core.Settings;
using AtamanAssistant.ViewModels;
using AtamanAssistant.Views;

namespace AtamanAssistant;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsBrowser())
        {
            this.AttachDeveloperTools();
        }
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.MainWindow = new MainWindow
            {
                DataContext = CreateMainViewModel(),
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new ShellView
            {
                DataContext = CreateMainViewModel(),
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            singleViewLifetime.MainView = new ShellView
            {
                DataContext = CreateMainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainViewModel CreateMainViewModel()
    {
        AppPaths.EnsureCreated();
        var settingsStore = new JsonSettingsStore(AppPaths.Data);
        var engine = AppServices.CreateDefaultEngine(settingsStore);
        return new MainViewModel(settingsStore, engine);
    }
}