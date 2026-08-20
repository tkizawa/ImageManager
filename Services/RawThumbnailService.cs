using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ImageManager.Services
{
    /// <summary>
    /// 各種デジタル一眼レフ・ミラーレスカメラのRAWファイル（CR2, CR3, ARW, NEF, DNG, RAF, RW2等）および
    /// 標準画像（JPG, PNG, WebP等）の超高速サムネイル抽出・デコード・回転補正・キャッシュを担うサービスクラス。
    /// 
    /// 高速化の仕組み：
    /// 1. RAWファイル内部に埋め込まれた高解像度プレビューJPEGのバイナリスキャン / ISO-BMFF (CR3) ボックス解析
    /// 2. 抽出されたJPEGのExif Orientationタグに基づくロスレス回転補正
    /// 3. ローカルSSDへのディスクキャッシュ（NAS・リムーバブルドライブ対応）
    /// 4. LRU方式のインメモリキャッシュおよびSemaphoreSlimによるCPU/I/O負荷制御
    /// </summary>
    public class RawThumbnailService
    {
        /// <summary>対応しているRAW画像拡張子一覧</summary>
        private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng",
            ".orf", ".rw2", ".pef", ".raf", ".3fr", ".erf", ".mrw"
        };

        /// <summary>対応している標準画像拡張子一覧</summary>
        private static readonly HashSet<string> StandardExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
        };

        /// <summary>メモリ上のJPEGバイト配列キャッシュ</summary>
        private static readonly ConcurrentDictionary<string, byte[]> MemoryCache = new();
        private static readonly int MaxCacheItems = 200;
        private static readonly ConcurrentQueue<string> CacheKeys = new();

        private static string? _cacheDirectory;

        /// <summary>
        /// サムネイルキャッシュの保存先ディレクトリ（AppData\Local\ImageManager\Thumbnails）を取得します。
        /// </summary>
        public static string CacheDirectory
        {
            get
            {
                if (_cacheDirectory == null)
                {
                    string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageManager");
                    _cacheDirectory = Path.Combine(baseDir, "Thumbnails");
                    Directory.CreateDirectory(_cacheDirectory);
                }
                return _cacheDirectory;
            }
        }

        /// <summary>
        /// メモリキャッシュ内のすべてのエントリを破棄・解放します。
        /// </summary>
        public static void ClearMemoryCache()
        {
            MemoryCache.Clear();
            while (CacheKeys.TryDequeue(out _)) { }
        }

        /// <summary>
        /// ファイルパス、ファイルサイズ、最終更新日時Ticksから一意のキャッシュキー（SHA256ハッシュ文字列）を生成します。
        /// </summary>
        /// <param name="filePath">対象ファイルパス</param>
        /// <returns>キャッシュキー文字列（SHA256 16進数文字列）</returns>
        public static string GetThumbnailCacheKey(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists) return string.Empty;
                string rawKey = $"{filePath}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
                using var sha = System.Security.Cryptography.SHA256.Create();
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawKey));
                return Convert.ToHexString(hash);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 指定された画像ファイルのキャッシュファイル絶対パスを取得します。
        /// </summary>
        /// <param name="filePath">画像ファイルパス</param>
        /// <returns>キャッシュファイルの絶対パス。非対応形式またはエラー時は null。</returns>
        public static string? GetCacheFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !IsSupportedImage(filePath)) return null;
            string key = GetThumbnailCacheKey(filePath);
            if (string.IsNullOrEmpty(key)) return null;
            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            return Path.Combine(CacheDirectory, $"raw_v10_{key}{ext}");
        }

        /// <summary>
        /// 指定されたファイルがローカルSSDキャッシュの対象であるかを判定します。
        /// RAW画像、UNCネットワーク共有パス、USBメモリ/SDカード、別ドライブ上の画像は
        /// パフォーマンス向上のためSSDキャッシュ対象と判定されます。
        /// </summary>
        /// <param name="filePath">対象ファイルパス</param>
        /// <returns>キャッシュすべき場合は true</returns>
        public static bool ShouldCacheFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            if (IsRawFile(filePath)) return true;

            try
            {
                // UNCネットワークパス (例: \\nas\photos\...)
                if (filePath.StartsWith(@"\\") || filePath.StartsWith("//"))
                    return true;

                string? fileRoot = Path.GetPathRoot(filePath);
                string? cacheRoot = Path.GetPathRoot(CacheDirectory);

                if (!string.IsNullOrEmpty(fileRoot))
                {
                    var driveInfo = new DriveInfo(fileRoot);
                    // ネットワークドライブ、USB/SDカード、光学ドライブ等はキャッシュ対象
                    if (driveInfo.DriveType == DriveType.Network ||
                        driveInfo.DriveType == DriveType.Removable ||
                        driveInfo.DriveType == DriveType.CDRom)
                    {
                        return true;
                    }
                }

                // キャッシュ先と異なるドライブ（別HDDや外付けなど）にある場合も高速化のためキャッシュ対象
                if (!string.IsNullOrEmpty(fileRoot) && !string.IsNullOrEmpty(cacheRoot) &&
                    !string.Equals(fileRoot, cacheRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 標準画像ファイルを一時ファイル経由で安全にキャッシュディレクトリへ非同期コピーします。
        /// </summary>
        /// <param name="filePath">元画像ファイルパス</param>
        /// <param name="cacheFilePath">コピー先キャッシュパス</param>
        public static async Task CacheStandardFileAsync(string filePath, string cacheFilePath)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(cacheFilePath)) return;
            if (File.Exists(cacheFilePath)) return;

            try
            {
                string tempCachePath = cacheFilePath + ".tmp";
                await using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
                await using (var destStream = new FileStream(tempCachePath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                {
                    await sourceStream.CopyToAsync(destStream);
                }
                
                if (File.Exists(tempCachePath))
                {
                    File.Move(tempCachePath, cacheFilePath, overwrite: true);
                }
            }
            catch
            {
                try
                {
                    string tempCachePath = cacheFilePath + ".tmp";
                    if (File.Exists(tempCachePath)) File.Delete(tempCachePath);
                }
                catch { }
            }
        }

        /// <summary>
        /// 指定されたパスのファイルがRAW画像であるかを判定します。
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <returns>RAWファイルであれば true</returns>
        public static bool IsRawFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = Path.GetExtension(filePath);
            return RawExtensions.Contains(ext);
        }

        /// <summary>
        /// アプリケーションがサポートしている画像形式（標準画像またはRAW画像）であるかを判定します。
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <returns>サポート形式であれば true</returns>
        public static bool IsSupportedImage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = Path.GetExtension(filePath);
            return StandardExtensions.Contains(ext) || RawExtensions.Contains(ext);
        }

        /// <summary>
        /// サポートされているすべての画像拡張子のコレクションを取得します。
        /// </summary>
        public static IEnumerable<string> GetSupportedExtensions()
        {
            return StandardExtensions.Concat(RawExtensions);
        }

        /// <summary>
        /// RAWファイルのExif IFD0から撮影時の回転情報（Orientation: 1〜8）を読み取ります。
        /// </summary>
        /// <param name="filePath">RAWファイルパス</param>
        /// <returns>Exif Orientation値（1: 正位置、6: 90度時計回り 等）</returns>
        public static int GetRawOrientation(string filePath)
        {
            try
            {
                var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(filePath);
                var ifd0 = directories.OfType<MetadataExtractor.Formats.Exif.ExifIfd0Directory>().FirstOrDefault();
                if (ifd0 != null && ifd0.ContainsTag(MetadataExtractor.Formats.Exif.ExifIfd0Directory.TagOrientation))
                {
                    var obj = ifd0.GetObject(MetadataExtractor.Formats.Exif.ExifIfd0Directory.TagOrientation);
                    if (obj != null)
                    {
                        return Convert.ToInt32(obj);
                    }
                }
            }
            catch { }
            return 1;
        }

        /// <summary>
        /// JPEGバイト配列をExif Orientation情報に従ってピクセル回転・反転変換し、補正済みJPEGバイト配列を返します。
        /// </summary>
        /// <param name="jpegBytes">元のJPEGバイト配列</param>
        /// <param name="orientation">Exif Orientation値（1〜8）</param>
        /// <returns>回転補正後のJPEGバイト配列</returns>
        public static async Task<byte[]> RotateJpegBytesAsync(byte[] jpegBytes, int orientation)
        {
            if (orientation <= 1 || orientation > 8) return jpegBytes;

            try
            {
                using var inStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(jpegBytes);
                    await writer.StoreAsync();
                }
                inStream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(inStream);
                var transform = new BitmapTransform();

                switch (orientation)
                {
                    case 3:
                        transform.Rotation = BitmapRotation.Clockwise180Degrees;
                        break;
                    case 6:
                        transform.Rotation = BitmapRotation.Clockwise90Degrees;
                        break;
                    case 8:
                        transform.Rotation = BitmapRotation.Clockwise270Degrees;
                        break;
                    case 2:
                        transform.Flip = BitmapFlip.Horizontal;
                        break;
                    case 4:
                        transform.Flip = BitmapFlip.Vertical;
                        break;
                    case 5:
                        transform.Rotation = BitmapRotation.Clockwise90Degrees;
                        transform.Flip = BitmapFlip.Horizontal;
                        break;
                    case 7:
                        transform.Rotation = BitmapRotation.Clockwise270Degrees;
                        transform.Flip = BitmapFlip.Horizontal;
                        break;
                    default:
                        return jpegBytes;
                    }

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);

                byte[] pixels = pixelData.DetachPixelData();
                uint outWidth = (orientation == 6 || orientation == 8 || orientation == 5 || orientation == 7) ? decoder.PixelHeight : decoder.PixelWidth;
                uint outHeight = (orientation == 6 || orientation == 8 || orientation == 5 || orientation == 7) ? decoder.PixelWidth : decoder.PixelHeight;

                using var outStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    outWidth,
                    outHeight,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();

                outStream.Seek(0);
                byte[] rotatedJpeg = new byte[outStream.Size];
                using var reader = new DataReader(outStream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)outStream.Size);
                reader.ReadBytes(rotatedJpeg);
                return rotatedJpeg;
            }
            catch
            {
                return jpegBytes;
            }
        }

        /// <summary>
        /// RAW画像から埋め込みJPEGプレビューを非同期抽出します。
        /// メモリキャッシュ、ディスクキャッシュ、CR3ボックス解析、バイナリスキャン、Windowsコーデックの順にフォールバックします。
        /// </summary>
        /// <param name="filePath">RAWファイルパス</param>
        /// <returns>抽出されたJPEGバイト配列。失敗時は null。</returns>
        public static async Task<byte[]?> GetEmbeddedJpegBytesAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            // 1. メモリキャッシュの確認
            if (MemoryCache.TryGetValue(filePath, out var cachedBytes))
            {
                if (cachedBytes.Length > 0)
                {
                    return cachedBytes;
                }
            }

            string? cacheFilePath = GetCacheFilePath(filePath);

            // 2. ディスクキャッシュの確認
            if (cacheFilePath != null && File.Exists(cacheFilePath))
            {
                try
                {
                    byte[] diskBytes = await File.ReadAllBytesAsync(cacheFilePath);
                    if (diskBytes.Length > 100)
                    {
                        CacheJpegBytes(filePath, diskBytes);
                        return diskBytes;
                    }
                }
                catch { }
            }

            bool isRaw = IsRawFile(filePath);
            byte[]? jpegBytes = null;

            if (isRaw)
            {
                try
                {
                    string ext = Path.GetExtension(filePath);
                    // Canon CR3 の場合は専用の ISO-BMFF ボックスパーサーを実行
                    if (ext.Equals(".cr3", StringComparison.OrdinalIgnoreCase))
                    {
                        jpegBytes = await Task.Run(() => ExtractCr3PreviewJpeg(filePath));
                    }

                    // 一般的なRAWの場合はバイナリスキャンで最大解像度の埋め込みJPEGを検出
                    if (jpegBytes == null || jpegBytes.Length == 0)
                    {
                        jpegBytes = await Task.Run(() => ExtractEmbeddedJpegByBinaryScan(filePath));
                    }

                    // 埋め込みJPEGが見つからない場合は Windows Imaging Component (WIC) でデコード
                    if (jpegBytes == null || jpegBytes.Length == 0)
                    {
                        jpegBytes = await DecodeRawUsingWindowsCodecAsync(filePath);
                    }

                    // Exif Orientation に基づく回転補正
                    if (jpegBytes != null && jpegBytes.Length > 0)
                    {
                        int orientation = GetRawOrientation(filePath);
                        if (orientation > 1)
                        {
                            jpegBytes = await RotateJpegBytesAsync(jpegBytes, orientation);
                        }
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    jpegBytes = await File.ReadAllBytesAsync(filePath);
                }
                catch { }
            }

            if (jpegBytes != null && jpegBytes.Length > 0)
            {
                CacheJpegBytes(filePath, jpegBytes);

                // RAWの場合は次回以降の高速化のためディスクキャッシュへ保存
                if (isRaw && cacheFilePath != null)
                {
                    try
                    {
                        await File.WriteAllBytesAsync(cacheFilePath, jpegBytes);
                    }
                    catch { }
                }
            }

            return jpegBytes;
        }

        /// <summary>
        /// 標準画像ファイルを指定解像度内に高品質リサイズしたサムネイルJPEGバイト配列を生成します。
        /// </summary>
        /// <param name="filePath">画像ファイルパス</param>
        /// <param name="maxDimension">長辺の最大ピクセル数（デフォルト: 1024）</param>
        /// <returns>サムネイルJPEGバイト配列</returns>
        public static async Task<byte[]?> CreateStandardImageThumbnailBytesAsync(string filePath, uint maxDimension = 1024)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(stream);

                var transform = new BitmapTransform();
                if (decoder.PixelWidth > maxDimension || decoder.PixelHeight > maxDimension)
                {
                    float scale = (float)maxDimension / Math.Max(decoder.PixelWidth, decoder.PixelHeight);
                    transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
                    transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
                    transform.InterpolationMode = BitmapInterpolationMode.Fant;
                }

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);

                byte[] pixels = pixelData.DetachPixelData();
                uint scaledW = transform.ScaledWidth > 0 ? transform.ScaledWidth : decoder.PixelWidth;
                uint scaledH = transform.ScaledHeight > 0 ? transform.ScaledHeight : decoder.PixelHeight;
                bool isSwapped = decoder.PixelWidth != decoder.OrientedPixelWidth;
                uint outWidth = isSwapped ? scaledH : scaledW;
                uint outHeight = isSwapped ? scaledW : scaledH;

                using var memStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    outWidth,
                    outHeight,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();

                memStream.Seek(0);
                byte[] encodedJpeg = new byte[memStream.Size];
                using var dr = new DataReader(memStream.GetInputStreamAt(0));
                await dr.LoadAsync((uint)memStream.Size);
                dr.ReadBytes(encodedJpeg);
                return encodedJpeg;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Windows の RAW コーデック拡張機能（WIC）を使用してRAW画像をデコードします。
        /// </summary>
        /// <param name="filePath">RAWファイルパス</param>
        /// <param name="maxDimension">長辺の最大ピクセル数（デフォルト: 1920）</param>
        /// <returns>デコードされたJPEGバイト配列</returns>
        public static async Task<byte[]?> DecodeRawUsingWindowsCodecAsync(string filePath, uint maxDimension = 1920)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);

                try
                {
                    using var stream = await file.OpenAsync(FileAccessMode.Read);
                    var decoder = await BitmapDecoder.CreateAsync(stream);

                    try
                    {
                        var thumbStream = await decoder.GetThumbnailAsync();
                        if (thumbStream != null && thumbStream.Size > 100)
                        {
                            using var reader = new DataReader(thumbStream.GetInputStreamAt(0));
                            await reader.LoadAsync((uint)thumbStream.Size);
                            byte[] bytes = new byte[thumbStream.Size];
                            reader.ReadBytes(bytes);
                            if (bytes.Length > 100)
                            {
                                return bytes;
                            }
                        }
                    }
                    catch { }

                    var transform = new BitmapTransform();
                    if (decoder.PixelWidth > maxDimension || decoder.PixelHeight > maxDimension)
                    {
                        float scale = (float)maxDimension / Math.Max(decoder.PixelWidth, decoder.PixelHeight);
                        transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
                        transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
                        transform.InterpolationMode = BitmapInterpolationMode.Fant;
                    }

                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.ColorManageToSRgb);

                    byte[] pixels = pixelData.DetachPixelData();
                    uint scaledW = transform.ScaledWidth > 0 ? transform.ScaledWidth : decoder.PixelWidth;
                    uint scaledH = transform.ScaledHeight > 0 ? transform.ScaledHeight : decoder.PixelHeight;
                    bool isSwapped = decoder.PixelWidth != decoder.OrientedPixelWidth;
                    uint outWidth = isSwapped ? scaledH : scaledW;
                    uint outHeight = isSwapped ? scaledW : scaledH;

                    using var memStream = new InMemoryRandomAccessStream();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, memStream);
                    encoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        outWidth,
                        outHeight,
                        96,
                        96,
                        pixels);
                    await encoder.FlushAsync();

                    memStream.Seek(0);
                    byte[] encodedJpeg = new byte[memStream.Size];
                    using var dr = new DataReader(memStream.GetInputStreamAt(0));
                    await dr.LoadAsync((uint)memStream.Size);
                    dr.ReadBytes(encodedJpeg);
                    return encodedJpeg;
                }
                catch { }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Canon CR3 (ISO-BMFF形式) ファイルの構造（PRVW, THMBボックス等）を走査してプレビューJPEGを抽出します。
        /// </summary>
        /// <param name="filePath">CR3ファイルパス</param>
        /// <returns>プレビューJPEGバイト配列</returns>
        public static byte[]? ExtractCr3PreviewJpeg(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            byte[]? boxResult = null;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = fs.Length;
                var candidates = new List<byte[]>();
                ScanForCr3Boxes(fs, 0, length, candidates);
                
                if (candidates.Count > 0)
                {
                    boxResult = PickBestJpeg(candidates);
                }
            }
            catch { }

            byte[]? scanResult = null;
            try
            {
                scanResult = ExtractEmbeddedJpegByBinaryScan(filePath);
            }
            catch { }

            if (boxResult != null && scanResult != null)
            {
                return PickBestJpeg(new[] { boxResult, scanResult });
            }

            return boxResult ?? scanResult;
        }

        /// <summary>
        /// CR3 ファイル内の ISO-BMFF ボックスを再帰的にパースし、PRVW / THMB 内の JPEG を収集します。
        /// </summary>
        private static void ScanForCr3Boxes(FileStream fs, long startOffset, long maxOffset, List<byte[]> foundJpegs)
        {
            fs.Position = startOffset;
            byte[] header = new byte[8];

            while (fs.Position < maxOffset - 8)
            {
                long boxStart = fs.Position;
                if (fs.Read(header, 0, 8) < 8) break;

                uint boxSize = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
                string boxType = System.Text.Encoding.ASCII.GetString(header, 4, 4);

                long actualBoxSize = boxSize;
                int headerSize = 8;

                if (boxSize == 1)
                {
                    byte[] largeBuf = new byte[8];
                    if (fs.Read(largeBuf, 0, 8) < 8) break;
                    actualBoxSize = (long)((ulong)largeBuf[0] << 56 | (ulong)largeBuf[1] << 48 | (ulong)largeBuf[2] << 40 | (ulong)largeBuf[3] << 32 |
                                           (ulong)largeBuf[4] << 24 | (ulong)largeBuf[5] << 16 | (ulong)largeBuf[6] << 8 | (ulong)largeBuf[7]);
                    headerSize = 16;
                }
                else if (boxSize == 0)
                {
                    actualBoxSize = maxOffset - boxStart;
                }

                if (actualBoxSize < 8 || boxStart + actualBoxSize > maxOffset)
                {
                    fs.Position = boxStart + 1;
                    continue;
                }

                if (boxType == "PRVW" || boxType == "THMB")
                {
                    long payloadSize = actualBoxSize - headerSize;
                    if (payloadSize > 100 && payloadSize <= 100 * 1024 * 1024)
                    {
                        byte[] payload = new byte[(int)payloadSize];
                        int read = fs.Read(payload, 0, (int)payloadSize);

                        for (int i = 0; i < read - 4; i++)
                        {
                            if (payload[i] == 0xFF && payload[i + 1] == 0xD8 && payload[i + 2] == 0xFF)
                            {
                                int jpegLen = read - i;
                                byte[] jpeg = new byte[jpegLen];
                                System.Buffer.BlockCopy(payload, i, jpeg, 0, jpegLen);
                                if (GetJpegDimensions(jpeg, out int w, out int h) && w > 0 && h > 0)
                                {
                                    foundJpegs.Add(jpeg);
                                    break;
                                }
                            }
                        }
                    }
                    fs.Position = boxStart + actualBoxSize;
                }
                else if (boxType == "uuid")
                {
                    long childStart = boxStart + headerSize + 16;
                    long innerEnd = boxStart + actualBoxSize;
                    if (childStart < innerEnd)
                    {
                        ScanForCr3Boxes(fs, childStart, innerEnd, foundJpegs);
                    }
                    fs.Position = innerEnd;
                }
                else if (boxType == "moov" || boxType == "trak" || boxType == "mdia" || boxType == "minf" || boxType == "stbl" || boxType == "dinf" || boxType == "edts")
                {
                    long innerEnd = boxStart + actualBoxSize;
                    ScanForCr3Boxes(fs, fs.Position, innerEnd, foundJpegs);
                    fs.Position = innerEnd;
                }
                else
                {
                    fs.Position = boxStart + actualBoxSize;
                }
            }
        }

        /// <summary>
        /// JPEGバイナリデータ内の SOF0 (0xC0), SOF1 (0xC1), SOF2 (0xC2) マーカーを解析して画像の横幅・高さを高速取得します。
        /// </summary>
        /// <param name="data">JPEGバイト列スパン</param>
        /// <param name="width">取得された横幅</param>
        /// <param name="height">取得された高さ</param>
        /// <returns>正常に寸法を取得できた場合は true</returns>
        public static bool GetJpegDimensions(ReadOnlySpan<byte> data, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return false;

            int pos = 2;
            while (pos < data.Length - 10)
            {
                if (data[pos] == 0xFF)
                {
                    byte marker = data[pos + 1];

                    if (marker == 0x00 || marker == 0xFF || (marker >= 0xD0 && marker <= 0xD7))
                    {
                        pos += 2;
                        continue;
                    }

                    if (marker == 0xD9 || marker == 0xDA)
                    {
                        break;
                    }

                    // 有効なJPEG SOFマーカー: SOF0 (C0: Baseline), SOF1 (C1: Extended), SOF2 (C2: Progressive)
                    if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                    {
                        int precision = data[pos + 4];
                        int h = (data[pos + 5] << 8) | data[pos + 6];
                        int w = (data[pos + 7] << 8) | data[pos + 8];
                        int components = data[pos + 9];

                        if ((precision == 8 || precision == 12) &&
                            (components == 1 || components == 3 || components == 4) &&
                            w >= 64 && w <= 12000 && h >= 64 && h <= 12000)
                        {
                            width = w;
                            height = h;
                            return true;
                        }
                    }

                    if (pos + 3 < data.Length)
                    {
                        int segLen = (data[pos + 2] << 8) | data[pos + 3];
                        if (segLen >= 2)
                        {
                            pos += 2 + segLen;
                            continue;
                        }
                    }
                }
                pos++;
            }

            return width > 0 && height > 0;
        }

        /// <summary>
        /// 複数の候補JPEGの中から最も解像度（画素数）の高いものを選択します。
        /// </summary>
        private static byte[] PickBestJpeg(IEnumerable<byte[]> jpegs)
        {
            byte[]? best = null;
            long maxPixels = -1;
            int maxLength = -1;

            foreach (var jpeg in jpegs)
            {
                if (jpeg == null || jpeg.Length < 100) continue;

                if (!GetJpegDimensions(jpeg, out int w, out int h)) continue;
                long pixels = (long)w * h;

                if (pixels > maxPixels || (pixels == maxPixels && jpeg.Length > maxLength))
                {
                    maxPixels = pixels;
                    maxLength = jpeg.Length;
                    best = jpeg;
                }
            }

            return best ?? jpegs.First();
        }

        private struct JpegCandidate
        {
            public int StartOffset;
            public int Length;
            public int Width;
            public int Height;
        }

        /// <summary>
        /// RAWファイルのバイナリストリームから SOI (0xFFD8) 〜 EOI (0xFFD9) パターンをスキャンし、
        /// 埋め込まれている最大解像度のプレビューJPEGを直接抽出します。
        /// </summary>
        /// <param name="filePath">RAWファイルパス</param>
        /// <returns>抽出されたJPEGバイト配列</returns>
        public static byte[]? ExtractEmbeddedJpegByBinaryScan(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long fileLength = fs.Length;
                if (fileLength < 100) return null;

                int bufferSize = (int)Math.Min(fileLength, 256 * 1024 * 1024);
                byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);

                try
                {
                    int bytesRead = 0;
                    while (bytesRead < bufferSize)
                    {
                        int r = fs.Read(buffer, bytesRead, bufferSize - bytesRead);
                        if (r <= 0) break;
                        bytesRead += r;
                    }

                    List<JpegCandidate> candidates = new();

                    for (int i = 0; i < bytesRead - 10; i++)
                    {
                        if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8 && buffer[i + 2] == 0xFF)
                        {
                            byte nextMarker = buffer[i + 3];
                            if (nextMarker >= 0xC0 && nextMarker <= 0xFE && nextMarker != 0xD8 && nextMarker != 0xFF)
                            {
                                int start = i;
                                int width = 0;
                                int height = 0;
                                int pos = start + 2;

                                while (pos < bytesRead - 10)
                                {
                                    if (buffer[pos] == 0xFF)
                                    {
                                        byte marker = buffer[pos + 1];
                                        if (marker == 0xDA || marker == 0xD9) break;
                                        if (marker == 0x00 || marker == 0xFF || (marker >= 0xD0 && marker <= 0xD7))
                                        {
                                            pos += 2;
                                            continue;
                                        }

                                        if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                                        {
                                            int precision = buffer[pos + 4];
                                            int h = (buffer[pos + 5] << 8) | buffer[pos + 6];
                                            int w = (buffer[pos + 7] << 8) | buffer[pos + 8];
                                            int components = buffer[pos + 9];

                                            if ((precision == 8 || precision == 12) &&
                                                (components == 1 || components == 3 || components == 4) &&
                                                w >= 64 && w <= 12000 && h >= 64 && h <= 12000)
                                            {
                                                height = h;
                                                width = w;
                                                break;
                                            }
                                        }

                                        if (pos + 3 < bytesRead)
                                        {
                                            int segLen = (buffer[pos + 2] << 8) | buffer[pos + 3];
                                            if (segLen >= 2)
                                            {
                                                pos += 2 + segLen;
                                                continue;
                                            }
                                        }
                                    }
                                    pos++;
                                }

                                if (width > 0 && height > 0)
                                {
                                    int eoi = -1;
                                    int maxScan = Math.Min(bytesRead - 1, start + 30 * 1024 * 1024);
                                    for (int j = pos; j < maxScan; j++)
                                    {
                                        if (buffer[j] == 0xFF && buffer[j + 1] == 0xD9)
                                        {
                                            eoi = j + 2;
                                            break;
                                        }
                                    }

                                    if (eoi > start)
                                    {
                                        int len = eoi - start;
                                        candidates.Add(new JpegCandidate
                                        {
                                            StartOffset = start,
                                            Length = len,
                                            Width = width,
                                            Height = height
                                        });
                                        i = eoi - 1;
                                    }
                                }
                            }
                        }
                    }

                    if (candidates.Count > 0)
                    {
                        var bestCandidate = candidates
                            .OrderByDescending(c => (long)c.Width * c.Height)
                            .ThenByDescending(c => c.Length)
                            .First();

                        byte[] jpeg = new byte[bestCandidate.Length];
                        System.Buffer.BlockCopy(buffer, bestCandidate.StartOffset, jpeg, 0, bestCandidate.Length);
                        return jpeg;
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// メモリキャッシュにJPEGバイト配列を登録（最大数超過時は最古のエントリを破棄）します。
        /// </summary>
        private static void CacheJpegBytes(string filePath, byte[] bytes)
        {
            if (MemoryCache.Count >= MaxCacheItems)
            {
                if (CacheKeys.TryDequeue(out var oldKey))
                {
                    MemoryCache.TryRemove(oldKey, out _);
                }
            }

            MemoryCache[filePath] = bytes;
            CacheKeys.Enqueue(filePath);
        }

        /// <summary>RAW抽出の同時実行数を制限するセマフォ（CPU過負荷防止）</summary>
        private static readonly System.Threading.SemaphoreSlim RawExtractionSemaphore = new(4, 4);

        /// <summary>
        /// <see cref="BitmapImage"/> に画像ファイル（RAWまたは通常画像）を非同期デコードして設定します。
        /// UIスレッドの安全性を保ちつつ、バックグラウンドスレッドでデコードを行います。
        /// </summary>
        /// <param name="bitmapImage">設定対象の <see cref="BitmapImage"/></param>
        /// <param name="filePath">画像ファイルの絶対パス</param>
        /// <param name="decodeWidth">デコード幅（0以下の場合は原寸表示）</param>
        public static async Task LoadBitmapImageAsync(BitmapImage bitmapImage, string filePath, int decodeWidth = 300)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            var dq = bitmapImage.DispatcherQueue ?? App.MainWindow?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // UIスレッド上でアクションを実行するローカル関数
            void RunOnUI(Action action)
            {
                if (dq != null && !dq.HasThreadAccess)
                {
                    dq.TryEnqueue(() =>
                    {
                        try { action(); } catch { }
                    });
                }
                else
                {
                    try { action(); } catch { }
                }
            }

            // バイト配列からストリームを介してUIスレッドでBitmapImageに設定
            void SetSourceFromBytesOnUI(byte[] bytes)
            {
                if (dq != null && !dq.HasThreadAccess)
                {
                    dq.TryEnqueue(async () =>
                    {
                        try
                        {
                            using var stream = new InMemoryRandomAccessStream();
                            using var writer = new DataWriter(stream.GetOutputStreamAt(0));
                            writer.WriteBytes(bytes);
                            await writer.StoreAsync();
                            stream.Seek(0);
                            await bitmapImage.SetSourceAsync(stream);
                        }
                        catch { }
                    });
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var stream = new InMemoryRandomAccessStream();
                            using var writer = new DataWriter(stream.GetOutputStreamAt(0));
                            writer.WriteBytes(bytes);
                            await writer.StoreAsync();
                            stream.Seek(0);
                            if (dq != null)
                            {
                                dq.TryEnqueue(async () =>
                                {
                                    try { await bitmapImage.SetSourceAsync(stream); } catch { }
                                });
                            }
                        }
                        catch { }
                    });
                }
            }

            bool isRaw = IsRawFile(filePath);
            string? cacheFilePath = GetCacheFilePath(filePath);

            // 1. 原寸・高解像度ビュー（decodeWidth <= 0 の場合: ImageWindow等）
            if (decodeWidth <= 0)
            {
                if (!isRaw)
                {
                    if (cacheFilePath != null && File.Exists(cacheFilePath))
                    {
                        RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                        return;
                    }

                    RunOnUI(() => bitmapImage.UriSource = new Uri(filePath));

                    if (cacheFilePath != null && ShouldCacheFile(filePath))
                    {
                        _ = Task.Run(() => CacheStandardFileAsync(filePath, cacheFilePath));
                    }
                    return;
                }

                // RAW画像のディスクキャッシュ確認
                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                    return;
                }

                // RAW画像からフルプレビューJPEGを取得
                byte[]? fullJpeg = null;
                await RawExtractionSemaphore.WaitAsync();
                try
                {
                    fullJpeg = await GetEmbeddedJpegBytesAsync(filePath);
                }
                finally
                {
                    RawExtractionSemaphore.Release();
                }

                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                    return;
                }

                if (fullJpeg != null && fullJpeg.Length > 0)
                {
                    SetSourceFromBytesOnUI(fullJpeg);
                    return;
                }

                // フォールバック: Windows StorageFile 経由のスケールサムネイル取得
                if (dq != null)
                {
                    dq.TryEnqueue(async () =>
                    {
                        try
                        {
                            var file = await StorageFile.GetFileFromPathAsync(filePath);
                            using var thumb = await file.GetScaledImageAsThumbnailAsync(
                                Windows.Storage.FileProperties.ThumbnailMode.PicturesView,
                                3840,
                                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);
                            if (thumb != null && thumb.Size > 0)
                            {
                                await bitmapImage.SetSourceAsync(thumb);
                            }
                        }
                        catch { }
                    });
                }
                return;
            }

            // 2. サムネイル表示（decodeWidth > 0）
            RunOnUI(() => bitmapImage.DecodePixelWidth = decodeWidth);

            if (!isRaw)
            {
                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                    return;
                }

                // 標準画像はハードウェアデコードを活用してURIから直接表示
                RunOnUI(() => bitmapImage.UriSource = new Uri(filePath));

                if (cacheFilePath != null && ShouldCacheFile(filePath))
                {
                    _ = Task.Run(() => CacheStandardFileAsync(filePath, cacheFilePath));
                }
                return;
            }

            RunOnUI(() => bitmapImage.DecodePixelWidth = decodeWidth);

            // RAW画像のディスクキャッシュ確認
            if (cacheFilePath != null && File.Exists(cacheFilePath))
            {
                RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                return;
            }

            if (isRaw)
            {
                byte[]? jpegBytes = null;
                await RawExtractionSemaphore.WaitAsync();
                try
                {
                    if (cacheFilePath != null && File.Exists(cacheFilePath))
                    {
                        RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                        return;
                    }
                    jpegBytes = await GetEmbeddedJpegBytesAsync(filePath);
                }
                finally
                {
                    RawExtractionSemaphore.Release();
                }

                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                    return;
                }

                if (jpegBytes != null && jpegBytes.Length > 0)
                {
                    SetSourceFromBytesOnUI(jpegBytes);
                    return;
                }

                // フォールバック: Windows StorageFile サムネイル
                if (dq != null)
                {
                    dq.TryEnqueue(async () =>
                    {
                        try
                        {
                            var file = await StorageFile.GetFileFromPathAsync(filePath);
                            using var thumb = await file.GetScaledImageAsThumbnailAsync(
                                Windows.Storage.FileProperties.ThumbnailMode.PicturesView,
                                (uint)decodeWidth,
                                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);
                            if (thumb != null && thumb.Size > 0)
                            {
                                await bitmapImage.SetSourceAsync(thumb);
                            }
                        }
                        catch { }
                    });
                }
            }
        }
    }
}
