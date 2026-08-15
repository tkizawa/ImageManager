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
    public class RawThumbnailService
    {
        private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng",
            ".orf", ".rw2", ".pef", ".raf", ".3fr", ".erf", ".mrw"
        };

        private static readonly HashSet<string> StandardExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
        };

        private static readonly ConcurrentDictionary<string, byte[]> MemoryCache = new();
        private static readonly int MaxCacheItems = 200;
        private static readonly ConcurrentQueue<string> CacheKeys = new();

        private static string? _cacheDirectory;
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

        public static string? GetCacheFilePath(string filePath)
        {
            string key = GetThumbnailCacheKey(filePath);
            if (string.IsNullOrEmpty(key)) return null;
            return Path.Combine(CacheDirectory, $"raw_v8_{key}.jpg");
        }

        public static bool IsRawFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = Path.GetExtension(filePath);
            return RawExtensions.Contains(ext);
        }

        public static bool IsSupportedImage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = Path.GetExtension(filePath);
            return StandardExtensions.Contains(ext) || RawExtensions.Contains(ext);
        }

        public static IEnumerable<string> GetSupportedExtensions()
        {
            return StandardExtensions.Concat(RawExtensions);
        }

        public static async Task<byte[]?> GetEmbeddedJpegBytesAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            if (MemoryCache.TryGetValue(filePath, out var cachedBytes))
            {
                if (cachedBytes.Length > 0)
                {
                    return cachedBytes;
                }
            }

            bool isRaw = IsRawFile(filePath);
            string? cacheFilePath = isRaw ? GetCacheFilePath(filePath) : null;

            if (cacheFilePath != null && File.Exists(cacheFilePath))
            {
                try
                {
                    byte[] diskBytes = await File.ReadAllBytesAsync(cacheFilePath);
                    if (diskBytes.Length > 1024)
                    {
                        CacheJpegBytes(filePath, diskBytes);
                        return diskBytes;
                    }
                }
                catch { }
            }

            byte[]? jpegBytes = null;

            if (isRaw)
            {
                try
                {
                    string ext = Path.GetExtension(filePath);
                    if (ext.Equals(".cr3", StringComparison.OrdinalIgnoreCase))
                    {
                        jpegBytes = await Task.Run(() => ExtractCr3PreviewJpeg(filePath));
                    }

                    if (jpegBytes == null || jpegBytes.Length == 0)
                    {
                        jpegBytes = await Task.Run(() => ExtractEmbeddedJpegByBinaryScan(filePath));
                    }

                    if (jpegBytes == null || jpegBytes.Length == 0)
                    {
                        jpegBytes = await DecodeRawUsingWindowsCodecAsync(filePath);
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

                if (cacheFilePath != null)
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

        public static async Task<byte[]?> DecodeRawUsingWindowsCodecAsync(string filePath, uint maxDimension = 1920)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);

                try
                {
                    using var thumbnail = await file.GetScaledImageAsThumbnailAsync(
                        Windows.Storage.FileProperties.ThumbnailMode.PicturesView,
                        maxDimension,
                        Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

                    if (thumbnail != null && thumbnail.Size > 100)
                    {
                        using var reader = new DataReader(thumbnail.GetInputStreamAt(0));
                        await reader.LoadAsync((uint)thumbnail.Size);
                        byte[] bytes = new byte[thumbnail.Size];
                        reader.ReadBytes(bytes);
                        if (bytes.Length > 100)
                        {
                            return bytes;
                        }
                    }
                }
                catch { }

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
                    uint outWidth = transform.ScaledWidth > 0 ? transform.ScaledWidth : decoder.PixelWidth;
                    uint outHeight = transform.ScaledHeight > 0 ? transform.ScaledHeight : decoder.PixelHeight;

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

                    // Valid JPEG SOF markers: SOF0 (C0: Baseline), SOF1 (C1: Extended), SOF2 (C2: Progressive)
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

        private static readonly System.Threading.SemaphoreSlim RawExtractionSemaphore = new(4, 4);

        public static async Task LoadBitmapImageAsync(BitmapImage bitmapImage, string filePath, int decodeWidth = 300)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            var dq = bitmapImage.DispatcherQueue ?? App.MainWindow?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

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

            // 1. Full resolution view (ImageWindow etc. where decodeWidth <= 0)
            if (decodeWidth <= 0)
            {
                if (!isRaw)
                {
                    RunOnUI(() => bitmapImage.UriSource = new Uri(filePath));
                    return;
                }

                // Check disk cache for RAW files
                string? cacheFilePath = GetCacheFilePath(filePath);
                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                    return;
                }

                // For RAW images in full viewer, load full embedded JPEG
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

                // Fallback for full viewer: Windows StorageFile scaled thumbnail
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

            // 2. Thumbnail view (decodeWidth > 0)
            RunOnUI(() => bitmapImage.DecodePixelWidth = decodeWidth);

            if (isRaw)
            {
                // Check disk cache for RAW files
                string? cacheFilePath = GetCacheFilePath(filePath);
                if (cacheFilePath != null && File.Exists(cacheFilePath))
                {
                    RunOnUI(() => bitmapImage.UriSource = new Uri(cacheFilePath));
                    return;
                }

                byte[]? jpegBytes = null;
                await RawExtractionSemaphore.WaitAsync();
                try
                {
                    // Check disk cache again inside semaphore
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

                // Fallback for RAW thumbnail: Windows StorageFile thumbnail
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
            else
            {
                // For standard image files (JPG, PNG, etc.), DecodePixelWidth handles high-quality fast native rendering
                RunOnUI(() => bitmapImage.UriSource = new Uri(filePath));
            }
        }
    }
}
