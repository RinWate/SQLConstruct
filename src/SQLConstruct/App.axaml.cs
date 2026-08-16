using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SQLConstruct.Services;
using SQLConstruct.ViewModels;
using SQLConstruct.Views;

namespace SQLConstruct;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialogs = new DialogService();
            var mainViewModel = new MainWindowViewModel(dialogs);
            var mainWindow = new MainWindow { DataContext = mainViewModel };
            dialogs.Main = mainWindow;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
