using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ImageManager.Services
{
    public class CacheInfo
    {
        public string CacheDirectory { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }
    }

    public class CacheCleanResult
    {
        public int DeletedCount { get; set; }
        public long FreedBytes { get; set; }
    }

    public static class ThumbnailCacheService
    {
        public static string DefaultCacheDirectory => RawThumbnailService.CacheDirectory;

        public static CacheInfo GetCacheInfo(string? cacheDir = null)
        {
            string dir = cacheDir ?? DefaultCacheDirectory;
            if (!Directory.Exists(dir))
            {
                return new CacheInfo { CacheDirectory = dir, FileCount = 0, TotalSizeBytes = 0 };
            }

            try
            {
                var directoryInfo = new DirectoryInfo(dir);
                var files = directoryInfo.GetFiles();
                long totalBytes = files.Sum(f => f.Length);
                return new CacheInfo
                {
                    CacheDirectory = dir,
                    FileCount = files.Length,
                    TotalSizeBytes = totalBytes
                };
            }
            catch
            {
                return new CacheInfo { CacheDirectory = dir, FileCount = 0, TotalSizeBytes = 0 };
            }
        }

        public static Task<CacheInfo> GetCacheInfoAsync(string? cacheDir = null)
        {
            return Task.Run(() => GetCacheInfo(cacheDir));
        }

        public static CacheCleanResult ClearAllCache(string? cacheDir = null)
        {
            string dir = cacheDir ?? DefaultCacheDirectory;
            int deletedCount = 0;
            long freedBytes = 0;

            if (Directory.Exists(dir))
            {
                var directoryInfo = new DirectoryInfo(dir);
                foreach (var file in directoryInfo.GetFiles())
                {
                    try
                    {
                        long len = file.Length;
                        file.Delete();
                        deletedCount++;
                        freedBytes += len;
                    }
                    catch { }
                }
            }

            RawThumbnailService.ClearMemoryCache();

            return new CacheCleanResult
            {
                DeletedCount = deletedCount,
                FreedBytes = freedBytes
            };
        }

        public static Task<CacheCleanResult> ClearAllCacheAsync(string? cacheDir = null)
        {
            return Task.Run(() => ClearAllCache(cacheDir));
        }

        public static CacheCleanResult CleanCache(int periodDays, long maxSizeBytes, string? cacheDir = null)
        {
            string dir = cacheDir ?? DefaultCacheDirectory;
            int deletedCount = 0;
            long freedBytes = 0;

            if (!Directory.Exists(dir))
            {
                return new CacheCleanResult { DeletedCount = 0, FreedBytes = 0 };
            }

            try
            {
                var directoryInfo = new DirectoryInfo(dir);
                var files = directoryInfo.GetFiles().ToList();

                // 1. Period-based deletion (delete files older than periodDays)
                if (periodDays > 0)
                {
                    var cutoffDate = DateTime.UtcNow.AddDays(-periodDays);
                    var oldFiles = files.Where(f => f.LastWriteTimeUtc < cutoffDate).ToList();

                    foreach (var file in oldFiles)
                    {
                        try
                        {
                            long len = file.Length;
                            file.Delete();
                            deletedCount++;
                            freedBytes += len;
                            files.Remove(file);
                        }
                        catch { }
                    }
                }

                // 2. Capacity-based deletion (delete oldest files until under maxSizeBytes)
                if (maxSizeBytes > 0)
                {
                    long currentSize = files.Sum(f => f.Length);
                    if (currentSize > maxSizeBytes)
                    {
                        var sortedOldest = files.OrderBy(f => f.LastWriteTimeUtc).ToList();
                        foreach (var file in sortedOldest)
                        {
                            if (currentSize <= maxSizeBytes)
                                break;

                            try
                            {
                                long len = file.Length;
                                file.Delete();
                                deletedCount++;
                                freedBytes += len;
                                currentSize -= len;
                            }
                            catch { }
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    RawThumbnailService.ClearMemoryCache();
                }
            }
            catch { }

            return new CacheCleanResult
            {
                DeletedCount = deletedCount,
                FreedBytes = freedBytes
            };
        }

        public static Task<CacheCleanResult> CleanCacheAsync(int periodDays, long maxSizeBytes, string? cacheDir = null)
        {
            return Task.Run(() => CleanCache(periodDays, maxSizeBytes, cacheDir));
        }

        public static bool EvaluateCleanupRequired(int periodDays, long maxSizeBytes, string? cacheDir = null)
        {
            string dir = cacheDir ?? DefaultCacheDirectory;
            if (!Directory.Exists(dir)) return false;

            try
            {
                var directoryInfo = new DirectoryInfo(dir);
                var files = directoryInfo.GetFiles().ToList();
                if (files.Count == 0) return false;

                if (periodDays > 0)
                {
                    var cutoffDate = DateTime.UtcNow.AddDays(-periodDays);
                    if (files.Any(f => f.LastWriteTimeUtc < cutoffDate))
                    {
                        return true;
                    }
                }

                if (maxSizeBytes > 0)
                {
                    long currentSize = files.Sum(f => f.Length);
                    if (currentSize > maxSizeBytes)
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dbl = bytes;
            while (dbl >= 1024 && i < suffixes.Length - 1)
            {
                dbl /= 1024;
                i++;
            }
            return $"{dbl:0.##} {suffixes[i]}";
        }
    }
}
