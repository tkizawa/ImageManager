using Microsoft.UI.Xaml.Data;
using System;
using System.IO;

namespace ImageManager.Converters
{
    /// <summary>
    /// フルパス文字列から末尾のフォルダ名のみを抽出するコンバーター。
    /// UI上のツリーやヘッダーで親パスを除去してフォルダ名だけを表示する際に使用されます。
    /// </summary>
    public class PathToFolderNameConverter : IValueConverter
    {
        /// <summary>
        /// パス文字列からフォルダ名を取得します。
        /// </summary>
        /// <param name="value">フルパス文字列</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">パラメータ</param>
        /// <param name="language">言語情報</param>
        /// <returns>フォルダ名</returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(folderName))
                {
                    return path;
                }
                return folderName;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
