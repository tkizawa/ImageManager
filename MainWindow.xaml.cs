using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using WinRT.Interop;
using Microsoft.UI;

namespace ImageManager;

public partial class MainWindow : Window
{
    private readonly Services.SettingsService _settingsService = null!;
    public ViewModels.MainViewModel ViewModel { get; } = null!;
    
    private AppWindow _appWindow = null!;

    public MainWindow()
    {
        try {
            InitializeComponent();
            
            // Get AppWindow
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            _appWindow.Title = "Image Manager";
        } catch (System.Exception ex) {
            System.IO.File.WriteAllText("crash_main.log", ex.ToString());
        }
    }

    public MainWindow(Services.SettingsService settingsService, ViewModels.MainViewModel mainViewModel) : this()
    {
        _settingsService = settingsService;
        ViewModel = mainViewModel;
        RootGrid.DataContext = ViewModel;

        RootGrid.Loaded += RootGrid_Loaded;
        this.Closed += MainWindow_Closed;

        // Handle Ctrl + Wheel
        RootGrid.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerWheelChanged), true);
    }

    private void ThumbnailGridView_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var isCtrlPressed = (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrlPressed)
        {
            e.Handled = true;

            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            int delta = properties.MouseWheelDelta;

            if (delta > 0)
            {
                ViewModel.ThumbnailSize = System.Math.Clamp(ViewModel.ThumbnailSize + 20, 50, 500);
            }
            else if (delta < 0)
            {
                ViewModel.ThumbnailSize = System.Math.Clamp(ViewModel.ThumbnailSize - 20, 50, 500);
            }
        }
    }

    private GridLength ParseGridLength(string value, GridLength fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        if (value.EndsWith("*"))
        {
            if (double.TryParse(value.TrimEnd('*'), out double starVal))
                return new GridLength(starVal, GridUnitType.Star);
            return new GridLength(1, GridUnitType.Star);
        }
        if (double.TryParse(value, out double pxVal))
            return new GridLength(pxVal, GridUnitType.Pixel);
        return fallback;
    }

    private string SerializeGridLength(GridLength length)
    {
        if (length.IsStar) return $"{length.Value}*";
        if (length.IsAbsolute) return length.Value.ToString();
        return "Auto";
    }

    private void Splitter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Primitives.Thumb thumb)
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (prop != null)
            {
                prop.SetValue(thumb, Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast));
            }
        }
    }

    private void TreeSplitter_DragDelta(object sender, Microsoft.UI.Xaml.Controls.Primitives.DragDeltaEventArgs e)
    {
        var col = RootGrid.ColumnDefinitions[0];
        double newWidth = col.ActualWidth + e.HorizontalChange;
        if (newWidth > 50) // Minimum width
        {
            col.Width = new GridLength(newWidth, GridUnitType.Pixel);
            // Change the middle column to Star sizing if it was fixed, so that it can absorb space
            if (RootGrid.ColumnDefinitions[2].Width.IsAbsolute)
            {
                RootGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }
    }

    private void PreviewSplitter_DragDelta(object sender, Microsoft.UI.Xaml.Controls.Primitives.DragDeltaEventArgs e)
    {
        var col = RootGrid.ColumnDefinitions[4];
        double newWidth = col.ActualWidth - e.HorizontalChange;
        if (newWidth > 50) // Minimum width
        {
            col.Width = new GridLength(newWidth, GridUnitType.Pixel);
            if (RootGrid.ColumnDefinitions[2].Width.IsAbsolute)
            {
                RootGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Load();
        
        if (!double.IsNaN(settings.WindowWidth) && settings.WindowWidth > 0 &&
            !double.IsNaN(settings.WindowHeight) && settings.WindowHeight > 0)
        {
            _appWindow.Resize(new Windows.Graphics.SizeInt32((int)settings.WindowWidth, (int)settings.WindowHeight));
        }
        
        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            _appWindow.Move(new Windows.Graphics.PointInt32((int)settings.WindowLeft, (int)settings.WindowTop));
        }

        if (settings.WindowState == 2) // Maximized
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        RootGrid.ColumnDefinitions[0].Width = ParseGridLength(settings.TreeColumnWidth, new GridLength(1, GridUnitType.Star));
        RootGrid.ColumnDefinitions[2].Width = ParseGridLength(settings.ThumbnailsColumnWidth, new GridLength(2, GridUnitType.Star));
        RootGrid.ColumnDefinitions[4].Width = ParseGridLength(settings.PreviewColumnWidth, new GridLength(1, GridUnitType.Star));
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        var settings = _settingsService.Load();
        
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            settings.WindowState = presenter.State == OverlappedPresenterState.Maximized ? 2 : 0;
            settings.WindowWidth = _appWindow.Size.Width;
            settings.WindowHeight = _appWindow.Size.Height;
            settings.WindowLeft = _appWindow.Position.X;
            settings.WindowTop = _appWindow.Position.Y;
        }
        
        settings.TreeColumnWidth = SerializeGridLength(RootGrid.ColumnDefinitions[0].Width);
        settings.ThumbnailsColumnWidth = SerializeGridLength(RootGrid.ColumnDefinitions[2].Width);
        settings.PreviewColumnWidth = SerializeGridLength(RootGrid.ColumnDefinitions[4].Width);

        _settingsService.Save(settings);
    }

    private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ViewModels.DirectoryNodeViewModel node)
        {
            await ViewModel.SelectFolderFromTreeAsync(node.FullPath);
        }
    }
}