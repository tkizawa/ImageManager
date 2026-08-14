using System;
using Microsoft.UI.Xaml.Data;

namespace ImageManager.Converters
{
    public class RatingToStarsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int rating = 0;
            if (value is int intVal) rating = intVal;
            else if (value is double dVal) rating = (int)Math.Round(dVal);
            else if (value is string sVal && int.TryParse(sVal, out int parsed)) rating = parsed;

            string param = parameter as string ?? string.Empty;

            if (param.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                if (rating <= 0) return "☆☆☆☆☆";
                return new string('★', Math.Clamp(rating, 0, 5)) + new string('☆', Math.Max(0, 5 - rating));
            }
            if (param.Equals("badge", StringComparison.OrdinalIgnoreCase))
            {
                if (rating <= 0) return string.Empty;
                return $"★{rating}";
            }
            if (param.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                if (rating <= 0) return "なし";
                return $"★ {rating}";
            }

            // Default: "★★★☆☆ (3)" or "なし"
            if (rating <= 0) return "なし";
            return new string('★', Math.Clamp(rating, 0, 5)) + new string('☆', Math.Max(0, 5 - rating)) + $" ({rating})";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
