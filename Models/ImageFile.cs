using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ImageManager.Services;

namespace ImageManager.Models
{
    /// <summary>
    /// アプリケーション内で扱う個々の画像ファイルを表すモデルクラス。
    /// ファイル情報、Exifメタデータ（撮影日時・カメラ・レンズ・露出情報等）、
    /// レーティング、お気に入り状態、サムネイル読み込み処理を提供します。
    /// </summary>
    public partial class ImageFile : ObservableObject
    {
        /// <summary>画像ファイルの絶対パス</summary>
        [ObservableProperty]
        private string _filePath;

        /// <summary>画像ファイル名（拡張子含む）</summary>
        [ObservableProperty]
        private string _fileName;

        /// <summary>ファイルサイズ（バイト単位）</summary>
        [ObservableProperty]
        private long _fileSize;

        /// <summary>ファイルの作成日時</summary>
        [ObservableProperty]
        private DateTime _creationTime;

        /// <summary>ファイルの最終更新日時</summary>
        [ObservableProperty]
        private DateTime _lastWriteTime;

        /// <summary>画像の幅（ピクセル）</summary>
        [ObservableProperty]
        private int _imageWidth;

        /// <summary>画像の高さ（ピクセル）</summary>
        [ObservableProperty]
        private int _imageHeight;

        /// <summary>Exifから取得した撮影日時文字列</summary>
        [ObservableProperty]
        private string _dateTaken = string.Empty;

        /// <summary>撮影に使用されたカメラのモデル名</summary>
        [ObservableProperty]
        private string _cameraModel = string.Empty;

        /// <summary>撮影に使用されたレンズのモデル名</summary>
        [ObservableProperty]
        private string _lens = string.Empty;

        /// <summary>シャッタースピード / 露光時間（例: "1/250"）</summary>
        [ObservableProperty]
        private string _exposureTime = string.Empty;

        /// <summary>絞り値（F値、例: "2.8"）</summary>
        [ObservableProperty]
        private string _fNumber = string.Empty;

        /// <summary>ISO感度（例: "100", "3200"）</summary>
        [ObservableProperty]
        private string _isoSpeed = string.Empty;

        /// <summary>焦点距離（例: "50.0 mm"）</summary>
        [ObservableProperty]
        private string _focalLength = string.Empty;

        /// <summary>画像分類カテゴリ（AIまたは手動で割り当てられた分類名）</summary>
        [ObservableProperty]
        private string _category = string.Empty;

        /// <summary>お気に入り登録されているかどうか</summary>
        [ObservableProperty]
        private bool _isFavorite;

        /// <summary>
        /// お気に入り状態が変更された際にデータベースへ自動反映します。
        /// </summary>
        /// <param name="value">変更後のお気に入り状態</param>
        partial void OnIsFavoriteChanged(bool value)
        {
            if (!string.IsNullOrEmpty(FilePath))
            {
                try
                {
                    DatabaseService.Instance.UpdateImageFavorite(FilePath, value);
                }
                catch (Exception ex)
                {
                    Services.AppLogService.LogException("ImageFile.OnIsFavoriteChanged", ex);
                }
            }
        }

        /// <summary>レーティング値（0: 未設定、1〜5: ★1〜★5）</summary>
        [ObservableProperty]
        private int _rating; // 0: None, 1..5: ★1..★5

        /// <summary>
        /// レーティングが変更された際に0〜5の範囲にクランプし、データベースへ反映します。
        /// </summary>
        /// <param name="value">変更後のレーティング値</param>
        partial void OnRatingChanged(int value)
        {
            if (value < 0 || value > 5)
            {
                int clamped = Math.Clamp(value, 0, 5);
                if (_rating != clamped)
                {
                    _rating = clamped;
                }
            }
            OnPropertyChanged(nameof(FormattedRating));
            OnPropertyChanged(nameof(HasRating));

            if (!string.IsNullOrEmpty(FilePath))
            {
                try
                {
                    DatabaseService.Instance.UpdateImageRating(FilePath, _rating);
                }
                catch (Exception ex)
                {
                    Services.AppLogService.LogException("ImageFile.OnRatingChanged", ex);
                }
            }
        }

        /// <summary>レーティングが1以上設定されているかを取得します。</summary>
        public bool HasRating => Rating > 0;

        /// <summary>
        /// UI表示用に星マークでフォーマットされたレーティング文字列を取得します（例: "★★★☆☆ (3)"）。
        /// </summary>
        public string FormattedRating
        {
            get
            {
                if (Rating <= 0) return "なし";
                return new string('★', Math.Min(5, Rating)) + new string('☆', Math.Max(0, 5 - Rating)) + $" ({Rating})";
            }
        }

        /// <summary>Exifメタデータがすでに読み込み済みかどうかを取得します。</summary>
        public bool IsExifLoaded { get; private set; }

        private Microsoft.UI.Xaml.Media.ImageSource? _thumbnailSource;

        private bool _isThumbnailLoading;

        /// <summary>
        /// UIバインディング用のサムネイル画像ソースを取得または設定します。
        /// 未読み込みの場合はアクセス時に非同期読み込みを開始します。
        /// </summary>
        public Microsoft.UI.Xaml.Media.ImageSource? ThumbnailSource
        {
            get
            {
                if (_thumbnailSource == null && !_isThumbnailLoading && !string.IsNullOrEmpty(FilePath))
                {
                    _ = LoadThumbnailAsync();
                }
                return _thumbnailSource;
            }
            set
            {
                SetProperty(ref _thumbnailSource, value);
            }
        }

        /// <summary>
        /// 画像のサムネイルを非同期に読み込みます。
        /// ディスクキャッシュ、標準画像、RAW現像/埋め込みJPEG、OSフォールバックの順に高速読み込みを試行します。
        /// </summary>
        /// <param name="decodeWidth">デコード時の横幅ピクセル数（デフォルト: 300px）</param>
        public async Task LoadThumbnailAsync(int decodeWidth = 300)
        {
            if (string.IsNullOrEmpty(FilePath) || _thumbnailSource != null || _isThumbnailLoading) return;
            _isThumbnailLoading = true;

            try
            {
                var dq = App.MainDispatcherQueue ?? App.MainWindow?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

                // UIスレッドでBitmapImageを設定するローカル関数
                void SetUriSource(string uriPath)
                {
                    if (dq != null && !dq.HasThreadAccess)
                    {
                        dq.TryEnqueue(() =>
                        {
                            try
                            {
                                ThumbnailSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(uriPath)) { DecodePixelWidth = decodeWidth };
                            }
                            catch { }
                        });
                    }
                    else
                    {
                        try
                        {
                            ThumbnailSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(uriPath)) { DecodePixelWidth = decodeWidth };
                        }
                        catch { }
                    }
                }

                // 1. ディスクキャッシュの存在確認（RAW現像済みキャッシュまたはNAS/外部ドライブの事前キャッシュ）
                string? cacheFilePath = RawThumbnailService.GetCacheFilePath(FilePath);
                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    SetUriSource(cacheFilePath);
                    return;
                }

                bool isRaw = RawThumbnailService.IsRawFile(FilePath);

                // 2. 標準画像（JPG, PNG, WebP等）は直接ファイルからネイティブに高速・高品質描画
                if (!isRaw)
                {
                    SetUriSource(FilePath);

                    // NASや外部ドライブ等の場合はローカルSSDキャッシュに非同期コピーして次回以降を高速化
                    if (cacheFilePath != null && RawThumbnailService.ShouldCacheFile(FilePath))
                    {
                        _ = Task.Run(() => RawThumbnailService.CacheStandardFileAsync(FilePath, cacheFilePath));
                    }
                    return;
                }

                // 3. RAW画像から埋め込みJPEGまたは現像プレビューを高速抽出
                var jpegBytes = await RawThumbnailService.GetEmbeddedJpegBytesAsync(FilePath);
                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    SetUriSource(cacheFilePath);
                    return;
                }

                if (jpegBytes != null && jpegBytes.Length > 0)
                {
                    if (dq != null && !dq.HasThreadAccess)
                    {
                        dq.TryEnqueue(async () =>
                        {
                            try
                            {
                                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = decodeWidth };
                                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                                using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                                writer.WriteBytes(jpegBytes);
                                await writer.StoreAsync();
                                stream.Seek(0);
                                await bitmap.SetSourceAsync(stream);
                                ThumbnailSource = bitmap;
                            }
                            catch { }
                        });
                    }
                    else
                    {
                        try
                        {
                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = decodeWidth };
                            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                            using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                            writer.WriteBytes(jpegBytes);
                            await writer.StoreAsync();
                            stream.Seek(0);
                            await bitmap.SetSourceAsync(stream);
                            ThumbnailSource = bitmap;
                        }
                        catch { }
                    }
                    return;
                }

                // 4. フォールバック: Windows Storage API経由でのサムネイル取得
                if (dq != null)
                {
                    dq.TryEnqueue(async () =>
                    {
                        try
                        {
                            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(FilePath);
                            using var thumb = await file.GetScaledImageAsThumbnailAsync(
                                Windows.Storage.FileProperties.ThumbnailMode.PicturesView,
                                (uint)decodeWidth,
                                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);
                            if (thumb != null && thumb.Size > 0)
                            {
                                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = decodeWidth };
                                await bitmap.SetSourceAsync(thumb);
                                ThumbnailSource = bitmap;
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
            finally
            {
                _isThumbnailLoading = false;
            }
        }

        /// <summary>
        /// 指定されたパスの画像ファイル情報を初期化します。
        /// ファイルサイズや作成日時・更新日時をファイルシステムから取得します。
        /// </summary>
        /// <param name="path">画像ファイルの絶対パス</param>
        public ImageFile(string path)
        {
            FilePath = path;
            FileName = Path.GetFileName(path);
            
            var info = new FileInfo(path);
            if (info.Exists)
            {
                FileSize = info.Length;
                CreationTime = info.CreationTime;
                LastWriteTime = info.LastWriteTime;
            }
        }

        /// <summary>
        /// 画像ファイルからExifメタデータを非同期で読み込み、モデルのプロパティおよびデータベースへ反映します。
        /// </summary>
        public async Task LoadExifAsync()
        {
            if (IsExifLoaded) return;
            IsExifLoaded = true;

            string dateTaken = string.Empty, cameraModel = string.Empty, lens = string.Empty, exposureTime = string.Empty, fNumber = string.Empty, isoSpeed = string.Empty, focalLength = string.Empty;
            int metaRating = 0;

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (!File.Exists(FilePath)) return;

                        // MetadataExtractor を使用して Exif タグを解析
                        var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(FilePath);
                        var exifIfd0 = directories.OfType<MetadataExtractor.Formats.Exif.ExifIfd0Directory>().FirstOrDefault();
                        var exifSubIfd = directories.OfType<MetadataExtractor.Formats.Exif.ExifSubIfdDirectory>().FirstOrDefault();

                        if (exifIfd0 != null)
                        {
                            cameraModel = exifIfd0.GetDescription(MetadataExtractor.Formats.Exif.ExifIfd0Directory.TagModel) ?? string.Empty;
                            // Exif タグ 18246: Windows / XMP Rating
                            if (exifIfd0.ContainsTag(18246))
                            {
                                var ratingStr = exifIfd0.GetDescription(18246);
                                if (int.TryParse(ratingStr, out int r) && r >= 1 && r <= 5)
                                {
                                    metaRating = r;
                                }
                            }
                        }

                        if (exifSubIfd != null)
                        {
                            dateTaken = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagDateTimeOriginal) ?? string.Empty;
                            lens = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagLensModel) ?? string.Empty;
                            exposureTime = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagExposureTime) ?? string.Empty;
                            fNumber = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagFNumber) ?? string.Empty;
                            isoSpeed = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagIsoEquivalent) ?? string.Empty;
                            focalLength = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagFocalLength) ?? string.Empty;
                        }
                    }
                    catch { }
                });
            }
            catch { }

            DateTaken = dateTaken;
            CameraModel = cameraModel;
            Lens = lens;
            ExposureTime = exposureTime;
            FNumber = fNumber;
            IsoSpeed = isoSpeed;
            FocalLength = focalLength;

            // ファイル内に埋め込まれたレーティングが存在し、アプリ未設定の場合は同期
            if (Rating == 0 && metaRating > 0)
            {
                Rating = metaRating;
            }

            // フォーマット済みプロパティのUI更新通知
            OnPropertyChanged(nameof(FormattedDateTaken));
            OnPropertyChanged(nameof(FormattedExposureSpecs));
            OnPropertyChanged(nameof(FormattedFileSize));

            // データベースにExifレコードをキャッシュ保存
            try
            {
                DatabaseService.Instance.UpdateExifRecord(this);
            }
            catch { }
        }

        /// <summary>
        /// UI表示用にフォーマットされた撮影日時（Exif未定義の場合は最終更新日時）を取得します。
        /// </summary>
        public string FormattedDateTaken
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DateTaken))
                    return DateTaken;
                return LastWriteTime != default ? LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss") : string.Empty;
            }
        }

        /// <summary>
        /// UI表示用にフォーマットされた露出情報文字列を取得します（例: "f/2.8  |  1/250s  |  ISO 100  |  50mm"）。
        /// </summary>
        public string FormattedExposureSpecs
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(FNumber))
                {
                    var f = FNumber.StartsWith("f/", StringComparison.OrdinalIgnoreCase) ? FNumber : $"f/{FNumber}";
                    parts.Add(f);
                }
                if (!string.IsNullOrWhiteSpace(ExposureTime))
                {
                    var exp = ExposureTime.EndsWith("s", StringComparison.OrdinalIgnoreCase) || ExposureTime.EndsWith("sec", StringComparison.OrdinalIgnoreCase) ? ExposureTime : $"{ExposureTime}s";
                    parts.Add(exp);
                }
                if (!string.IsNullOrWhiteSpace(IsoSpeed))
                {
                    var iso = IsoSpeed.StartsWith("ISO", StringComparison.OrdinalIgnoreCase) ? IsoSpeed : $"ISO {IsoSpeed}";
                    parts.Add(iso);
                }
                if (!string.IsNullOrWhiteSpace(FocalLength))
                {
                    parts.Add(FocalLength);
                }
                return string.Join("  |  ", parts);
            }
        }

        /// <summary>
        /// UI表示用にバイト単位からフォーマットされたファイルサイズ文字列を取得します（例: "12.5 MB"）。
        /// </summary>
        public string FormattedFileSize
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024.0):F2} MB";
            }
        }
    }
}

