using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ImageManager.Converters
{
    /// <summary>
    /// レーティング数値（1〜5）と特定の位置（パラメータ 1〜5）を比較し、
    /// 点灯状態（アクティブ）か消灯状態（非アクティブ）の星ブラシ色を返すコンバーター。
    /// </summary>
    public class RatingToStarBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush ActiveStarBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 193, 7)); // Gold #FFC107
        private static readonly SolidColorBrush InactiveStarBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 150, 150, 150)); // Muted Gray

        /// <summary>
        /// レーティング値がパラメータで指定された星の位置以上であればアクティブ色、未満であれば非アクティブ色を返します。
        /// </summary>
        /// <param name="value">現在のレーティング値（int）</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">星のインデックス番号（"1"〜"5"）</param>
        /// <param name="language">言語情報</param>
        /// <returns>アクティブ色または非アクティブ色の <see cref="SolidColorBrush"/></returns>
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
