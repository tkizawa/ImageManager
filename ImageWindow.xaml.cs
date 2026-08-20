using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using WinRT.Interop;
using Microsoft.UI;

namespace ImageManager;

/// <summary>
/// 単一画像を高解像度・原寸で閲覧するためのプレビュー専用ウィンドウクラス。
/// 
/// 主な機能：
/// 1. ウィンドウ位置・サイズの永続化と次回起動時の復元（プロジェクト規約準拠）
/// 2. マウスホイールによる前後画像切り替え、Ctrl+ホイールによるスムーズズーム
/// 3. マウスドラッグによる自由な画像パン（位置移動）
/// 4. キーボードショートカット（左右矢印/Space/BSで画像送り、F/IキーでExif情報表示切替、0〜5でレーティング付与、Escで閉じる）
/// 5. 右クリックコンテキストメニューからの外部アプリケーション連携
/// </summary>
public sealed partial class ImageWindow : Window
{
    private AppWindow _appWindow = null!;
    private readonly Services.ISettingsService _settingsService;
    private readonly ViewModels.MainViewModel _viewModel;
    
    private bool _isDragging = false;
    private Windows.Foundation.Point _lastPointerPosition;

    /// <summary>
    /// <see cref="ImageWindow"/> クラスの新しいインスタンスを初期化します。
    /// 前回のウィンドウ位置・サイズおよびオーバーレイ設定を復元します。
    /// </summary>
    /// <param name="viewModel">メインViewModelインスタンス</param>
    /// <param name="settingsService">設定管理サービス</param>
    public ImageWindow(ViewModels.MainViewModel viewModel, Services.ISettingsService settingsService)
    {
        this.InitializeComponent();
        _settingsService = settingsService;
        _viewModel = viewModel;
        RootGrid.DataContext = _viewModel;

        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_viewModel.SelectedImage != null)
        {
            UpdateDisplayedImage(_viewModel.SelectedImage);
        }

        // プロジェクト規約: 終了時のウィンドウサイズと位置を復元
        var settings = _settingsService.Load();
        if (!double.IsNaN(settings.ImageWindowWidth) && settings.ImageWindowWidth > 0 &&
            !double.IsNaN(settings.ImageWindowHeight) && settings.ImageWindowHeight > 0)
        {
            _appWindow.Resize(new Windows.Graphics.SizeInt32((int)settings.ImageWindowWidth, (int)settings.ImageWindowHeight));
        }
        else
        {
            _appWindow.Resize(new Windows.Graphics.SizeInt32(1024, 768));
        }

        if (!double.IsNaN(settings.ImageWindowLeft) && !double.IsNaN(settings.ImageWindowTop))
        {
            _appWindow.Move(new Windows.Graphics.PointInt32((int)settings.ImageWindowLeft, (int)settings.ImageWindowTop));
        }

        // 情報オーバーレイの表示状態を復元
        bool showInfo = settings.ShowImageWindowInfo;
        InfoOverlayBorder.Visibility = showInfo ? Visibility.Visible : Visibility.Collapsed;
        ToggleInfoMenuItem.IsChecked = showInfo;

        this.Closed += ImageWindow_Closed;
    }

    /// <summary>
    /// ウィンドウ終了時に現在の位置およびサイズを設定へ保存します（プロジェクト規約準拠）。
    /// </summary>
    private void ImageWindow_Closed(object sender, WindowEventArgs args)
    {
        var settings = _settingsService.Load();
        settings.ImageWindowWidth = _appWindow.Size.Width;
        settings.ImageWindowHeight = _appWindow.Size.Height;
        settings.ImageWindowLeft = _appWindow.Position.X;
        settings.ImageWindowTop = _appWindow.Position.Y;
        _settingsService.Save(settings);
    }

    /// <summary>
    /// ウィンドウに表示する画像を指定モデルに更新し、原寸デコードおよびズーム・パンのリセットを行います。
    /// </summary>
    /// <param name="imageFile">表示対象の画像モデル</param>
    private void UpdateDisplayedImage(Models.ImageFile imageFile)
    {
        _appWindow.Title = imageFile.FileName;

        try
        {
            var bitmapImage = new BitmapImage();
            FullImage.Source = bitmapImage;
            // 原寸（decodeWidth = 0）で高品質非同期読み込み
            _ = Services.RawThumbnailService.LoadBitmapImageAsync(bitmapImage, imageFile.FilePath, 0);
            
            _ = imageFile.LoadExifAsync();

            // 画像切り替え時はズームと位置をデフォルト（1.0倍・原点）にリセット
            ImageTransform.ScaleX = 1.0;
            ImageTransform.ScaleY = 1.0;
            ImageTransform.TranslateX = 0;
            ImageTransform.TranslateY = 0;
        }
        catch (Exception)
        {
            // 画像ロード失敗時
        }
    }

    /// <summary>キーボードショートカット（Iキー）によるExifオーバーレイ表示切り替え</summary>
    private void ToggleInfoOverlay_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleInfoDisplay();
    }

    /// <summary>メニュー項目クリックによるExifオーバーレイ表示切り替え</summary>
    private void ToggleInfoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToggleInfoDisplay();
    }

    /// <summary>キーボードショートカット（Fキー）によるお気に入り切り替え</summary>
    private void ToggleFavorite_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleSelectedImageFavorite();
    }

    /// <summary>メニュー項目クリックによるお気に入り切り替え</summary>
    private void ToggleFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToggleSelectedImageFavorite();
    }

    /// <summary>メニュー項目クリックによるレーティング設定</summary>
    private void SetRatingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tagStr && int.TryParse(tagStr, out int rating))
        {
            SetSelectedImageRating(rating);
        }
    }

    /// <summary>キーボード数字キー（0〜5 / テンキー0〜5）によるレーティング即時反映</summary>
    private void RatingKey_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        int rating = 0;
        if (sender.Key >= Windows.System.VirtualKey.Number0 && sender.Key <= Windows.System.VirtualKey.Number5)
        {
            rating = (int)(sender.Key - Windows.System.VirtualKey.Number0);
        }
        else if (sender.Key >= Windows.System.VirtualKey.NumberPad0 && sender.Key <= Windows.System.VirtualKey.NumberPad5)
        {
            rating = (int)(sender.Key - Windows.System.VirtualKey.NumberPad0);
        }
        SetSelectedImageRating(rating);
    }

    /// <summary>表示中画像のレーティング値を設定します。</summary>
    private void SetSelectedImageRating(int rating)
    {
        if (_viewModel?.SelectedImage != null)
        {
            _viewModel.SelectedImage.Rating = Math.Clamp(rating, 0, 5);
        }
    }

    /// <summary>表示中画像のお気に入り状態をトグル反転します。</summary>
    private void ToggleSelectedImageFavorite()
    {
        if (_viewModel?.SelectedImage != null)
        {
            _viewModel.SelectedImage.IsFavorite = !_viewModel.SelectedImage.IsFavorite;
        }
    }

    /// <summary>Exif情報オーバーレイの表示/非表示を切り替え、設定に保存します。</summary>
    private void ToggleInfoDisplay()
    {
        bool isCurrentlyVisible = InfoOverlayBorder.Visibility == Visibility.Visible;
        bool newVisible = !isCurrentlyVisible;

        InfoOverlayBorder.Visibility = newVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleInfoMenuItem.IsChecked = newVisible;

        var settings = _settingsService.Load();
        settings.ShowImageWindowInfo = newVisible;
        _settingsService.Save(settings);
    }

    /// <summary>Windows標準の「フォト」アプリで現在の画像を開きます。</summary>
    private async void OpenWithPhotosMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var currentImage = _viewModel.SelectedImage;
        if (currentImage != null && !string.IsNullOrEmpty(currentImage.FilePath))
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(currentImage.FilePath);
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

    /// <summary>エクスプローラーで現在の画像ファイルを選択状態で開きます。</summary>
    private void ShowInExplorerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var currentImage = _viewModel.SelectedImage;
        if (currentImage != null && !string.IsNullOrEmpty(currentImage.FilePath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{currentImage.FilePath}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show in explorer: {ex.Message}");
            }
        }
    }

    /// <summary>コンテキストメニューオープン時に登録済み外部アプリ一覧を動的に生成します。</summary>
    private void ContextFlyout_Opening(object sender, object e)
    {
        var currentImage = _viewModel.SelectedImage;
        if (currentImage != null && !string.IsNullOrEmpty(currentImage.FilePath))
        {
            PopulateExternalAppsMenu(ExternalAppsSubMenu, currentImage);
        }
    }

    /// <summary>外部アプリケーションメニュー項目を構築します。</summary>
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

    /// <summary>外部アプリ起動メニュー項目クリック時のハンドラ</summary>
    private void ExternalAppItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is (Models.ExternalApp app, Models.ImageFile imageFile))
        {
            MainWindow.LaunchExternalApp(app, imageFile.FilePath);
        }
    }

    /// <summary>外部アプリ設定ダイアログを表示します。</summary>
    private async void ConfigureExternalApps_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExternalAppsDialog(_settingsService, this.Content?.XamlRoot ?? App.MainWindow?.Content?.XamlRoot);
        await dialog.ShowAsync();
    }

    /// <summary>次の画像へ進むショートカット（右矢印 / PageDown）</summary>
    private void NextImage_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        MoveToNextImage();
    }

    /// <summary>前の画像へ戻るショートカット（左矢印 / PageUp）</summary>
    private void PreviousImage_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        MoveToPreviousImage();
    }

    /// <summary>
    /// マウスホイール操作ハンドラ。
    /// Ctrl+ホイールでズーム、通常ホイールで前後の画像へ移動します。
    /// </summary>
    private void Image_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var pointerPoint = e.GetCurrentPoint(FullImage);
        int delta = pointerPoint.Properties.MouseWheelDelta;

        if (isCtrlPressed)
        {
            // ズーム処理（下限1.0倍）
            double zoomFactor = delta > 0 ? 1.1 : 1 / 1.1;
            double newScale = Math.Max(1.0, ImageTransform.ScaleX * zoomFactor);

            ImageTransform.ScaleX = newScale;
            ImageTransform.ScaleY = newScale;
            
            // 等倍に戻った場合は位置を中央リセット
            if (newScale <= 1.0)
            {
                ImageTransform.TranslateX = 0;
                ImageTransform.TranslateY = 0;
            }
            
            e.Handled = true;
            return;
        }

        // 前後画像の切り替え
        if (delta < 0)
        {
            MoveToNextImage();
            e.Handled = true;
        }
        else if (delta > 0)
        {
            MoveToPreviousImage();
            e.Handled = true;
        }
    }

    /// <summary>次の画像へ切り替えます。</summary>
    private void MoveToNextImage()
    {
        var images = _viewModel.Images;
        var current = _viewModel.SelectedImage;
        if (current == null || images.Count == 0) return;

        int idx = images.IndexOf(current);
        if (idx >= 0 && idx < images.Count - 1)
        {
            _viewModel.SelectedImage = images[idx + 1];
            UpdateDisplayedImage(_viewModel.SelectedImage);
        }
    }

    /// <summary>前の画像へ切り替えます。</summary>
    private void MoveToPreviousImage()
    {
        var images = _viewModel.Images;
        var current = _viewModel.SelectedImage;
        if (current == null || images.Count == 0) return;

        int idx = images.IndexOf(current);
        if (idx > 0)
        {
            _viewModel.SelectedImage = images[idx - 1];
            UpdateDisplayedImage(_viewModel.SelectedImage);
        }
    }

    /// <summary>Escキー押下時にウィンドウを閉じます。</summary>
    private void CloseWindow_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        this.Close();
    }

    /// <summary>
    /// マウスポインター押下ハンドラ。ドラッグによるパン移動を開始します。
    /// </summary>
    private void Image_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(this.Content);
        if (pointerPoint.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastPointerPosition = pointerPoint.Position;
            FullImage.CapturePointer(e.Pointer);
            
            var prop = typeof(Microsoft.UI.Xaml.UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(FullImage, Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand));
            
            e.Handled = true;
        }
    }

    /// <summary>
    /// マウスポインター移動ハンドラ。画像をドラッグ移動（パン）させます。
    /// </summary>
    private void Image_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var pointerPoint = e.GetCurrentPoint(this.Content);
            var currentPosition = pointerPoint.Position;

            double offsetX = currentPosition.X - _lastPointerPosition.X;
            double offsetY = currentPosition.Y - _lastPointerPosition.Y;

            ImageTransform.TranslateX += offsetX;
            ImageTransform.TranslateY += offsetY;

            _lastPointerPosition = currentPosition;
            e.Handled = true;
        }
    }

    /// <summary>
    /// マウスポインター解放ハンドラ。ドラッグ移動を終了します。
    /// </summary>
    private void Image_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            FullImage.ReleasePointerCapture(e.Pointer);
            
            var prop = typeof(Microsoft.UI.Xaml.UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(FullImage, null);
            
            e.Handled = true;
        }
    }
}
