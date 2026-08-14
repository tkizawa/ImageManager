using System;
using Microsoft.UI.Xaml.Data;

namespace ImageManager.Converters
{
    public class RatingToStarGlyphConverter : IValueConverter
    {
        private const string SolidStar = "\uE735";
        private const string EmptyStar = "\uE734";

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int rating = 0;
            if (value is int intVal) rating = intVal;

            int targetStar = 1;
            if (parameter is string paramStr && int.TryParse(paramStr, out int parsed))
            {
                targetStar = parsed;
            }

            return rating >= targetStar ? SolidStar : EmptyStar;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
