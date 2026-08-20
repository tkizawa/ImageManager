using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace ImageManager.Converters
{
    /// <summary>
    /// お気に入り状態（bool）を星アイコンのブラシ色（SolidColorBrush）に変換するコンバーター。
    /// お気に入りの場合はゴールド/アンバー色、非お気に入りの場合は薄いグレー色を返します。
    /// </summary>
    public class BoolToStarBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush FavoriteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 193, 7)); // Amber/Gold #FFC107
        private static readonly SolidColorBrush UnfavoriteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 160, 160, 160)); // Muted Gray

        /// <summary>
        /// bool値をお気に入り表示用のブラシ色に変換します。
        /// </summary>
        /// <param name="value">変換元のbool値（お気に入りフラグ）</param>
        /// <param name="targetType">変換先ターゲット型</param>
        /// <param name="parameter">オプションパラメータ</param>
        /// <param name="language">言語情報</param>
        /// <returns>お気に入り状態に応じた <see cref="SolidColorBrush"/></returns>
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
