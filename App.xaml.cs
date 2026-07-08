using System.Configuration;
using System.Data;
using System.Windows;

namespace ImageManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var fileSystemService = new ImageManager.Services.FileSystemService();
        var settingsService = new ImageManager.Services.SettingsService();
        var mainViewModel = new ImageManager.ViewModels.MainViewModel(fileSystemService, settingsService);
        
        var mainWindow = new MainWindow(settingsService)
        {
            DataContext = mainViewModel
        };
        
        mainWindow.Show();
    }
}

