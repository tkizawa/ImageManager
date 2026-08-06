using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System;
using WinRT.Interop;
using Microsoft.UI;

namespace ImageManager;

public partial class MainWindow : Window
{
    private readonly Services.ISettingsService _settingsService = null!;
    private readonly Services.ImageClassifierService _classifierService = new();
    public ViewModels.MainViewModel ViewModel { get; } = null!;
    private AppWindow _appWindow = null!;

    private ScrollViewer? _thumbnailScrollViewer;
    private bool _isThumbnailPointerDown;
    private bool _isThumbnailDragScrolling;
    private Windows.Foundation.Point _thumbnailStartPoint;
    private Windows.Foundation.Point _thumbnailLastPoint;

    public MainWindow()
    {
        try {
            InitializeComponent();
            
            // Get AppWindow
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            _appWindow.Title = "WoodStream ImageManager";
            _appWindow.SetIcon("Assets\\AppIcon.ico");
        } catch (System.Exception ex) {
            System.IO.File.WriteAllText("crash_main.log", ex.ToString());
        }
    }

    public MainWindow(Services.ISettingsService settingsService, ViewModels.MainViewModel mainViewModel) : this()
    {
        _settingsService = settingsService;
        ViewModel = mainViewModel;
        RootGrid.DataContext = ViewModel;

        RootGrid.Loaded += RootGrid_Loaded;
        this.Closed += MainWindow_Closed;

        // Handle Ctrl + Wheel
        RootGrid.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerWheelChanged), true);
        
        // Mouse drag scrolling for ThumbnailGridView
        ThumbnailGridView.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerPressed), true);
        ThumbnailGridView.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerMoved), true);
        ThumbnailGridView.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerReleased), true);
        ThumbnailGridView.AddHandler(UIElement.PointerCanceledEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerReleased), true);
        ThumbnailGridView.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerReleased), true);
        
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

        ViewModel.ShowMessageRequested += async (s, e) =>
        {
            try
            {
                var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                string title = resourceLoader.GetString(e.titleKey);
                string message = resourceLoader.GetString(e.messageKey);

                var dialog = new ContentDialog
                {
                    Title = string.IsNullOrEmpty(title) ? e.titleKey : title,
                    Content = string.IsNullOrEmpty(message) ? e.messageKey : message,
                    CloseButtonText = "OK",
                    XamlRoot = RootGrid.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }
            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
            {
                return childOfChild;
            }
        }
        return null;
    }

    private void ThumbnailGridView_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ThumbnailGridView);
        if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _isThumbnailPointerDown = true;
            _thumbnailStartPoint = point.Position;
            _thumbnailLastPoint = point.Position;
            _isThumbnailDragScrolling = false;

            _thumbnailScrollViewer ??= FindVisualChild<ScrollViewer>(ThumbnailGridView);
        }
    }

    private void ThumbnailGridView_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isThumbnailPointerDown) return;

        _thumbnailScrollViewer ??= FindVisualChild<ScrollViewer>(ThumbnailGridView);
        if (_thumbnailScrollViewer == null) return;

        var point = e.GetCurrentPoint(ThumbnailGridView);
        var currentPosition = point.Position;

        double deltaX = currentPosition.X - _thumbnailStartPoint.X;
        double deltaY = currentPosition.Y - _thumbnailStartPoint.Y;

        if (!_isThumbnailDragScrolling)
        {
            if (System.Math.Abs(deltaX) > 5 || System.Math.Abs(deltaY) > 5)
            {
                _isThumbnailDragScrolling = true;
                ThumbnailGridView.CapturePointer(e.Pointer);

                var prop = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                prop?.SetValue(ThumbnailGridView, Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand));
            }
        }

        if (_isThumbnailDragScrolling)
        {
            double stepX = currentPosition.X - _thumbnailLastPoint.X;
            double stepY = currentPosition.Y - _thumbnailLastPoint.Y;

            double targetVertical = _thumbnailScrollViewer.VerticalOffset - stepY;
            double targetHorizontal = _thumbnailScrollViewer.HorizontalOffset - stepX;

            _thumbnailScrollViewer.ChangeView(targetHorizontal, targetVertical, null, disableAnimation: true);
            _thumbnailLastPoint = currentPosition;

            e.Handled = true;
        }
    }

    private void ThumbnailGridView_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isThumbnailDragScrolling)
        {
            ThumbnailGridView.ReleasePointerCapture(e.Pointer);

            var prop = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            prop?.SetValue(ThumbnailGridView, null);

            e.Handled = true;
        }

        _isThumbnailPointerDown = false;
        _isThumbnailDragScrolling = false;
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
        if (sender is MenuFlyoutItem item && item.Tag != null)
        {
            ViewModel.AddFavoriteFolderCommand.Execute(item.Tag);
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

    private void HistoryListView_ItemClick(object sender, Microsoft.UI.Xaml.Controls.ItemClickEventArgs e)
    {
        if (e.ClickedItem is string folderPath)
        {
            if (ViewModel.SelectHistoryFolderCommand.CanExecute(folderPath))
            {
                ViewModel.SelectHistoryFolderCommand.Execute(folderPath);
            }
        }
    }

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string folderPath)
        {
            ViewModel.SelectHistoryFolderCommand.Execute(folderPath);
        }
    }

    private void RemoveHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string folderPath)
        {
            ViewModel.RemoveHistoryFolderCommand.Execute(folderPath);
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

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is MenuFlyoutItem item && item.Tag is Models.ImageFile imageFile)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{imageFile.FilePath}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show in explorer: {ex}");
        }
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

        settings.ThumbnailSize = ViewModel.ThumbnailSize;
        settings.SortFieldIndex = ViewModel.SortFieldIndex;
        settings.SortDirectionIndex = ViewModel.SortDirectionIndex;
        if (ViewModel.SelectedImage != null)
        {
            settings.SelectedImageFilePath = ViewModel.SelectedImage.FilePath;
        }

        _settingsService.Save(settings);
    }

    private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ViewModels.DirectoryNodeViewModel node)
        {
            await ViewModel.SelectFolderFromTreeAsync(node.FullPath);
        }
    }

    private async void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var isJa = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);

            var dialog = new ContentDialog
            {
                Title = isJa ? "操作説明" : "User Guide",
                CloseButtonText = isJa ? "閉じる" : "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
                MaxWidth = 650
            };

            var container = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 480,
                Content = CreateHelpContent(isJa)
            };

            dialog.Content = container;
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("crash.log", ex.ToString());
        }
    }

    private async void ClassifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Images.Count == 0)
        {
            var dialog = new ContentDialog
            {
                Title = "AI自動分類",
                Content = "分類対象の画像が読み込まれていません。まずフォルダを選択してください。",
                CloseButtonText = "OK",
                XamlRoot = RootGrid.XamlRoot
            };
            await dialog.ShowAsync();
            return;
        }

        var classifyDialog = new ClassifyDialog(_classifierService, ViewModel.Images, ViewModel.CurrentFolderPath)
        {
            XamlRoot = RootGrid.XamlRoot
        };

        await classifyDialog.ShowAsync();
    }

    private UIElement CreateHelpContent(bool isJa)
    {
        var stack = new StackPanel { Spacing = 16, Margin = new Thickness(0, 8, 16, 8) };

        if (isJa)
        {
            stack.Children.Add(CreateHelpSection("📁 フォルダナビゲーション", new[]
            {
                "【ツリータブ】: ドライブやフォルダを階層表示。「フォルダを開く...」ボタンからも直接選択可能。",
                "【お気に入りタブ】: よく使うフォルダを登録。ツリーや履歴の右クリックメニュー「お気に入りに追加」から追加。",
                "【履歴タブ】: 直近で開いたフォルダ（最大20件）を自動記録。ワンクリックで移動、右クリックで削除。"
            }));

            stack.Children.Add(CreateHelpSection("🖼️ サムネイル表示とズーム", new[]
            {
                "【サムネイル表示】: 選択したフォルダ内の画像ファイルをグリッド一覧表示。",
                "【ズーム操作】: 「Ctrl + マウスホイール」でサムネイルの拡大・縮小が可能。",
                "【並び替え】: 「更新日」または「撮影日」順、昇順／降順でソート切り替え。"
            }));

            stack.Children.Add(CreateHelpSection("🖱️ 右クリック操作", new[]
            {
                "【Windowsのフォトで開く】: Windows標準の「フォト」アプリで全画面閲覧・編集。",
                "【エクスプローラーで表示】: Windowsのエクスプローラーを起動し、対象ファイルを選択。"
            }));

            stack.Children.Add(CreateHelpSection("📊 プレビューとEXIF情報", new[]
            {
                "【拡大プレビュー】: サムネイルを選択すると画面右側に拡大画像を表示。",
                "【EXIFメタデータ】: ファイルサイズ・作成日に加え、撮影日、カメラモデル、レンズ、F値、ISO、露出時間を表示。"
            }));

            stack.Children.Add(CreateHelpSection("🪟 画像ビュアー (別ウィンドウ)", new[]
            {
                "【起動】: サムネイルをダブルクリックすると別ウィンドウで開きます。",
                "【画像切り替え】: 左右のカーソルキー、またはマウスのホイール。",
                "【拡大縮小】: Ctrlキーを押しながらマウスのホイール。",
                "【移動】: マウスのドラッグアンドドロップで画像表示を移動。",
                "【閉じる】: Escキー。"
            }));

            stack.Children.Add(CreateHelpSection("🤖 AI自動分類", new[]
            {
                "【AI自動分類】: ヘッダーの「AI自動分類」ボタンからダイアログを起動。",
                "【自動判別・分類】: ONNX Runtime（GPU/CPU）または色調・特徴解析により画像を主要カテゴリー（人物・風景・動物・食べ物・乗り物・建物・文書）に判定し、「サブフォルダにコピー」「サブフォルダに移動」「タグ付与のみ」を選択して一括処理。"
            }));

            stack.Children.Add(CreateHelpSection("⚙️ 設定のインポート / エクスポート", new[]
            {
                "【設定メニュー】: 画面左上の「設定（歯車）」ボタンからドロップダウンメニューを開きます。",
                "【バックアップ・復元】: 「設定のエクスポート...」「設定のインポート...」でお気に入りや履歴をファイルとして保存・復元。"
            }));
        }
        else
        {
            stack.Children.Add(CreateHelpSection("📁 Folder Navigation", new[]
            {
                "[Folders Tab]: View drive and folder hierarchies. Or click 'Open Folder...' to select a directory.",
                "[Favorites Tab]: Access saved favorite folders. Right-click folders in Tree or History to add.",
                "[History Tab]: Auto-saves recently opened folders (up to 20). Click to reopen, right-click to remove."
            }));

            stack.Children.Add(CreateHelpSection("🖼️ Thumbnails & Zoom", new[]
            {
                "[Thumbnails]: Displays all supported image files in the current folder.",
                "[Zoom]: Press Ctrl + Mouse Wheel over the list to resize thumbnail icons.",
                "[Sorting]: Sort images by Modified Date or Date Taken (Ascending / Descending)."
            }));

            stack.Children.Add(CreateHelpSection("🖱️ Context Menu", new[]
            {
                "[Open with Windows Photos]: Opens the file in default Windows Photos viewer.",
                "[Show in Explorer]: Opens Windows File Explorer with the selected item highlighted."
            }));

            stack.Children.Add(CreateHelpSection("📊 Preview & EXIF Metadata", new[]
            {
                "[Preview Pane]: Click any thumbnail to preview the image on the right side.",
                "[EXIF Properties]: View file size, date modified, camera, lens, F-stop, exposure time, ISO, and focal length."
            }));

            stack.Children.Add(CreateHelpSection("🪟 Image Viewer (Separate Window)", new[]
            {
                "[Open]: Double-click a thumbnail to open in a separate window.",
                "[Navigate]: Left/Right arrow keys, or mouse wheel.",
                "[Zoom]: Hold Ctrl + Mouse wheel.",
                "[Pan]: Mouse drag and drop to move the image.",
                "[Close]: Esc key."
            }));

            stack.Children.Add(CreateHelpSection("🤖 AI Auto Classification", new[]
            {
                "[AI Classification]: Click the 'AI自動分類' button in the toolbar.",
                "[Auto Categorize]: Classify images into categories (People, Landscape, Animal, Food, Vehicle, Building, Document) using ONNX Runtime or heuristic feature analysis."
            }));

            stack.Children.Add(CreateHelpSection("⚙️ Import / Export Settings", new[]
            {
                "[Settings Menu]: Click the gear icon in the top header bar to access setting actions.",
                "[Backup & Restore]: Export or import your user settings (Favorites & History) as a JSON file."
            }));
        }

        return stack;
    }

    private StackPanel CreateHelpSection(string title, string[] items)
    {
        var section = new StackPanel { Spacing = 6 };

        Brush headerBrush;
        if (Application.Current.Resources.TryGetValue("SystemControlHighlightAccentBrush", out var brushObj) && brushObj is Brush b)
        {
            headerBrush = b;
        }
        else
        {
            headerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));
        }

        section.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = headerBrush
        });

        foreach (var item in items)
        {
            section.Children.Add(new TextBlock
            {
                Text = "• " + item,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(8, 0, 0, 0)
            });
        }

        return section;
    }

    #region Library Tab Handlers

    private async void NewLibrary_Click(object sender, RoutedEventArgs e)
    {
        var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        string dialogTitle = resourceLoader.GetString("NewLibraryDialogTitle");
        if (string.IsNullOrEmpty(dialogTitle)) dialogTitle = "新規ライブラリ作成";

        string placeholder = resourceLoader.GetString("LibraryNamePlaceholder");
        if (string.IsNullOrEmpty(placeholder)) placeholder = "ライブラリ名を入力";

        string okText = resourceLoader.GetString("OKText");
        if (string.IsNullOrEmpty(okText)) okText = "OK";

        string cancelText = resourceLoader.GetString("CancelText");
        if (string.IsNullOrEmpty(cancelText)) cancelText = "キャンセル";

        var inputTextBox = new TextBox
        {
            PlaceholderText = placeholder,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var dialog = new ContentDialog
        {
            Title = dialogTitle,
            Content = inputTextBox,
            PrimaryButtonText = okText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
        {
            ViewModel.CreateLibrary(inputTextBox.Text.Trim());
        }
    }

    private async void LibraryTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ViewModels.LibraryNodeViewModel node)
        {
            if (!node.IsLibrary && !string.IsNullOrEmpty(node.FullPath))
            {
                await ViewModel.SelectFolderFromTreeAsync(node.FullPath);
            }
        }
    }

    private void LibraryNode_ContextFlyout_Opening(object sender, object e)
    {
        if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement element && element.DataContext is ViewModels.LibraryNodeViewModel node)
        {
            foreach (var item in flyout.Items)
            {
                if (item is MenuFlyoutItem menuItem)
                {
                    if (menuItem.Name == "AddFolderMenuItem" || menuItem.Name == "RenameLibraryMenuItem" || menuItem.Name == "DeleteLibraryMenuItem")
                    {
                        menuItem.Visibility = node.IsLibrary ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else if (menuItem.Name == "RemoveFolderMenuItem")
                    {
                        menuItem.Visibility = node.IsTopLevelFolder ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
        }
    }

    private async void AddFolderToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewModels.LibraryNodeViewModel libraryNode && libraryNode.IsLibrary)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ViewModel.AddFolderToLibrary(libraryNode, folder.Path);
            }
        }
    }

    private async void RenameLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewModels.LibraryNodeViewModel libraryNode && libraryNode.IsLibrary)
        {
            var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            string dialogTitle = resourceLoader.GetString("RenameLibraryDialogTitle");
            if (string.IsNullOrEmpty(dialogTitle)) dialogTitle = "ライブラリ名の変更";

            string okText = resourceLoader.GetString("OKText");
            if (string.IsNullOrEmpty(okText)) okText = "OK";

            string cancelText = resourceLoader.GetString("CancelText");
            if (string.IsNullOrEmpty(cancelText)) cancelText = "キャンセル";

            var inputTextBox = new TextBox
            {
                Text = libraryNode.Name,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var dialog = new ContentDialog
            {
                Title = dialogTitle,
                Content = inputTextBox,
                PrimaryButtonText = okText,
                CloseButtonText = cancelText,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                ViewModel.RenameLibrary(libraryNode, inputTextBox.Text.Trim());
            }
        }
    }

    private void DeleteLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewModels.LibraryNodeViewModel libraryNode && libraryNode.IsLibrary)
        {
            ViewModel.DeleteLibrary(libraryNode);
        }
    }

    private void RemoveFolderFromLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ViewModels.LibraryNodeViewModel folderNode && !folderNode.IsLibrary)
        {
            ViewModel.RemoveFolderFromLibrary(folderNode);
        }
    }

    #endregion
}