using Microsoft.UI.Xaml;

namespace ImageManager;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (s, e) =>
        {
            System.IO.File.WriteAllText("crash.log", e.Exception?.ToString() + "\n" + e.Message);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try 
        {
            var fileSystemService = new ImageManager.Services.FileSystemService();
            var settingsService = new ImageManager.Services.SettingsService();
            var mainViewModel = new ImageManager.ViewModels.MainViewModel(fileSystemService, settingsService);
            
            MainWindow = new MainWindow(settingsService, mainViewModel);
            MainWindow.Activate();
        }
        catch (System.Exception ex)
        {
            System.IO.File.WriteAllText("crash.log", ex.ToString());
        }
    }
}
