using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ImageManager.Services
{
    /// <summary>
    /// キャッシュディレクトリの使用状況（ファイル数・合計サイズ）を保持するクラス。
    /// </summary>
    public class CacheInfo
    {
        /// <summary>キャッシュディレクトリのパス</summary>
        public string CacheDirectory { get; set; } = string.Empty;

        /// <summary>キャッシュファイル総数</summary>
        public int FileCount { get; set; }

        /// <summary>キャッシュ合計サイズ（バイト単位）</summary>
        public long TotalSizeBytes { get; set; }
    }

    /// <summary>
    /// キャッシュ削除・クリーンアップ処理の実行結果（削除数・解放容量）を保持するクラス。
    /// </summary>
    public class CacheCleanResult
    {
        /// <summary>削除されたファイル数</summary>
        public int DeletedCount { get; set; }

        /// <summary>解放されたストレージ容量（バイト単位）</summary>
        public long FreedBytes { get; set; }
    }

    /// <summary>
    /// サムネイルキャッシュ（ディスクおよびメモリ）の管理・クリーンアップ・統計取得を提供する静的サービスクラス。
    /// 保持期間指定（日数）や最大容量制限に基づく自動クリーンアップアルゴリズムを含みます。
    /// </summary>
    public static class ThumbnailCacheService
    {
        /// <summary>デフォルトのキャッシュディレクトリパスを取得します。</summary>
        public static string DefaultCacheDirectory => RawThumbnailService.CacheDirectory;

        /// <summary>
        /// 指定された（またはデフォルトの）キャッシュディレクトリのファイル数および合計サイズを取得します。
        /// </summary>
        /// <param name="cacheDir">対象キャッシュディレクトリ（省略時はデフォルト）</param>
        /// <returns>キャッシュ情報オブジェクト <see cref="CacheInfo"/></returns>
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

        /// <summary>
        /// キャッシュ情報を非同期で取得します。
        /// </summary>
        /// <param name="cacheDir">対象キャッシュディレクトリ</param>
        /// <returns><see cref="CacheInfo"/> を返すタスク</returns>
        public static Task<CacheInfo> GetCacheInfoAsync(string? cacheDir = null)
        {
            return Task.Run(() => GetCacheInfo(cacheDir));
        }

        /// <summary>
        /// キャッシュディレクトリ内のすべてのキャッシュファイルおよびメモリキャッシュを全削除します。
        /// </summary>
        /// <param name="cacheDir">対象キャッシュディレクトリ</param>
        /// <returns>削除結果 <see cref="CacheCleanResult"/></returns>
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

            // メモリ上のキャッシュも併せて解放
            RawThumbnailService.ClearMemoryCache();

            return new CacheCleanResult
            {
                DeletedCount = deletedCount,
                FreedBytes = freedBytes
            };
        }

        /// <summary>
        /// すべてのキャッシュを非同期で全削除します。
        /// </summary>
        /// <param name="cacheDir">対象キャッシュディレクトリ</param>
        /// <returns>削除結果を返すタスク</returns>
        public static Task<CacheCleanResult> ClearAllCacheAsync(string? cacheDir = null)
        {
            return Task.Run(() => ClearAllCache(cacheDir));
        }

        /// <summary>
        /// 保持期間（日数）および最大容量制限（バイト数）に基づいてキャッシュのクリーンアップを実行します。
        /// 1. 指定日数を経過した古いファイルを優先的に削除
        /// 2. まだ最大サイズを超過している場合、古い順に上限内に収まるまで削除
        /// </summary>
        /// <param name="periodDays">保持日数（0以下の場合は日数による削除スキップ）</param>
        /// <param name="maxSizeBytes">最大許容サイズ（0以下の場合は容量による削除スキップ）</param>
        /// <param name="cacheDir">対象キャッシュディレクトリ</param>
        /// <returns>クリーンアップ結果 <see cref="CacheCleanResult"/></returns>
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

                // 1. 期間ベースの削除（periodDays日以上更新されていない古いファイルを削除）
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

                // 2. 容量ベースの削除（maxSizeBytesを超えている場合、更新日時が古いファイルから順に削減）
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

        /// <summary>
        /// 保持期間および最大容量に基づくキャッシュクリーンアップを非同期で実行します。
        /// </summary>
        /// <param name="periodDays">保持日数</param>
        /// <param name="maxSizeBytes">最大許容サイズ</param>
        /// <param name="cacheDir">対象キャッシュディレクトリ</param>
        /// <returns>クリーンアップ結果を返すタスク</returns>
        public static Task<CacheCleanResult> CleanCacheAsync(int periodDays, long maxSizeBytes, string? cacheDir = null)
        {
            return Task.Run(() => CleanCache(periodDays, maxSizeBytes, cacheDir));
        }

        /// <summary>
        /// クリーンアップが必要な状態（期限切れファイルまたは容量超過があるか）を判定します。
        /// </summary>
        /// <param name="periodDays">保持日数</param>
        /// <param name="maxSizeBytes">最大許容サイズ</param>
        /// <param name="cacheDir">対象キャッシュディレクトリ</param>
        /// <returns>クリーンアップ対象が存在する場合は true</returns>
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

        /// <summary>
        /// バイト数を適切な単位（B, KB, MB, GB, TB）の文字列にフォーマットします。
        /// </summary>
        /// <param name="bytes">バイト数</param>
        /// <returns>フォーマットされたサイズ文字列（例: "1.25 GB"）</returns>
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
