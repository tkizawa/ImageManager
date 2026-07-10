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

    public ImageWindow(Models.ImageFile imageFile, Services.SettingsService settingsService)
    {
        this.InitializeComponent();
        _settingsService = settingsService;

        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Title = imageFile.FileName;

        try
        {
            var bitmapImage = new BitmapImage(new Uri(imageFile.FilePath));
            FullImage.Source = bitmapImage;
        }
        catch (Exception)
        {
            // 画像ロードエラー時の処理
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
}
