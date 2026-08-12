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
                return cachedBytes;
            }

            byte[]? jpegBytes = null;

            // Tier 1: Windows WIC API (StorageFile thumbnail / BitmapDecoder preview)
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                using var stream = await storageFile.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);

                using var previewStream = await decoder.GetPreviewAsync();
                if (previewStream != null && previewStream.Size > 0)
                {
                    using var reader = new DataReader(previewStream);
                    await reader.LoadAsync((uint)previewStream.Size);
                    jpegBytes = new byte[previewStream.Size];
                    reader.ReadBytes(jpegBytes);
                }
            }
            catch { }

            // Tier 1b: StorageFile.GetThumbnailAsync
            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                try
                {
                    var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                    using var thumbnail = await storageFile.GetThumbnailAsync(
                        Windows.Storage.FileProperties.ThumbnailMode.PicturesView,
                        1024,
                        Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

                    if (thumbnail != null && thumbnail.Type == Windows.Storage.FileProperties.ThumbnailType.Image)
                    {
                        using var reader = new DataReader(thumbnail.GetInputStreamAt(0));
                        await reader.LoadAsync((uint)thumbnail.Size);
                        jpegBytes = new byte[thumbnail.Size];
                        reader.ReadBytes(jpegBytes);
                    }
                }
                catch { }
            }

            // Tier 2: Direct Binary JPEG SOI/EOI Extractor for RAW files
            if ((jpegBytes == null || jpegBytes.Length == 0) && IsRawFile(filePath))
            {
                try
                {
                    jpegBytes = await Task.Run(() => ExtractEmbeddedJpegByBinaryScan(filePath));
                }
                catch { }
            }

            if (jpegBytes != null && jpegBytes.Length > 0)
            {
                CacheJpegBytes(filePath, jpegBytes);
            }

            return jpegBytes;
        }

        public static byte[]? ExtractEmbeddedJpegByBinaryScan(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            long fileLength = fs.Length;
            if (fileLength < 100) return null;

            // Read header & scan for embedded JPEG SOI (0xFF, 0xD8, 0xFF)
            byte[] buffer = new byte[Math.Min(fileLength, 16 * 1024 * 1024)]; // Read up to 16MB preview area
            int bytesRead = fs.Read(buffer, 0, buffer.Length);

            List<(int start, int end)> candidateJpegs = new();

            for (int i = 0; i < bytesRead - 4; i++)
            {
                // Check SOI marker 0xFF 0xD8 0xFF
                if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8 && buffer[i + 2] == 0xFF)
                {
                    int soiIndex = i;
                    int eoiIndex = -1;

                    // Scan for EOI marker 0xFF 0xD9
                    for (int j = soiIndex + 100; j < bytesRead - 1; j++)
                    {
                        if (buffer[j] == 0xFF && buffer[j + 1] == 0xD9)
                        {
                            eoiIndex = j + 2;
                        }
                    }

                    if (eoiIndex > soiIndex + 1000) // Minimum valid JPEG size
                    {
                        candidateJpegs.Add((soiIndex, eoiIndex));
                    }
                }
            }

            if (candidateJpegs.Count > 0)
            {
                // Pick largest embedded JPEG payload (usually full-res or large preview)
                var bestCandidate = candidateJpegs.OrderByDescending(c => c.end - c.start).First();
                int length = bestCandidate.end - bestCandidate.start;
                byte[] jpeg = new byte[length];
                System.Buffer.BlockCopy(buffer, bestCandidate.start, jpeg, 0, length);
                return jpeg;
            }

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

        public static async Task LoadBitmapImageAsync(BitmapImage bitmapImage, string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            if (!IsRawFile(filePath))
            {
                try
                {
                    bitmapImage.UriSource = new Uri(filePath);
                    return;
                }
                catch { }
            }

            var jpegBytes = await GetEmbeddedJpegBytesAsync(filePath);
            if (jpegBytes != null && jpegBytes.Length > 0)
            {
                using var stream = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(jpegBytes);
                await writer.StoreAsync();
                stream.Seek(0);
                await bitmapImage.SetSourceAsync(stream);
            }
            else
            {
                try
                {
                    bitmapImage.UriSource = new Uri(filePath);
                }
                catch { }
            }
        }
    }
}
