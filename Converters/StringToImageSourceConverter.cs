using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using ImageManager.Services;

namespace ImageManager.Converters
{
    /// <summary>
    /// 画像ファイルパス（string）を非同期読み込み対応の <see cref="BitmapImage"/> に変換するコンバーター。
    /// WeakReference によるメモリキャッシュを備え、高速なサムネイル描画を提供します。
    /// </summary>
    public class StringToImageSourceConverter : IValueConverter
    {
        /// <summary>GCによる回収を許容するメモリキャッシュ辞書</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WeakReference<BitmapImage>> ImageCache = new();

        /// <summary>
        /// 画像ファイルパスを BitmapImage に変換します。
        /// </summary>
        /// <param name="value">画像ファイルパス文字列</param>
        /// <param name="targetType">ターゲット型</param>
        /// <param name="parameter">パラメータ</param>
        /// <param name="language">言語情報</param>
        /// <returns>生成またはキャッシュされた <see cref="BitmapImage"/></returns>
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                // キャッシュに存在し、GCで破棄されていなければ再利用
                if (ImageCache.TryGetValue(path, out var weakRef) && weakRef.TryGetTarget(out var cachedImage))
                {
                    return cachedImage;
                }

                try
                {
                    var bitmapImage = new BitmapImage();
                    // 非同期で画像デコード・読み込み
                    _ = RawThumbnailService.LoadBitmapImageAsync(bitmapImage, path, 300);
                    ImageCache[path] = new WeakReference<BitmapImage>(bitmapImage);
                    return bitmapImage;
                }
                catch { }
            }
            return null!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
