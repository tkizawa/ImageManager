using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ImageManager.Converters
{
    /// <summary>
    /// レーティング数値が設定されているか（1以上か）に基づいて <see cref="Visibility"/> に変換するコンバーター。
    /// レーティングが1以上なら Visible、0以下なら Collapsed を返します。
    /// ConverterParameter に "Invert" を指定すると反転します。
    /// </summary>
    public class RatingToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// レーティング数値を Visibility に変換します。
        /// </summary>
        /// <param name="value">レーティング値（int, double）</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">"Invert" で反転</param>
        /// <param name="language">言語情報</param>
        /// <returns><see cref="Visibility"/></returns>
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
