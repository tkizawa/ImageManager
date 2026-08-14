using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ImageManager.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool boolValue = false;
            if (value is bool b)
            {
                boolValue = b;
            }

            // Support inverted parameter if "Invert" or "!" is passed
            if (parameter is string paramStr && (paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase) || paramStr == "!"))
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                if (parameter is string paramStr && (paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase) || paramStr == "!"))
                {
                    result = !result;
                }
                return result;
            }
            return false;
        }
    }
}
