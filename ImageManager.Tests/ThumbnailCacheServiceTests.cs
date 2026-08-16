using System;
using System.IO;
using System.Threading.Tasks;
using ImageManager.Services;
using Xunit;

namespace ImageManager.Tests
{
    public class ThumbnailCacheServiceTests : IDisposable
    {
        private readonly string _tempCacheDir;

        public ThumbnailCacheServiceTests()
        {
            _tempCacheDir = Path.Combine(Path.GetTempPath(), "ImageManagerCacheTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempCacheDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempCacheDir))
            {
                try
                {
                    Directory.Delete(_tempCacheDir, true);
                }
                catch { }
            }
        }

        private string CreateDummyCacheFile(string fileName, int sizeInBytes, DateTime lastWriteTimeUtc)
        {
            string filePath = Path.Combine(_tempCacheDir, fileName);
            byte[] data = new byte[sizeInBytes];
            File.WriteAllBytes(filePath, data);
            File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc);
            return filePath;
        }

        [Fact]
        public void GetCacheInfo_EmptyDirectory_ReturnsZero()
        {
            var info = ThumbnailCacheService.GetCacheInfo(_tempCacheDir);
            Assert.Equal(_tempCacheDir, info.CacheDirectory);
            Assert.Equal(0, info.FileCount);
            Assert.Equal(0, info.TotalSizeBytes);
        }

        [Fact]
        public void GetCacheInfo_WithFiles_ReturnsCorrectCountAndSize()
        {
            CreateDummyCacheFile("file1.jpg", 1024, DateTime.UtcNow);
            CreateDummyCacheFile("file2.jpg", 2048, DateTime.UtcNow);

            var info = ThumbnailCacheService.GetCacheInfo(_tempCacheDir);
            Assert.Equal(2, info.FileCount);
            Assert.Equal(3072, info.TotalSizeBytes);
        }

        [Fact]
        public void ClearAllCache_DeletesAllFiles()
        {
            CreateDummyCacheFile("file1.jpg", 1000, DateTime.UtcNow);
            CreateDummyCacheFile("file2.jpg", 2000, DateTime.UtcNow);

            var result = ThumbnailCacheService.ClearAllCache(_tempCacheDir);
            Assert.Equal(2, result.DeletedCount);
            Assert.Equal(3000, result.FreedBytes);

            var info = ThumbnailCacheService.GetCacheInfo(_tempCacheDir);
            Assert.Equal(0, info.FileCount);
            Assert.Equal(0, info.TotalSizeBytes);
        }

        [Fact]
        public void CleanCache_PeriodBased_DeletesOnlyOldFiles()
        {
            // file1: 40 days old (should be deleted if period=30)
            CreateDummyCacheFile("file1.jpg", 1000, DateTime.UtcNow.AddDays(-40));
            // file2: 10 days old (should NOT be deleted if period=30)
            CreateDummyCacheFile("file2.jpg", 2000, DateTime.UtcNow.AddDays(-10));
            // file3: 2 days old (should NOT be deleted if period=30)
            CreateDummyCacheFile("file3.jpg", 4000, DateTime.UtcNow.AddDays(-2));

            var result = ThumbnailCacheService.CleanCache(30, 0, _tempCacheDir);
            Assert.Equal(1, result.DeletedCount);
            Assert.Equal(1000, result.FreedBytes);

            var info = ThumbnailCacheService.GetCacheInfo(_tempCacheDir);
            Assert.Equal(2, info.FileCount);
            Assert.Equal(6000, info.TotalSizeBytes);
            Assert.False(File.Exists(Path.Combine(_tempCacheDir, "file1.jpg")));
            Assert.True(File.Exists(Path.Combine(_tempCacheDir, "file2.jpg")));
            Assert.True(File.Exists(Path.Combine(_tempCacheDir, "file3.jpg")));
        }

        [Fact]
        public void CleanCache_CapacityBased_DeletesOldestFilesUntilUnderLimit()
        {
            // file1: oldest (10 days old), size 3000
            CreateDummyCacheFile("file1.jpg", 3000, DateTime.UtcNow.AddDays(-10));
            // file2: medium (5 days old), size 3000
            CreateDummyCacheFile("file2.jpg", 3000, DateTime.UtcNow.AddDays(-5));
            // file3: newest (1 day old), size 3000
            CreateDummyCacheFile("file3.jpg", 3000, DateTime.UtcNow.AddDays(-1));

            // Total = 9000. Limit = 5000.
            // Oldest file1 (3000) deleted -> total is 6000 > 5000.
            // Next oldest file2 (3000) deleted -> total is 3000 <= 5000.
            // file3 is kept.
            var result = ThumbnailCacheService.CleanCache(0, 5000, _tempCacheDir);
            Assert.Equal(2, result.DeletedCount);
            Assert.Equal(6000, result.FreedBytes);

            var info = ThumbnailCacheService.GetCacheInfo(_tempCacheDir);
            Assert.Equal(1, info.FileCount);
            Assert.Equal(3000, info.TotalSizeBytes);
            Assert.False(File.Exists(Path.Combine(_tempCacheDir, "file1.jpg")));
            Assert.False(File.Exists(Path.Combine(_tempCacheDir, "file2.jpg")));
            Assert.True(File.Exists(Path.Combine(_tempCacheDir, "file3.jpg")));
        }

        [Fact]
        public void CleanCache_CombinedPeriodAndCapacity_CleansCorrectly()
        {
            // file1: 60 days old, size 2000 (deleted by period >= 14)
            CreateDummyCacheFile("file1.jpg", 2000, DateTime.UtcNow.AddDays(-60));
            // file2: 20 days old, size 2000 (deleted by period >= 14)
            CreateDummyCacheFile("file2.jpg", 2000, DateTime.UtcNow.AddDays(-20));
            // file3: 5 days old, size 4000 (kept by period, but evaluated for capacity limit 3000)
            CreateDummyCacheFile("file3.jpg", 4000, DateTime.UtcNow.AddDays(-5));
            // file4: 1 day old, size 2000 (kept by period)
            CreateDummyCacheFile("file4.jpg", 2000, DateTime.UtcNow.AddDays(-1));

            // Period = 14 days: deletes file1 & file2 (total remaining: file3(4000) + file4(2000) = 6000)
            // Capacity limit = 3000: deletes file3(4000) (total remaining: file4(2000) <= 3000)
            var result = ThumbnailCacheService.CleanCache(14, 3000, _tempCacheDir);
            Assert.Equal(3, result.DeletedCount);
            Assert.Equal(8000, result.FreedBytes);

            var info = ThumbnailCacheService.GetCacheInfo(_tempCacheDir);
            Assert.Equal(1, info.FileCount);
            Assert.Equal(2000, info.TotalSizeBytes);
            Assert.True(File.Exists(Path.Combine(_tempCacheDir, "file4.jpg")));
        }

        [Fact]
        public void EvaluateCleanupRequired_ReturnsTrueWhenCriteriaMet()
        {
            CreateDummyCacheFile("file1.jpg", 5000, DateTime.UtcNow.AddDays(-10));

            // Older than 7 days -> true
            Assert.True(ThumbnailCacheService.EvaluateCleanupRequired(7, 0, _tempCacheDir));
            // Older than 14 days -> false
            Assert.False(ThumbnailCacheService.EvaluateCleanupRequired(14, 0, _tempCacheDir));
            // Over 4000 bytes -> true
            Assert.True(ThumbnailCacheService.EvaluateCleanupRequired(0, 4000, _tempCacheDir));
            // Over 6000 bytes -> false
            Assert.False(ThumbnailCacheService.EvaluateCleanupRequired(0, 6000, _tempCacheDir));
        }

        [Fact]
        public void FormatBytes_FormatsCorrectly()
        {
            Assert.Equal("0 B", ThumbnailCacheService.FormatBytes(0));
            Assert.Equal("512 B", ThumbnailCacheService.FormatBytes(512));
            Assert.Equal("1.5 KB", ThumbnailCacheService.FormatBytes(1536));
            Assert.Equal("10 MB", ThumbnailCacheService.FormatBytes(10 * 1024 * 1024));
            Assert.Equal("1 GB", ThumbnailCacheService.FormatBytes(1024L * 1024 * 1024));
        }
    }
}
