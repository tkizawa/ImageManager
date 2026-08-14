using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ImageManager.Converters
{
    public class RatingToStarBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush ActiveStarBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 193, 7)); // Gold #FFC107
        private static readonly SolidColorBrush InactiveStarBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 150, 150, 150)); // Muted Gray

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int rating = 0;
            if (value is int intVal) rating = intVal;

            int targetStar = 1;
            if (parameter is string paramStr && int.TryParse(paramStr, out int parsed))
            {
                targetStar = parsed;
            }

            if (rating >= targetStar)
            {
                return ActiveStarBrush;
            }
            return InactiveStarBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
