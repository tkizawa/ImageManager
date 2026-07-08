using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ImageManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly Services.SettingsService _settingsService;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(Services.SettingsService settingsService) : this()
    {
        _settingsService = settingsService;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        if (!double.IsNaN(settings.WindowWidth) && settings.WindowWidth > 0)
            Width = settings.WindowWidth;
        if (!double.IsNaN(settings.WindowHeight) && settings.WindowHeight > 0)
            Height = settings.WindowHeight;
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        WindowState = (WindowState)settings.WindowState;
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = _settingsService.Load();
        settings.WindowState = (int)WindowState;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
        }
        else
        {
            settings.WindowWidth = RestoreBounds.Width;
            settings.WindowHeight = RestoreBounds.Height;
            settings.WindowLeft = RestoreBounds.Left;
            settings.WindowTop = RestoreBounds.Top;
        }
        _settingsService.Save(settings);
    }

    private async void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ViewModels.DirectoryNodeViewModel node && DataContext is ViewModels.MainViewModel vm)
        {
            await vm.SelectFolderFromTreeAsync(node.FullPath);
        }
    }
}