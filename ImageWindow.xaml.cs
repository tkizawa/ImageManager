using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using WinRT.Interop;
using Microsoft.UI;

namespace ImageManager;

public sealed partial class ImageWindow : Window
{
    private AppWindow _appWindow = null!;
    private readonly Services.SettingsService _settingsService;
    private readonly ViewModels.MainViewModel _viewModel;
    
    private bool _isDragging = false;
    private Windows.Foundation.Point _lastPointerPosition;

    public ImageWindow(ViewModels.MainViewModel viewModel, Services.SettingsService settingsService)
    {
        this.InitializeComponent();
        _settingsService = settingsService;
        _viewModel = viewModel;

        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_viewModel.SelectedImage != null)
        {
            UpdateDisplayedImage(_viewModel.SelectedImage);
        }

        // 初期ウィンドウサイズと位置の設定
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

        this.Closed += ImageWindow_Closed;
    }

    private void ImageWindow_Closed(object sender, WindowEventArgs args)
    {
        var settings = _settingsService.Load();
        settings.ImageWindowWidth = _appWindow.Size.Width;
        settings.ImageWindowHeight = _appWindow.Size.Height;
        settings.ImageWindowLeft = _appWindow.Position.X;
        settings.ImageWindowTop = _appWindow.Position.Y;
        _settingsService.Save(settings);
    }

    private void UpdateDisplayedImage(Models.ImageFile imageFile)
    {
        _appWindow.Title = imageFile.FileName;

        try
        {
            var bitmapImage = new BitmapImage(new Uri(imageFile.FilePath));
            FullImage.Source = bitmapImage;
            
            // 画像切り替え時はズームと位置をデフォルト（ウィンドウに収まるサイズ）にリセット
            ImageTransform.ScaleX = 1.0;
            ImageTransform.ScaleY = 1.0;
            ImageTransform.TranslateX = 0;
            ImageTransform.TranslateY = 0;
        }
        catch (Exception)
        {
            // 画像ロードエラー時の処理
        }
    }

    private void NextImage_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        MoveToNextImage();
    }

    private void PreviousImage_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        MoveToPreviousImage();
    }

    private void Image_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var pointerPoint = e.GetCurrentPoint(FullImage);
        int delta = pointerPoint.Properties.MouseWheelDelta;

        if (isCtrlPressed)
        {
            // ズーム処理
            double zoomFactor = delta > 0 ? 1.1 : 1 / 1.1;
            double newScale = Math.Max(1.0, ImageTransform.ScaleX * zoomFactor); // 下限は1.0 (ウィンドウに収まるサイズ)

            // マウス位置を中心にズームするための計算 (簡易版)
            // 実際はTranslateX/Yの調整も必要だが、ここではシンプルに中央基準に近いズームとする
            ImageTransform.ScaleX = newScale;
            ImageTransform.ScaleY = newScale;
            
            // スケールが1.0に戻った場合は位置も中央にリセットする
            if (newScale <= 1.0)
            {
                ImageTransform.TranslateX = 0;
                ImageTransform.TranslateY = 0;
            }
            
            e.Handled = true;
            return;
        }

        // Ctrlキーが押されていない場合は、前後画像の切り替えを行う
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

    private void CloseWindow_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        this.Close();
    }

    private void Image_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pointerPoint = e.GetCurrentPoint(this.Content);
        if (pointerPoint.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastPointerPosition = pointerPoint.Position;
            FullImage.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

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

    private void Image_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            FullImage.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }
}
