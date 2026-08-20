using System;
using Microsoft.UI.Xaml.Data;

namespace ImageManager.Converters
{
    /// <summary>
    /// レーティング数値（0〜5）をテキスト形式の星表記文字列（例: "★★★☆☆ (3)", "★3", "☆☆☆☆☆" など）にフォーマットするコンバーター。
    /// ConverterParameter に応じてフォーマット形式（"full", "badge", "text", デフォルト）を切り替えます。
    /// </summary>
    public class RatingToStarsConverter : IValueConverter
    {
        /// <summary>
        /// レーティング数値を星表記文字列に変換します。
        /// </summary>
        /// <param name="value">レーティング数値（int, double, string）</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">フォーマットオプション（"full", "badge", "text"）</param>
        /// <param name="language">言語情報</param>
        /// <returns>フォーマットされた星表記文字列</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int rating = 0;
            if (value is int intVal) rating = intVal;
            else if (value is double dVal) rating = (int)Math.Round(dVal);
            else if (value is string sVal && int.TryParse(sVal, out int parsed)) rating = parsed;

            string param = parameter as string ?? string.Empty;

            // "full": 常に5文字の星で表示（例: "★★★☆☆", 0は "☆☆☆☆☆"）
            if (param.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                if (rating <= 0) return "☆☆☆☆☆";
                return new string('★', Math.Clamp(rating, 0, 5)) + new string('☆', Math.Max(0, 5 - rating));
            }

            // "badge": バッジ用短縮表示（例: "★3"、0は非表示）
            if (param.Equals("badge", StringComparison.OrdinalIgnoreCase))
            {
                if (rating <= 0) return string.Empty;
                return $"★{rating}";
            }

            // "text": シンプルテキスト表示（例: "★ 3"、0は "なし"）
            if (param.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                if (rating <= 0) return "なし";
                return $"★ {rating}";
            }

            // デフォルト: "★★★☆☆ (3)" または "なし"
            if (rating <= 0) return "なし";
            return new string('★', Math.Clamp(rating, 0, 5)) + new string('☆', Math.Max(0, 5 - rating)) + $" ({rating})";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
