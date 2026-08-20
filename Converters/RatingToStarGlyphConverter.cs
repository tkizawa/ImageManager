using System;
using Microsoft.UI.Xaml.Data;

namespace ImageManager.Converters
{
    /// <summary>
    /// レーティング数値（1〜5）と特定の位置（パラメータ 1〜5）を比較し、
    /// 塗りつぶし星（\uE735）または輪郭星（\uE734）のグリフ文字列を返すコンバーター。
    /// </summary>
    public class RatingToStarGlyphConverter : IValueConverter
    {
        private const string SolidStar = "\uE735"; // 塗りつぶし星
        private const string EmptyStar = "\uE734"; // 輪郭線星

        /// <summary>
        /// レーティング値がパラメータで指定された星の位置以上であれば塗りつぶし星、未満であれば輪郭線星を返します。
        /// </summary>
        /// <param name="value">現在のレーティング値（int）</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">星のインデックス番号（"1"〜"5"）</param>
        /// <param name="language">言語情報</param>
        /// <returns>星グリフ文字列</returns>
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
