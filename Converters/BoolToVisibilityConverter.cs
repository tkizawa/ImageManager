using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ImageManager.Converters
{
    /// <summary>
    /// bool値を <see cref="Visibility"/>（Visible / Collapsed）に変換するコンバーター。
    /// ConverterParameter に "Invert" または "!" を渡すことで反転変換にも対応します。
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// bool値を <see cref="Visibility"/> に変換します。
        /// </summary>
        /// <param name="value">変換元のbool値</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">反転指定パラメータ（"Invert" または "!" で結果を反転）</param>
        /// <param name="language">言語情報</param>
        /// <returns><see cref="Visibility.Visible"/> または <see cref="Visibility.Collapsed"/></returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool boolValue = false;
            if (value is bool b)
            {
                boolValue = b;
            }

            // "Invert" または "!" が渡された場合は真偽値を反転
            if (parameter is string paramStr && (paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase) || paramStr == "!"))
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// <see cref="Visibility"/> を bool値に逆変換します。
        /// </summary>
        /// <param name="value">変換元のVisibility</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">反転指定パラメータ</param>
        /// <param name="language">言語情報</param>
        /// <returns>対応するbool値</returns>
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
