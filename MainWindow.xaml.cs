using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinRT.Interop;
using Microsoft.UI;

namespace ImageManager;

/// <summary>
/// アプリケーションのメインウィンドウクラス。
/// 
/// 主な機能と役割：
/// 1. ウィンドウ位置・サイズ・最大化状態および列幅（グリッドスプリッター）の保存・復元（プロジェクト規約準拠）
/// 2. アプリ終了時のキャッシュ自動クリーンアップ処理（バックグラウンド実行）
/// 3. サムネイル一覧のキーボード操作（Ctrl+C/X/V, Delete, F, 0〜5, Ctrl+A 等）およびマウスマウスドラッグスクロール
/// 4. モーダルライクなプレビューウィンドウ（ImageWindow）の生成とウィンドウフォーカス制御
/// 5. フォルダツリー、お気に入り、履歴、ライブラリ管理、外部アプリケーション連携
/// </summary>
public partial class MainWindow : Window
{
    private readonly Services.ISettingsService _settingsService = null!;
    private readonly Services.ImageClassifierService _classifierService = new();

    /// <summary>バインドされているメインViewModelインスタンスを取得します。</summary>
    public ViewModels.MainViewModel ViewModel { get; } = null!;
    private AppWindow _appWindow = null!;

    private ScrollViewer? _thumbnailScrollViewer;
    private bool _isThumbnailPointerDown;
    private bool _isThumbnailDragScrolling;
    private Windows.Foundation.Point _thumbnailStartPoint;
    private Windows.Foundation.Point _thumbnailLastPoint;
    private bool _isClosingHandled;
    private bool _isCleaningOnExit;

    /// <summary>
    /// <see cref="MainWindow"/> クラスのデフォルトコンストラクタ。
    /// ウィンドウタイトルの設定、アイコンの読み込み、および終了イベントの購読を行います。
    /// </summary>
    public MainWindow()
    {
        try {
            InitializeComponent();
            
            // WinUI 3 AppWindow インスタンスの取得
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            _appWindow.Title = "WoodStream ImageManager";
            _appWindow.SetIcon("Assets\\AppIcon.ico");
            _appWindow.Closing += AppWindow_Closing;
        } catch (System.Exception ex) {
            Services.AppLogService.LogException("MainWindow.Constructor", ex);
        }
    }

    /// <summary>
    /// 依存サービスおよびViewModelを注入して <see cref="MainWindow"/> を初期化します。
    /// </summary>
    /// <param name="settingsService">設定管理サービス</param>
    /// <param name="mainViewModel">メインViewModel</param>
    public MainWindow(Services.ISettingsService settingsService, ViewModels.MainViewModel mainViewModel) : this()
    {
        _settingsService = settingsService;
        ViewModel = mainViewModel;
        RootGrid.DataContext = ViewModel;

        RootGrid.Loaded += RootGrid_Loaded;
        this.Closed += MainWindow_Closed;

        // キーボードショートカットイベントの登録
        RootGrid.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(RootGrid_KeyDown), true);

        // Ctrl + ホイールによるサムネイルサイズ拡大・縮小
        RootGrid.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerWheelChanged), true);
        
        // サムネイルグリッドのマウスドラッグによるスクロール操作
        ThumbnailGridView.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerPressed), true);
        ThumbnailGridView.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerMoved), true);
        ThumbnailGridView.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerReleased), true);
        ThumbnailGridView.AddHandler(UIElement.PointerCanceledEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerReleased), true);
        ThumbnailGridView.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(ThumbnailGridView_PointerReleased), true);
        
        // ツリービューで選択されたフォルダノードへのフォーカス＆スクロール追従
        ViewModel.FolderSelectedEvent += async (s, node) => 
        {
            await System.Threading.Tasks.Task.Delay(100);
            FolderTreeView.SelectedItem = node;

            // UI生成遅延に対応するため最大10回リトライ
            for (int i = 0; i < 10; i++)
            {
                FolderTreeView.UpdateLayout();
                var container = FolderTreeView.ContainerFromItem(node) as TreeViewItem;
                if (container != null)
                {
                    container.Focus(FocusState.Programmatic);
                    container.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 });
                    break;
                }
                await System.Threading.Tasks.Task.Delay(200);
            }
        };

        // メッセージダイアログ表示イベント（ローカライズキー対応）
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

        // 直接テキスト指定によるメッセージダイアログ表示イベント
        ViewModel.ShowDirectMessageRequested += async (s, e) =>
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = e.title,
                    Content = e.message,
                    CloseButtonText = "OK",
                    XamlRoot = RootGrid.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
        };
    }

    /// <summary>
    /// Ctrl+マウスホイールによるサムネイルサイズの動的リサイズハンドラ。
    /// </summary>
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

    /// <summary>
    /// VisualTreeから指定された型の最初の子要素を再帰的に探索します。
    /// </summary>
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

    /// <summary>サムネイルグリッド上でのマウス押下（ドラッグスクロール開始判定）</summary>
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

    /// <summary>サムネイルグリッド上でのマウス移動（ドラッグスクロール実行）</summary>
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

    /// <summary>サムネイルグリッド上でのマウス解放（ドラッグスクロール終了）</summary>
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

    /// <summary>
    /// サムネイルダブルクリック時に別ウィンドウ（ImageWindow）をモーダル表示します。
    /// </summary>
    private async void ThumbnailGridView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ThumbnailGridView.SelectedItem is Models.ImageFile selectedImage)
        {
            e.Handled = true;

            var imageWindow = new ImageWindow(ViewModel, _settingsService);
            IntPtr mainHWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            IntPtr imageHWnd = WinRT.Interop.WindowNative.GetWindowHandle(imageWindow);

            // メインウィンドウを一時的に無効化（モーダルライク動作）
            EnableWindow(mainHWnd, false);

            imageWindow.Closed += (s, ev) => 
            {
                // プレビューウィンドウ終了時にメインウィンドウを再有効化しフォーカス復元
                EnableWindow(mainHWnd, true);
                SetForegroundWindow(mainHWnd);
            };

            await System.Threading.Tasks.Task.Delay(50);

            imageWindow.Activate();
            
            // プレビューウィンドウを最前面にアクティベート
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

    private void ThumbnailGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedList = ThumbnailGridView.SelectedItems.OfType<Models.ImageFile>().ToList();
        ViewModel.UpdateSelectedImages(selectedList);
    }

    private async void ConfigureExternalApps_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExternalAppsDialog(_settingsService, RootGrid.XamlRoot);
        await dialog.ShowAsync();
    }

    private async void ConfigureCache_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CacheManagementDialog(_settingsService, RootGrid.XamlRoot);
        await dialog.ShowAsync();
    }

    private void Thumbnail_ContextFlyout_Opening(object sender, object e)
    {
        if (sender is MenuFlyout flyout && flyout.Target is FrameworkElement element && element.DataContext is Models.ImageFile clickedImage)
        {
            if (!ThumbnailGridView.SelectedItems.Contains(clickedImage))
            {
                ThumbnailGridView.SelectedItem = clickedImage;
            }

            var subMenu = flyout.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(i => i.Name == "ExternalAppsSubMenu" || i.Text.Contains("外部プログラム") || i.Text.Contains("External App"));
            if (subMenu != null)
            {
                PopulateExternalAppsMenu(subMenu, clickedImage);
            }
        }
    }

    private void PopulateExternalAppsMenu(MenuFlyoutSubItem subMenu, Models.ImageFile imageFile)
    {
        subMenu.Items.Clear();
        var settings = _settingsService.Load();
        var apps = settings.ExternalApps ?? new List<Models.ExternalApp>();

        if (apps.Count > 0)
        {
            foreach (var app in apps)
            {
                var item = new MenuFlyoutItem
                {
                    Text = app.Name,
                    Tag = (app, imageFile)
                };
                item.Click += ExternalAppItem_Click;
                subMenu.Items.Add(item);
            }
            subMenu.Items.Add(new MenuFlyoutSeparator());
        }

        var configItem = new MenuFlyoutItem
        {
            Text = "外部プログラムの設定..."
        };
        configItem.Click += ConfigureExternalApps_Click;
        subMenu.Items.Add(configItem);
    }

    private void ExternalAppItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is (Models.ExternalApp app, Models.ImageFile imageFile))
        {
            LaunchExternalApp(app, imageFile.FilePath);
        }
    }

    /// <summary>
    /// 登録された外部アプリケーションを起動し、指定された画像ファイルパスを渡します。
    /// </summary>
    /// <param name="app">外部アプリ設定モデル</param>
    /// <param name="filePath">対象画像パス</param>
    public static void LaunchExternalApp(Models.ExternalApp app, string filePath)
    {
        if (string.IsNullOrEmpty(app.ExecutablePath) || string.IsNullOrEmpty(filePath)) return;

        try
        {
            string args = !string.IsNullOrEmpty(app.Arguments)
                ? app.Arguments.Replace("{path}", $"\"{filePath}\"")
                : $"\"{filePath}\"";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = app.ExecutablePath,
                Arguments = args,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to launch external app '{app.Name}': {ex.Message}");
        }
    }

    private async void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        await CopySelectedImagesAsync();
    }

    private async void CutSelected_Click(object sender, RoutedEventArgs e)
    {
        await CutSelectedImagesAsync();
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        await PasteImagesAsync();
    }

    private async void CopyToFolder_Click(object sender, RoutedEventArgs e)
    {
        await CopySelectedToFolderAsync();
    }

    private async void MoveToFolder_Click(object sender, RoutedEventArgs e)
    {
        await MoveSelectedToFolderAsync();
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedImagesWithConfirmationAsync();
    }

    private async Task CopySelectedImagesAsync()
    {
        var selected = ThumbnailGridView.SelectedItems.OfType<Models.ImageFile>().ToList();
        if (selected.Count == 0) return;

        ViewModel.CopySelectedToClipboard();

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            
            var storageFiles = new List<Windows.Storage.IStorageItem>();
            foreach (var img in selected)
            {
                if (System.IO.File.Exists(img.FilePath))
                {
                    try
                    {
                        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(img.FilePath);
                        storageFiles.Add(file);
                    }
                    catch { }
                }
            }

            if (storageFiles.Count > 0)
            {
                dataPackage.SetStorageItems(storageFiles);
            }
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch { }
    }

    private async Task CutSelectedImagesAsync()
    {
        var selected = ThumbnailGridView.SelectedItems.OfType<Models.ImageFile>().ToList();
        if (selected.Count == 0) return;

        ViewModel.CutSelectedToClipboard();

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            
            var storageFiles = new List<Windows.Storage.IStorageItem>();
            foreach (var img in selected)
            {
                if (System.IO.File.Exists(img.FilePath))
                {
                    try
                    {
                        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(img.FilePath);
                        storageFiles.Add(file);
                    }
                    catch { }
                }
            }

            if (storageFiles.Count > 0)
            {
                dataPackage.SetStorageItems(storageFiles);
            }
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch { }
    }

    private async Task PasteImagesAsync()
    {
        if (string.IsNullOrEmpty(ViewModel.CurrentFolderPath) || !System.IO.Directory.Exists(ViewModel.CurrentFolderPath))
            return;

        try
        {
            var clipContent = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (clipContent != null && clipContent.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await clipContent.GetStorageItemsAsync();
                var paths = items.Where(i => i is Windows.Storage.StorageFile).Select(i => i.Path).ToList();
                if (paths.Count > 0)
                {
                    bool isCut = clipContent.RequestedOperation == Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                    int count = isCut
                        ? await ViewModel.MoveFilesToFolderAsync(paths, ViewModel.CurrentFolderPath)
                        : await ViewModel.CopyFilesToFolderAsync(paths, ViewModel.CurrentFolderPath);

                    ShowNotification(isCut ? "MoveSuccessTitle" : "CopySuccessTitle", isCut ? "MoveSuccessMessage" : "CopySuccessMessage", count);
                    return;
                }
            }
        }
        catch { }

        if (ViewModel.ClipboardFilePaths.Count > 0)
        {
            bool wasCut = ViewModel.IsClipboardCut;
            int count = await ViewModel.PasteFromClipboardAsync();
            ShowNotification(wasCut ? "MoveSuccessTitle" : "CopySuccessTitle", wasCut ? "MoveSuccessMessage" : "CopySuccessMessage", count);
        }
    }

    private async Task CopySelectedToFolderAsync()
    {
        var selected = ThumbnailGridView.SelectedItems.OfType<Models.ImageFile>().Select(i => i.FilePath).ToList();
        if (selected.Count == 0) return;

        var targetFolder = await ViewModel.FileSystemService.SelectFolderAsync();
        if (!string.IsNullOrEmpty(targetFolder) && System.IO.Directory.Exists(targetFolder))
        {
            int count = await ViewModel.CopyFilesToFolderAsync(selected, targetFolder);
            ShowNotification("CopySuccessTitle", "CopySuccessMessage", count);
        }
    }

    private async Task MoveSelectedToFolderAsync()
    {
        var selected = ThumbnailGridView.SelectedItems.OfType<Models.ImageFile>().Select(i => i.FilePath).ToList();
        if (selected.Count == 0) return;

        var targetFolder = await ViewModel.FileSystemService.SelectFolderAsync();
        if (!string.IsNullOrEmpty(targetFolder) && System.IO.Directory.Exists(targetFolder))
        {
            int count = await ViewModel.MoveFilesToFolderAsync(selected, targetFolder);
            ShowNotification("MoveSuccessTitle", "MoveSuccessMessage", count);
        }
    }

    private async Task DeleteSelectedImagesWithConfirmationAsync()
    {
        var selected = ThumbnailGridView.SelectedItems.OfType<Models.ImageFile>().ToList();
        if (selected.Count == 0) return;

        var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        string title = resourceLoader.GetString("DeleteConfirmTitle");
        if (string.IsNullOrEmpty(title)) title = "画像の削除";

        string msgTemplate = resourceLoader.GetString("DeleteConfirmMessage");
        string msg = string.IsNullOrEmpty(msgTemplate) 
            ? $"選択した {selected.Count} 件の画像を削除しますか？" 
            : string.Format(msgTemplate, selected.Count);

        string okText = resourceLoader.GetString("OKText");
        if (string.IsNullOrEmpty(okText)) okText = "OK";
        string cancelText = resourceLoader.GetString("CancelText");
        if (string.IsNullOrEmpty(cancelText)) cancelText = "キャンセル";

        var dialog = new ContentDialog
        {
            Title = title,
            Content = msg,
            PrimaryButtonText = okText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            int count = await ViewModel.DeleteSelectedImagesAsync();
            ShowNotification("DeleteSuccessTitle", "DeleteSuccessMessage", count);
        }
    }

    private void ShowNotification(string titleKey, string msgKey, int count)
    {
        var resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        string title = resourceLoader.GetString(titleKey);
        string msgFormat = resourceLoader.GetString(msgKey);
        string msg = string.IsNullOrEmpty(msgFormat) ? $"{count} items processed." : string.Format(msgFormat, count);

        ViewModel.RaiseDirectMessage(string.IsNullOrEmpty(title) ? titleKey : title, msg);
    }

    /// <summary>
    /// メインウィンドウ全体のキーボードショートカット処理（Ctrl+C/X/V/A, Delete, F, 0〜5）
    /// </summary>
    private async void RootGrid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrlDown = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrlDown)
        {
            if (e.Key == Windows.System.VirtualKey.C)
            {
                if (ViewModel.HasAnyImageSelected)
                {
                    e.Handled = true;
                    await CopySelectedImagesAsync();
                }
            }
            else if (e.Key == Windows.System.VirtualKey.X)
            {
                if (ViewModel.HasAnyImageSelected)
                {
                    e.Handled = true;
                    await CutSelectedImagesAsync();
                }
            }
            else if (e.Key == Windows.System.VirtualKey.V)
            {
                if (ViewModel.CanPaste)
                {
                    e.Handled = true;
                    await PasteImagesAsync();
                }
            }
            else if (e.Key == Windows.System.VirtualKey.A)
            {
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootGrid.XamlRoot);
                if (focused is not TextBox)
                {
                    e.Handled = true;
                    ThumbnailGridView.SelectAll();
                }
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Delete)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootGrid.XamlRoot);
            if (focused is not TextBox && ViewModel.HasAnyImageSelected)
            {
                e.Handled = true;
                await DeleteSelectedImagesWithConfirmationAsync();
            }
        }
        else if (e.Key == Windows.System.VirtualKey.F)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootGrid.XamlRoot);
            if (focused is not TextBox && ViewModel.SelectedImage != null)
            {
                e.Handled = true;
                ViewModel.ToggleFavoriteCommand.Execute(ViewModel.SelectedImage);
            }
        }
        else if (e.Key >= Windows.System.VirtualKey.Number0 && e.Key <= Windows.System.VirtualKey.Number5)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootGrid.XamlRoot);
            if (focused is not TextBox && ViewModel.HasAnyImageSelected)
            {
                e.Handled = true;
                int rating = (int)(e.Key - Windows.System.VirtualKey.Number0);
                ViewModel.SetRatingCommand.Execute(rating);
            }
        }
        else if (e.Key >= Windows.System.VirtualKey.NumberPad0 && e.Key <= Windows.System.VirtualKey.NumberPad5)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootGrid.XamlRoot);
            if (focused is not TextBox && ViewModel.HasAnyImageSelected)
            {
                e.Handled = true;
                int rating = (int)(e.Key - Windows.System.VirtualKey.NumberPad0);
                ViewModel.SetRatingCommand.Execute(rating);
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

    #region Win32 API Interop
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
    #endregion

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
        if (newWidth > 50)
        {
            col.Width = new GridLength(newWidth, GridUnitType.Pixel);
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
        if (newWidth > 50)
        {
            col.Width = new GridLength(newWidth, GridUnitType.Pixel);
            if (RootGrid.ColumnDefinitions[2].Width.IsAbsolute)
            {
                RootGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }
    }

    /// <summary>
    /// メインUIロード時、保存されたウィンドウ位置・サイズ・最大化状態および列幅を復元します（プロジェクト規約準拠）。
    /// </summary>
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

        if (settings.WindowState == 2) // 最大化状態
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        // 各列の幅を復元
        RootGrid.ColumnDefinitions[0].Width = ParseGridLength(settings.TreeColumnWidth, new GridLength(1, GridUnitType.Star));
        RootGrid.ColumnDefinitions[2].Width = ParseGridLength(settings.ThumbnailsColumnWidth, new GridLength(2, GridUnitType.Star));
        RootGrid.ColumnDefinitions[4].Width = ParseGridLength(settings.PreviewColumnWidth, new GridLength(1, GridUnitType.Star));

        // ナビゲーションタブの配置順と選択状態を復元
        RestoreNavigationTabOrder(settings);

        _ = ViewModel.InitializeAsync();
    }

    /// <summary>
    /// ナビゲーションタブの配置順および選択状態を設定から復元します。
    /// </summary>
    private void RestoreNavigationTabOrder(Models.AppSettings settings)
    {
        try
        {
            if (settings.NavigationTabOrder != null && settings.NavigationTabOrder.Count > 0)
            {
                var tabMap = new Dictionary<string, TabViewItem>();
                foreach (var item in NavigationTabView.TabItems.OfType<TabViewItem>())
                {
                    if (item.Tag is string tag)
                    {
                        tabMap[tag] = item;
                    }
                }

                var orderedItems = new List<TabViewItem>();
                foreach (var tag in settings.NavigationTabOrder)
                {
                    if (tabMap.TryGetValue(tag, out var tabItem))
                    {
                        orderedItems.Add(tabItem);
                        tabMap.Remove(tag);
                    }
                }
                // リストに含まれていなかったタブを末尾に追加
                foreach (var remaining in tabMap.Values)
                {
                    orderedItems.Add(remaining);
                }

                NavigationTabView.TabItems.Clear();
                foreach (var tab in orderedItems)
                {
                    NavigationTabView.TabItems.Add(tab);
                }
            }

            // 前回選択タブの復元
            if (!string.IsNullOrEmpty(settings.SelectedNavigationTab))
            {
                var selectedTab = NavigationTabView.TabItems.OfType<TabViewItem>().FirstOrDefault(t => (t.Tag as string) == settings.SelectedNavigationTab);
                if (selectedTab != null)
                {
                    NavigationTabView.SelectedItem = selectedTab;
                }
            }
        }
        catch (Exception ex)
        {
            Services.AppLogService.LogException("RestoreNavigationTabOrder", ex);
        }
    }

    /// <summary>
    /// 現在のナビゲーションタブ配置順および選択状態を設定オブジェクトに反映します。
    /// </summary>
    private void SaveNavigationTabOrder(Models.AppSettings settings)
    {
        try
        {
            var order = new List<string>();
            foreach (var item in NavigationTabView.TabItems.OfType<TabViewItem>())
            {
                if (item.Tag is string tag)
                {
                    order.Add(tag);
                }
            }
            settings.NavigationTabOrder = order;

            if (NavigationTabView.SelectedItem is TabViewItem selectedTab && selectedTab.Tag is string selTag)
            {
                settings.SelectedNavigationTab = selTag;
            }
        }
        catch (Exception ex)
        {
            Services.AppLogService.LogException("SaveNavigationTabOrder", ex);
        }
    }

    /// <summary>
    /// タブのドラッグ並び替え完了時、新しい配置順を設定へ即座に保存します。
    /// </summary>
    private void NavigationTabView_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        var settings = _settingsService.Load();
        SaveNavigationTabOrder(settings);
        _settingsService.Save(settings);
    }

    /// <summary>
    /// ナビゲーションタブの選択変更時、選択されたタブを設定へ保存します。
    /// </summary>
    private void NavigationTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavigationTabView.SelectedItem is TabViewItem selectedTab && selectedTab.Tag is string selTag)
        {
            var settings = _settingsService.Load();
            if (settings.SelectedNavigationTab != selTag)
            {
                settings.SelectedNavigationTab = selTag;
                _settingsService.Save(settings);
            }
        }
    }

    /// <summary>
    /// ウィンドウ終了要求時ハンドラ。
    /// 自動クリーンアップ設定が有効な場合、クリーンアップを実行してからアプリを終了します。
    /// </summary>
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isClosingHandled) return;
        if (_isCleaningOnExit)
        {
            args.Cancel = true;
            return;
        }

        try
        {
            var settings = _settingsService.Load();
            if (settings.AutoCleanCacheOnExit && (settings.CacheCleanPeriodDays > 0 || settings.CacheCleanMaxSizeBytes > 0))
            {
                if (Services.ThumbnailCacheService.EvaluateCleanupRequired(settings.CacheCleanPeriodDays, settings.CacheCleanMaxSizeBytes))
                {
                    args.Cancel = true;
                    _isCleaningOnExit = true;

                    var cleanResult = await Services.ThumbnailCacheService.CleanCacheAsync(settings.CacheCleanPeriodDays, settings.CacheCleanMaxSizeBytes);
                    if (cleanResult.DeletedCount > 0 && RootGrid.XamlRoot != null)
                    {
                        var isJa = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);
                        var dialog = new ContentDialog
                        {
                            Title = isJa ? "キャッシュの自動削除" : "Automatic Cache Cleanup",
                            Content = isJa
                                ? $"終了時のキャッシュ自動削除を実行しました。\n\n削除ファイル数: {cleanResult.DeletedCount:N0} 件\n解放容量: {Services.ThumbnailCacheService.FormatBytes(cleanResult.FreedBytes)}"
                                : $"Automatic cache cleanup on exit has completed.\n\nDeleted files: {cleanResult.DeletedCount:N0}\nFreed space: {Services.ThumbnailCacheService.FormatBytes(cleanResult.FreedBytes)}",
                            CloseButtonText = "OK",
                            DefaultButton = ContentDialogButton.Close,
                            XamlRoot = RootGrid.XamlRoot
                        };
                        await dialog.ShowAsync();
                    }

                    _isClosingHandled = true;
                    this.Close();
                    return;
                }
            }
        }
        catch { }

        _isClosingHandled = true;
    }

    /// <summary>
    /// ウィンドウが閉じられた際、ウィンドウサイズ・位置・最大化状態・各ペイン列幅を設定へ保存します（プロジェクト規約準拠）。
    /// </summary>
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

        // タブ配置順と選択タブの保存
        SaveNavigationTabOrder(settings);

        _settingsService.Save(settings);
    }

    private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ViewModels.DirectoryNodeViewModel node)
        {
            await ViewModel.SelectFolderFromTreeAsync(node.FullPath);
        }
    }

    /// <summary>
    /// 操作説明ヘルプダイアログを表示します。
    /// </summary>
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
            Services.AppLogService.LogException("MainWindow.HelpButton_Click", ex);
        }
    }

    /// <summary>
    /// AI自動分類ダイアログを起動します。
    /// </summary>
    private async void ClassifyButton_Click(object sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            Services.AppLogService.LogException("MainWindow.ClassifyButton_Click", ex);
        }
    }

    /// <summary>
    /// 操作説明（ヘルプ）ダイアログのコンテンツUIツリーを動的に構築します。
    /// </summary>
    private UIElement CreateHelpContent(bool isJa)
    {
        var stack = new StackPanel { Spacing = 16, Margin = new Thickness(0, 8, 16, 8) };

        if (isJa)
        {
            stack.Children.Add(CreateHelpSection("📁 フォルダナビゲーション", new[]
            {
                "【ツリータブ】: ドライブやフォルダを階層表示。「フォルダを開く...」ボタンからも直接選択可能。",
                "【お気に入りタブ】: よく使うフォルダを登録。ツリーや履歴の右クリックメニュー「お気に入りに追加」から追加。",
                "【履歴タブ】: 直近で開いたフォルダ（最大20件）を自動記録。ワンクリックで移動、右クリックで削除。",
                "【ライブラリタブ】: 複数のフォルダを任意の名前でグループ化して管理。「新規ライブラリ」から作成し、フォルダを追加可能。フォルダが移動された場合は「フォルダを再選択...」から追跡を更新できます。"
            }));

            stack.Children.Add(CreateHelpSection("🖼️ サムネイル表示と選択・ズーム・スクロール", new[]
            {
                "【サムネイル表示】: 選択したフォルダ内の画像ファイルをグリッド一覧表示。",
                "【複数選択】: 「Ctrl + クリック」で複数画像を選択・解除。「Shift + クリック」で範囲選択、「Ctrl + A」で全選択。",
                "【ズーム操作】: 「Ctrl + マウスホイール」でサムネイルの拡大・縮小が可能。",
                "【ドラッグスクロール】: サムネイル一覧の余白や画像をマウスドラッグ（左クリックまたは中クリック）することでスムーズにスクロール操作が可能。",
                "【並び替え】: 「更新日」「撮影日」「レート」順、昇順／降順でソート切り替え。"
            }));

            stack.Children.Add(CreateHelpSection("⭐ お気に入り・レート機能と絞り込み", new[]
            {
                "【お気に入りの設定】: サムネイル右上の星アイコン、右クリック「お気に入りの切り替え」、またはビュアー表示中に「F」キーでON/OFFを切り替え。",
                "【レートの設定 (★1～5)】: 右ペインの星ボタン、右クリック「レートを設定」、キーボードの「0」～「5」キー（テンキー対応）で瞬時に設定・解除。複数選択時の一括設定にも対応。",
                "【レートフィルター】: ツールバーのドロップダウンから指定したレート（★1～★5、レートなし、すべて）の写真のみを抽出表示。",
                "【お気に入りとのAND条件】: 「お気に入りのみ」ボタンと「レートフィルター」を併用すると、お気に入り かつ 指定レート の両方を満たす写真のみを絞り込み表示。"
            }));

            stack.Children.Add(CreateHelpSection("📋 画像のコピー・移動・削除", new[]
            {
                "【フォルダへコピー/移動】: ツールバーまたは右クリックメニューから「フォルダへコピー...」「フォルダへ移動...」を選択して一括処理。",
                "【クリップボード】: 「Ctrl + C（コピー）」「Ctrl + X（切り取り）」「Ctrl + V（貼り付け）」に対応。Windowsエクスプローラーとの相互コピペも可能。",
                "【削除】: 選択した画像を「Delete」キーまたは右クリック「削除」から削除可能。"
            }));

            stack.Children.Add(CreateHelpSection("🖱️ 右クリック操作", new[]
            {
                "【レートを設定】: ★1～5およびレートなしをサブメニューから指定。",
                "【お気に入りの切り替え】: 選択画像のブックマーク登録・解除。",
                "【コピー/切り取り/貼り付け】: 選択中の画像を一括操作。",
                "【Windowsのフォトで開く】: Windows標準の「フォト」アプリで全画面閲覧・編集。",
                "【エクスプローラーで表示】: Windowsのエクスプローラーを起動し、対象ファイルを選択。"
            }));

            stack.Children.Add(CreateHelpSection("📊 プレビューとEXIF情報", new[]
            {
                "【単一選択プレビュー】: 画像を1枚選択すると画面右側に拡大画像、星レート操作UI、およびEXIFメタデータを表示。",
                "【複数選択時】: 複数画像選択時はプレビューを非表示にし、選択件数サマリーと一括レート設定・操作ボタンを表示。"
            }));

            stack.Children.Add(CreateHelpSection("🪟 画像ビュアー (別ウィンドウ)", new[]
            {
                "【起動】: サムネイルをダブルクリックすると別ウィンドウで開きます。",
                "【画像切り替え】: 左右のカーソルキー、またはマウスのホイール。",
                "【拡大縮小】: Ctrlキーを押しながらマウスのホイール。",
                "【移動】: マウスのドラッグアンドドロップで画像表示を移動。",
                "【お気に入り/レート操作】: 「F」キーでお気に入り切り替え、「0」～「5」キーでレート設定（右クリックメニューからも可能）。",
                "【情報表示の切り替え】: 右クリックメニュー、または「I」キーで写真情報（ファイル名・撮影日・お気に入り・レート・EXIF等）の表示・非表示を切り替え。",
                "【閉じる】: Escキー。"
            }));

            stack.Children.Add(CreateHelpSection("🤖 AI自動分類 (Ollama / ONNX DirectML / ルールベース)", new[]
            {
                "【AI自動分類】: ヘッダーの「AI自動分類」ボタンからダイアログを起動。",
                "【マルチAIエンジン対応】: OllamaローカルVLM（LLaVA / Moondream等）、ONNX DirectML（GPU加速/CPU）、または色調・特徴解析から判定エンジンを選択可能。",
                "【自動判別・分類】: 主要カテゴリー（人物・風景・動物・食べ物・乗り物・建物・文書）に判定し、「サブフォルダにコピー」「サブフォルダに移動」「タグ付与のみ」を選択して一括処理。"
            }));

            stack.Children.Add(CreateHelpSection("📷 RAW画像 (CR3 / ORF / NEF / ARW 等) の高画質プレビュー", new[]
            {
                "【フル解像度プレビュー】: Canon CR3、Olympus ORF、Nikon NEF、Sony ARW などの各社RAW画像からフルサイズの最高画質プレビューを自動抽出して高速表示。",
                "【縦横の自動回転】: カメラ本体のジャイロセンサー（Exif Orientation）を自動解析し、縦撮り写真を正しい縦向きに自動回転してサムネイルおよびビュアーに描画。"
            }));

            stack.Children.Add(CreateHelpSection("🎨 外部プログラム連携 (Photoshop / GIMP / ペイント等)", new[]
            {
                "【プログラムの登録】: 「設定（歯車）」メニューの「外部プログラムの設定...」から、よく使う画像編集ソフト（Photoshop、GIMP、Affinity Photo、ペイントなど）を自由に追加・管理。",
                "【外部アプリで開く】: サムネイルやビュアーの右クリックメニュー「外部プログラムで開く」から、登録したアプリを選択して瞬時に画像を渡して起動。"
            }));

            stack.Children.Add(CreateHelpSection("💾 サムネイルのキャッシュ管理", new[]
            {
                "【キャッシュの管理】: 「設定（歯車）」メニューの「キャッシュの管理...」から現在のキャッシュ使用量・ファイル数を確認。",
                "【一括削除】: 「キャッシュを一括削除」ボタンでディスクおよびメモリ上のサムネイルキャッシュを即時クリア。",
                "【終了時の自動削除】: 経過日数（7日、14日、30日、60日）または容量上限（1GB、5GB、10GB）を指定してアプリ終了時に自動クリーンアップ。"
            }));

            stack.Children.Add(CreateHelpSection("⚙️ 設定のインポート / エクスポート", new[]
            {
                "【設定メニュー】: 画面左上の「設定（歯車）」ボタンからドロップダウンメニューを開きます。",
                "【バックアップ・復元】: 「設定のエクスポート...」「設定のインポート...」でお気に入りや履歴、外部プログラム設定、データベースをファイルとして保存・復元。"
            }));

            // トラブルシューティングおよびログフォルダ参照ボタン
            var logSection = CreateHelpSection("📋 ログ・トラブルシューティング", new[]
            {
                "【ログの確認】: アプリの動作ログおよびクラッシュ情報は AppData\\Local\\ImageManager\\Logs に安全に記録されます。",
                "【問題の報告】: 不具合や予期せぬ終了が発生した場合は、ログフォルダ内の crash.log や app.log を添付してお問い合わせください。"
            });
            var openLogBtn = new Button
            {
                Content = "📁 ログフォルダを開く",
                Margin = new Thickness(0, 4, 0, 0)
            };
            openLogBtn.Click += (s, e) => Services.AppLogService.OpenLogFolder();
            logSection.Children.Add(openLogBtn);
            stack.Children.Add(logSection);
        }
        else
        {
            stack.Children.Add(CreateHelpSection("📁 Folder Navigation", new[]
            {
                "[Folders Tab]: View drive and folder hierarchies. Or click 'Open Folder...' to select a directory.",
                "[Favorites Tab]: Access saved favorite folders. Right-click folders in Tree or History to add.",
                "[History Tab]: Auto-saves recently opened folders (up to 20). Click to reopen, right-click to remove.",
                "[Library Tab]: Group and manage multiple folders. Click 'New Library' to create a group and add folders. If a folder was moved, use 'Re-select Folder...' to update its path."
            }));

            stack.Children.Add(CreateHelpSection("🖼️ Thumbnails, Selection & Scrolling", new[]
            {
                "[Thumbnails]: Displays all supported image files in the current folder.",
                "[Multi-Selection]: Hold Ctrl + Click to toggle multiple images. Shift + Click for range selection, Ctrl + A to select all.",
                "[Zoom]: Press Ctrl + Mouse Wheel over the list to resize thumbnail icons.",
                "[Drag Scrolling]: Left or middle-click and drag across the thumbnail grid for smooth scrolling.",
                "[Sorting]: Sort images by Modified Date, Date Taken, or Rating (Ascending / Descending)."
            }));

            stack.Children.Add(CreateHelpSection("⭐ Favorites & Rating Filtering", new[]
            {
                "[Favorite Flag]: Click the star icon on thumbnail top-right, use context menu, or press 'F' in viewer window.",
                "[Rating (★1-5)]: Click stars in properties pane, use context menu, or press '0' to '5' keys. Supports batch rating for multiple selected images.",
                "[Rating Filter]: Filter images by specific star rating (★1 to ★5, No Rating, or All) from toolbar dropdown.",
                "[AND Condition]: When both 'Show Favorites' and 'Rating Filter' are enabled, only images matching BOTH conditions are displayed."
            }));

            stack.Children.Add(CreateHelpSection("📋 Copy, Move & Delete", new[]
            {
                "[Copy / Move to Folder]: Choose 'Copy to Folder...' or 'Move to Folder...' from toolbar or context menu.",
                "[Clipboard]: Full support for Ctrl+C (Copy), Ctrl+X (Cut), Ctrl+V (Paste), compatible with Windows Explorer.",
                "[Delete]: Press Delete key or select Delete from context menu to remove selected images."
            }));

            stack.Children.Add(CreateHelpSection("🖱️ Context Menu", new[]
            {
                "[Set Rating]: Set rating from ★1 to ★5 or clear rating.",
                "[Toggle Favorite]: Bookmark or unbookmark selected image.",
                "[Copy / Cut / Paste]: Perform batch operations on selected images.",
                "[Open with External App]: Open selected image directly in registered image editors.",
                "[Open with Windows Photos]: Opens the file in default Windows Photos viewer.",
                "[Show in Explorer]: Opens Windows File Explorer with the selected item highlighted."
            }));

            stack.Children.Add(CreateHelpSection("📊 Preview & EXIF Metadata", new[]
            {
                "[Preview Pane]: Select 1 image to preview, adjust star rating, and view EXIF metadata.",
                "[Multi-Selection]: Preview is hidden when multiple items are selected, showing selection summary and batch rating controls instead."
            }));

            stack.Children.Add(CreateHelpSection("🪟 Image Viewer (Separate Window)", new[]
            {
                "[Open]: Double-click a thumbnail to open in a separate window.",
                "[Navigate]: Left/Right arrow keys, or mouse wheel.",
                "[Zoom]: Ctrl + Mouse Wheel to zoom in/out.",
                "[Pan]: Drag and drop to pan zoomed images.",
                "[Rating & Favorites]: Press 'F' to toggle favorite, '0'-'5' keys to set star rating.",
                "[Info Overlay]: Press 'I' or use context menu to toggle photographic EXIF information overlay.",
                "[Close]: Press Esc key."
            }));

            stack.Children.Add(CreateHelpSection("📷 High-Res RAW Image Previews & Auto-Rotation", new[]
            {
                "[Full-Res Previews]: Automatically extracts full-resolution previews from Canon CR3, Olympus ORF, Nikon NEF, Sony ARW, DNG, etc.",
                "[Auto-Rotation]: Automatically detects camera orientation sensors (Exif Orientation) and rotates portrait/vertical photos accordingly."
            }));

            stack.Children.Add(CreateHelpSection("🎨 External Program Integration", new[]
            {
                "[Configure Apps]: Register favorite photo editors (Photoshop, GIMP, Paint, etc.) in Settings -> External Apps.",
                "[Launch]: Right-click any photo and select 'Open with...' to launch with the selected editor."
            }));

            stack.Children.Add(CreateHelpSection("🤖 AI Auto-Classification (Ollama / ONNX DirectML / Rule-based)", new[]
            {
                "[AI Auto-Classification]: Click 'AI Auto Classification' button in the toolbar.",
                "[Multi-Engine]: Choose between local Ollama Vision models (LLaVA/Moondream), ONNX DirectML (GPU/CPU), or color/feature analysis.",
                "[Automatic Categorization]: Classify images into People, Landscape, Animal, Food, Vehicle, Building, Document and copy/move to subfolders or tag."
            }));

            stack.Children.Add(CreateHelpSection("💾 Thumbnail Cache Management", new[]
            {
                "[Manage Cache]: Open Settings -> 'Manage Cache...' to view disk usage and file counts.",
                "[Bulk Cleanup]: Click 'Clear All Cache' to instantly purge disk and in-memory thumbnail caches.",
                "[Auto-Cleanup on Exit]: Configure retention days (7, 14, 30, 60 days) or max size limits (1GB, 5GB, 10GB) to clean cache automatically on application close."
            }));

            stack.Children.Add(CreateHelpSection("⚙️ Settings Backup & Restore", new[]
            {
                "[Settings Menu]: Click the gear icon on the top-left toolbar.",
                "[Export / Import]: Backup and restore favorites, history, external apps, and database as a ZIP archive."
            }));

            // Troubleshooting and Open Log Folder Button (EN)
            var logSectionEn = CreateHelpSection("📋 Logs & Troubleshooting", new[]
            {
                "[Logs Location]: Diagnostic logs and crash logs are safely stored in AppData\\Local\\ImageManager\\Logs.",
                "[Report an Issue]: If you encounter unexpected crashes or issues, please attach crash.log or app.log from the logs folder."
            });
            var openLogBtnEn = new Button
            {
                Content = "📁 Open Logs Folder",
                Margin = new Thickness(0, 4, 0, 0)
            };
            openLogBtnEn.Click += (s, e) => Services.AppLogService.OpenLogFolder();
            logSectionEn.Children.Add(openLogBtnEn);
            stack.Children.Add(logSectionEn);
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

    /// <summary>「新規ライブラリ」作成ボタン押下時のハンドラ</summary>
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

    /// <summary>ライブラリツリーアイテムクリック時のハンドラ</summary>
    private async void LibraryTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is ViewModels.LibraryNodeViewModel node)
        {
            if (!node.IsLibrary && !string.IsNullOrEmpty(node.FullPath))
            {
                if (!System.IO.Directory.Exists(node.FullPath))
                {
                    await PromptRelocateLibraryFolderAsync(node);
                }
                else
                {
                    await ViewModel.SelectFolderFromTreeAsync(node.FullPath);
                }
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
                    else if (menuItem.Name == "RemoveFolderMenuItem" || menuItem.Name == "RelocateFolderMenuItem")
                    {
                        menuItem.Visibility = node.IsTopLevelFolder ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
        }
    }

    private async void RelocateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ViewModels.LibraryNodeViewModel node)
        {
            await PromptRelocateLibraryFolderAsync(node);
        }
    }

    /// <summary>
    /// ライブラリ登録フォルダが見つからない（移動・削除された）場合に再選択を促すダイアログ
    /// </summary>
    private async System.Threading.Tasks.Task PromptRelocateLibraryFolderAsync(ViewModels.LibraryNodeViewModel node)
    {
        var isJa = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);

        var dialog = new ContentDialog
        {
            Title = isJa ? "フォルダが見つかりません" : "Folder Not Found",
            Content = isJa 
                ? $"選択されたフォルダ「{node.Name}」が見つかりません。\n({node.FullPath})\n\n移動先・変更後のフォルダを再選択しますか？"
                : $"The folder '{node.Name}' ({node.FullPath}) could not be found.\n\nWould you like to select its new location?",
            PrimaryButtonText = isJa ? "フォルダを再選択..." : "Re-select Folder...",
            CloseButtonText = isJa ? "キャンセル" : "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var newFolder = await ViewModel.FileSystemService.SelectFolderAsync();
            if (!string.IsNullOrEmpty(newFolder))
            {
                await ViewModel.RelocateLibraryFolderAsync(node, newFolder);
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