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
        
        ViewModel.FolderSelectedEvent += async (s, node) => 
        {
            await System.Threading.Tasks.Task.Delay(100);
            FolderTreeView.SelectedItem = node;

            // Retry for up to 10 times to let the UI generate the TreeViewItem
            for (int i = 0; i < 10; i++)
            {
                FolderTreeView.UpdateLayout();
                var container = FolderTreeView.ContainerFromItem(node) as TreeViewItem;
                if (container != null)
                {
                    // Focus and scroll into view
                    container.Focus(FocusState.Programmatic);
                    container.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 });
                    break;
                }
                await System.Threading.Tasks.Task.Delay(200);
            }
        };
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

    private async void ThumbnailGridView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ThumbnailGridView.SelectedItem is Models.ImageFile selectedImage)
        {
            e.Handled = true;

            var imageWindow = new ImageWindow(ViewModel, _settingsService);
            IntPtr mainHWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            IntPtr imageHWnd = WinRT.Interop.WindowNative.GetWindowHandle(imageWindow);

            // メインウィンドウを無効化（モーダル動作）
            EnableWindow(mainHWnd, false);

            imageWindow.Closed += (s, ev) => 
            {
                // ウィンドウが閉じたらメインウィンドウを有効化し、最前面に戻す
                EnableWindow(mainHWnd, true);
                SetForegroundWindow(mainHWnd);
            };

            // メインウィンドウでのイベント伝播とフォーカス処理が完全に終了するのを待つ
            await System.Threading.Tasks.Task.Delay(50);

            imageWindow.Activate();
            
            // 別ウィンドウを強制的に最前面に出す
            SetWindowPos(imageHWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetWindowPos(imageHWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(imageHWnd);
        }
    }

    private void FavoriteListView_ItemClick(object sender, Microsoft.UI.Xaml.Controls.ItemClickEventArgs e)
    {
        if (e.ClickedItem is string folderPath)
        {
            if (ViewModel.SelectFavoriteFolderCommand.CanExecute(folderPath))
            {
                ViewModel.SelectFavoriteFolderCommand.Execute(folderPath);
            }
        }
    }

    private void AddFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is ViewModels.DirectoryNodeViewModel node)
        {
            ViewModel.AddFavoriteFolderCommand.Execute(node);
        }
    }

    private void OpenFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string folderPath)
        {
            ViewModel.SelectFavoriteFolderCommand.Execute(folderPath);
        }
    }

    private void RemoveFavoriteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string folderPath)
        {
            ViewModel.RemoveFavoriteFolderCommand.Execute(folderPath);
        }
    }

    private async void OpenWithPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is Models.ImageFile imageFile)
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(imageFile.FilePath);
                var options = new Windows.System.LauncherOptions
                {
                    TargetApplicationPackageFamilyName = "Microsoft.Windows.Photos_8wekyb3d8bbwe"
                };
                await Windows.System.Launcher.LaunchFileAsync(file, options);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open with Photos: {ex.Message}");
            }
        }
    }

    private void CopyFilePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is Models.ImageFile imageFile)
        {
            string path = imageFile.FilePath;
            string url = TryGetSharePointOrOneDriveUrl(path);
            
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(url ?? path);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
    }

    private string TryGetSharePointOrOneDriveUrl(string localPath)
    {
        try
        {
            string registryKey = @"Software\SyncEngines\Providers\OneDrive";
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryKey))
            {
                if (key != null)
                {
                    foreach (string subkeyName in key.GetSubKeyNames())
                    {
                        using (var subkey = key.OpenSubKey(subkeyName))
                        {
                            if (subkey != null)
                            {
                                string mountPoint = subkey.GetValue("MountPoint") as string;
                                string urlNamespace = subkey.GetValue("UrlNamespace") as string;

                                if (!string.IsNullOrEmpty(mountPoint) && !string.IsNullOrEmpty(urlNamespace) &&
                                    localPath.StartsWith(mountPoint, System.StringComparison.OrdinalIgnoreCase))
                                {
                                    string relativePath = localPath.Substring(mountPoint.Length).Replace('\\', '/');
                                    if (relativePath.StartsWith("/"))
                                    {
                                        relativePath = relativePath.Substring(1);
                                    }
                                    
                                    string baseUrl = urlNamespace;
                                    if (!baseUrl.EndsWith("/"))
                                    {
                                        baseUrl += "/";
                                    }

                                    if (baseUrl.Equals("https://d.docs.live.net/", System.StringComparison.OrdinalIgnoreCase))
                                    {
                                        string cid = subkey.GetValue("CID") as string;
                                        if (!string.IsNullOrEmpty(cid))
                                        {
                                            baseUrl += cid + "/";
                                        }
                                    }

                                    string escapedPath = "";
                                    string[] parts = relativePath.Split('/');
                                    for (int i = 0; i < parts.Length; i++)
                                    {
                                        escapedPath += System.Uri.EscapeDataString(parts[i]);
                                        if (i < parts.Length - 1) escapedPath += "/";
                                    }

                                    return baseUrl + escapedPath;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore registry errors
        }

        return localPath;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

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

        _ = ViewModel.InitializeAsync();
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