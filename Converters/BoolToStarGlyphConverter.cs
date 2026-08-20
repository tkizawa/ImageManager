using Microsoft.UI.Xaml.Data;
using System;

namespace ImageManager.Converters
{
    /// <summary>
    /// お気に入り状態（bool）をSegoe MDL2 Assets等の星グリフ文字列（Unicode）に変換するコンバーター。
    /// お気に入りの場合は塗りつぶし星（\uE735）、非お気に入りの場合は輪郭星（\uE734）を返します。
    /// </summary>
    public class BoolToStarGlyphConverter : IValueConverter
    {
        /// <summary>
        /// bool値をお気に入りアイコンのグリフコードに変換します。
        /// </summary>
        /// <param name="value">変換元のbool値</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">パラメータ</param>
        /// <param name="language">言語情報</param>
        /// <returns>星グリフの文字列</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isFav && isFav)
            {
                return "\uE735"; // 塗りつぶし星
            }
            return "\uE734"; // 輪郭線星
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
