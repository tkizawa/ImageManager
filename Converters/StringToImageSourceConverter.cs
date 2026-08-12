using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using ImageManager.Services;

namespace ImageManager.Converters
{
    public class StringToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    var bitmapImage = new BitmapImage();
                    _ = RawThumbnailService.LoadBitmapImageAsync(bitmapImage, path);
                    return bitmapImage;
                }
                catch { }
            }
            return null!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
