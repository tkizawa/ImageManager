using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using ImageManager.Services;

namespace ImageManager.Converters
{
    public class StringToImageSourceConverter : IValueConverter
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WeakReference<BitmapImage>> ImageCache = new();

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                if (ImageCache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var cachedImage))
                {
                    return cachedImage;
                }

                try
                {
                    var bitmapImage = new BitmapImage();
                    _ = RawThumbnailService.LoadBitmapImageAsync(bitmapImage, path, 300);
                    ImageCache[path] = new WeakReference<BitmapImage>(bitmapImage);
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
