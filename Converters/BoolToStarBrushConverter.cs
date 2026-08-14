using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;

namespace ImageManager.Converters
{
    public class BoolToStarBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush FavoriteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 193, 7)); // Amber/Gold #FFC107
        private static readonly SolidColorBrush UnfavoriteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 160, 160, 160)); // Muted Gray

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isFav && isFav)
            {
                return FavoriteBrush;
            }
            return UnfavoriteBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
