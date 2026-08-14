using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ImageManager.Converters
{
    public class RatingToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int rating = 0;
            if (value is int intVal) rating = intVal;
            else if (value is double dVal) rating = (int)dVal;

            bool isVisible = rating > 0;

            if (parameter is string paramStr && paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                isVisible = !isVisible;
            }

            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
