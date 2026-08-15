using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ImageManager.Models;

namespace ImageManager.Services
{
    public class DatabaseService
    {
        private static DatabaseService? _instance;
        public static DatabaseService Instance
        {
            get => _instance ??= new DatabaseService();
            set => _instance = value;
        }

        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseService(string? customDbPath = null)
        {
            _dbPath = customDbPath ?? GetDatabasePath();
            _connectionString = $"Data Source={_dbPath}";
            InitializeDatabase();
        }

        private static string GetDatabasePath()
        {
            string folderPath;
            try
            {
                folderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageManager");
            }
            Directory.CreateDirectory(folderPath);
            return Path.Combine(folderPath, "imagemanager.db");
        }

        public void InitializeDatabase()
        {
            try
            {
                ExecuteCreateTables();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 11) // SQLITE_CORRUPT
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(_dbPath))
                {
                    try { File.Delete(_dbPath); } catch { }
                }
                ExecuteCreateTables();
            }
        }

        private void ExecuteCreateTables()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Libraries (
                    LibraryId TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    RootPath TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Images (
                    ImageId TEXT PRIMARY KEY,
                    LibraryId TEXT NOT NULL,
                    RelativePath TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    FileSize INTEGER NOT NULL,
                    FileHash TEXT NOT NULL,
                    DateTaken TEXT,
                    Width INTEGER,
                    Height INTEGER,
                    Rating INTEGER DEFAULT 0,
                    IsFavorite INTEGER DEFAULT 0,
                    Category TEXT,
                    LastKnownFullPath TEXT NOT NULL,
                    LastScanTime TEXT,
                    FOREIGN KEY (LibraryId) REFERENCES Libraries(LibraryId)
                );

                CREATE TABLE IF NOT EXISTS ExifMetadata (
                    ImageId TEXT PRIMARY KEY,
                    CameraMake TEXT,
                    CameraModel TEXT,
                    LensModel TEXT,
                    FNumber TEXT,
                    ExposureTime TEXT,
                    IsoSpeed TEXT,
                    FocalLength TEXT,
                    FOREIGN KEY (ImageId) REFERENCES Images(ImageId)
                );

                CREATE TABLE IF NOT EXISTS ImageTags (
                    TagId INTEGER PRIMARY KEY AUTOINCREMENT,
                    ImageId TEXT NOT NULL,
                    TagName TEXT NOT NULL,
                    Category TEXT,
                    Confidence REAL,
                    FOREIGN KEY (ImageId) REFERENCES Images(ImageId)
                );

                CREATE INDEX IF NOT EXISTS idx_images_lib_rel ON Images(LibraryId, RelativePath);
                CREATE INDEX IF NOT EXISTS idx_images_hash ON Images(FileHash);
                CREATE INDEX IF NOT EXISTS idx_images_fullpath ON Images(LastKnownFullPath);
            ";
            cmd.ExecuteNonQuery();
        }

        #region File Hash Computation
        public static string CalculateFileHash(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists) return string.Empty;

                long length = info.Length;
                long ticks = info.LastWriteTimeUtc.Ticks;

                using var stream = File.OpenRead(filePath);
                byte[] buffer = new byte[Math.Min(8192, (int)Math.Min(length, int.MaxValue))];
                int read = stream.Read(buffer, 0, buffer.Length);

                using var md5 = MD5.Create();
                byte[] hashBytes = md5.ComputeHash(buffer, 0, read);
                string bufferHash = BitConverter.ToString(hashBytes).Replace("-", "");

                return $"{length}_{ticks}_{bufferHash}";
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion

        #region Library Operations & Path Tracking
        public void UpsertLibrary(string libraryId, string name, string rootPath)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // Check if root path has changed for existing library
            string? existingRoot = null;
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT RootPath FROM Libraries WHERE LibraryId = @id";
                checkCmd.Parameters.AddWithValue("@id", libraryId);
                existingRoot = checkCmd.ExecuteScalar() as string;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Libraries (LibraryId, Name, RootPath, CreatedAt)
                    VALUES (@id, @name, @rootPath, @createdAt)
                    ON CONFLICT(LibraryId) DO UPDATE SET
                        Name = @name,
                        RootPath = @rootPath;
                ";
                cmd.Parameters.AddWithValue("@id", libraryId);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@rootPath", rootPath);
                cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            // Path Tracking: If Library RootPath changed, update all resolved full paths in DB
            if (!string.IsNullOrEmpty(existingRoot) && !existingRoot.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateLibraryFullPaths(conn, libraryId, rootPath);
            }
        }

        public void UpdateLibraryFullPaths(SqliteConnection conn, string libraryId, string newRootPath)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ImageId, RelativePath FROM Images WHERE LibraryId = @libId";
            cmd.Parameters.AddWithValue("@libId", libraryId);

            var itemsToUpdate = new List<(string ImageId, string NewFullPath)>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string imageId = reader.GetString(0);
                    string relPath = reader.GetString(1);
                    string newFullPath = Path.Combine(newRootPath, relPath);
                    itemsToUpdate.Add((imageId, newFullPath));
                }
            }

            using var transaction = conn.BeginTransaction();
            foreach (var (imageId, newFullPath) in itemsToUpdate)
            {
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = "UPDATE Images SET LastKnownFullPath = @fullPath WHERE ImageId = @imageId";
                updateCmd.Parameters.AddWithValue("@fullPath", newFullPath);
                updateCmd.Parameters.AddWithValue("@imageId", imageId);
                updateCmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        public void DeleteLibrary(string libraryId)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM ImageTags WHERE ImageId IN (SELECT ImageId FROM Images WHERE LibraryId = @id)";
                cmd.Parameters.AddWithValue("@id", libraryId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM ExifMetadata WHERE ImageId IN (SELECT ImageId FROM Images WHERE LibraryId = @id)";
                cmd.Parameters.AddWithValue("@id", libraryId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Images WHERE LibraryId = @id";
                cmd.Parameters.AddWithValue("@id", libraryId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Libraries WHERE LibraryId = @id";
                cmd.Parameters.AddWithValue("@id", libraryId);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        #endregion

        #region Image Operations & Auto-Relocation Tracking
        public string SyncImageRecord(ImageFile imageFile, string libraryId, string libraryRootPath)
        {
            string fullPath = imageFile.FilePath;
            string relativePath = Path.GetRelativePath(libraryRootPath, fullPath);
            string fileHash = CalculateFileHash(fullPath);

            // Ensure library record exists in Libraries table for FOREIGN KEY constraint
            UpsertLibrary(libraryId, Path.GetFileName(libraryRootPath) ?? libraryId, libraryRootPath);

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // 1. Check if exact (LibraryId, RelativePath) exists OR LastKnownFullPath matches
            string? existingImageId = null;
            int isFav = 0;
            string? category = null;
            int rating = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT ImageId, IsFavorite, Category, Rating 
                    FROM Images 
                    WHERE (LibraryId = @libId AND LOWER(REPLACE(RelativePath, '/', '\')) = LOWER(REPLACE(@relPath, '/', '\'))) 
                       OR LOWER(REPLACE(LastKnownFullPath, '/', '\')) = LOWER(REPLACE(@fullPath, '/', '\'))";
                cmd.Parameters.AddWithValue("@libId", libraryId);
                cmd.Parameters.AddWithValue("@relPath", relativePath);
                cmd.Parameters.AddWithValue("@fullPath", fullPath);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    existingImageId = reader.GetString(0);
                    if (!reader.IsDBNull(1)) isFav = reader.GetInt32(1);
                    if (!reader.IsDBNull(2)) category = reader.GetString(2);
                    if (!reader.IsDBNull(3)) rating = reader.GetInt32(3);
                }
            }

            if (!string.IsNullOrEmpty(existingImageId))
            {
                imageFile.IsFavorite = (isFav == 1);
                if (rating > 0 && imageFile.Rating == 0)
                {
                    imageFile.Rating = rating;
                }
                if (!string.IsNullOrEmpty(category) && string.IsNullOrEmpty(imageFile.Category))
                {
                    imageFile.Category = category;
                }
                UpdateImageRecord(conn, existingImageId, fullPath, fileHash, imageFile);
                return existingImageId;
            }

            // 2. Path Tracking: Check if file with same FileHash exists whose LastKnownFullPath no longer exists on disk
            if (!string.IsNullOrEmpty(fileHash))
            {
                var candidates = new List<(string CandidateId, string OldFullPath, int CandidateFav, string? CandidateCategory, int CandidateRating)>();
                using (var searchCmd = conn.CreateCommand())
                {
                    searchCmd.CommandText = "SELECT ImageId, LastKnownFullPath, IsFavorite, Category, Rating FROM Images WHERE FileHash = @hash";
                    searchCmd.Parameters.AddWithValue("@hash", fileHash);

                    using var reader = searchCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        string candidateId = reader.GetString(0);
                        string oldFullPath = reader.GetString(1);
                        int candidateIsFav = !reader.IsDBNull(2) ? reader.GetInt32(2) : 0;
                        string? candidateCategory = !reader.IsDBNull(3) ? reader.GetString(3) : null;
                        int candidateRating = !reader.IsDBNull(4) ? reader.GetInt32(4) : 0;
                        candidates.Add((candidateId, oldFullPath, candidateIsFav, candidateCategory, candidateRating));
                    }
                }

                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate.OldFullPath))
                    {
                        // File was moved or renamed! Relocate DB record to new path to preserve tags & metadata
                        using var relocateCmd = conn.CreateCommand();
                        relocateCmd.CommandText = @"
                            UPDATE Images 
                            SET LibraryId = @libId, 
                                RelativePath = @relPath, 
                                FileName = @fileName, 
                                LastKnownFullPath = @fullPath, 
                                LastScanTime = @scanTime
                            WHERE ImageId = @candidateId";
                        relocateCmd.Parameters.AddWithValue("@libId", libraryId);
                        relocateCmd.Parameters.AddWithValue("@relPath", relativePath);
                        relocateCmd.Parameters.AddWithValue("@fileName", imageFile.FileName);
                        relocateCmd.Parameters.AddWithValue("@fullPath", fullPath);
                        relocateCmd.Parameters.AddWithValue("@scanTime", DateTime.UtcNow.ToString("o"));
                        relocateCmd.Parameters.AddWithValue("@candidateId", candidate.CandidateId);
                        relocateCmd.ExecuteNonQuery();

                        imageFile.IsFavorite = (candidate.CandidateFav == 1);
                        if (candidate.CandidateRating > 0 && imageFile.Rating == 0)
                        {
                            imageFile.Rating = candidate.CandidateRating;
                        }
                        if (!string.IsNullOrEmpty(candidate.CandidateCategory) && string.IsNullOrEmpty(imageFile.Category))
                        {
                            imageFile.Category = candidate.CandidateCategory;
                        }

                        return candidate.CandidateId;
                    }
                }
            }

            // 3. New Image Insertion
            string newImageId = Guid.NewGuid().ToString();
            using (var insertCmd = conn.CreateCommand())
            {
                insertCmd.CommandText = @"
                    INSERT INTO Images (
                        ImageId, LibraryId, RelativePath, FileName, FileSize, FileHash, 
                        DateTaken, Width, Height, Category, IsFavorite, Rating, LastKnownFullPath, LastScanTime
                    ) VALUES (
                        @imageId, @libId, @relPath, @fileName, @fileSize, @fileHash, 
                        @dateTaken, @width, @height, @category, @isFav, @rating, @fullPath, @scanTime
                    );
                ";
                insertCmd.Parameters.AddWithValue("@imageId", newImageId);
                insertCmd.Parameters.AddWithValue("@libId", libraryId);
                insertCmd.Parameters.AddWithValue("@relPath", relativePath);
                insertCmd.Parameters.AddWithValue("@fileName", imageFile.FileName);
                insertCmd.Parameters.AddWithValue("@fileSize", imageFile.FileSize);
                insertCmd.Parameters.AddWithValue("@fileHash", fileHash);
                insertCmd.Parameters.AddWithValue("@dateTaken", imageFile.DateTaken ?? string.Empty);
                insertCmd.Parameters.AddWithValue("@width", imageFile.ImageWidth);
                insertCmd.Parameters.AddWithValue("@height", imageFile.ImageHeight);
                insertCmd.Parameters.AddWithValue("@category", imageFile.Category ?? string.Empty);
                insertCmd.Parameters.AddWithValue("@isFav", imageFile.IsFavorite ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@rating", imageFile.Rating);
                insertCmd.Parameters.AddWithValue("@fullPath", fullPath);
                insertCmd.Parameters.AddWithValue("@scanTime", DateTime.UtcNow.ToString("o"));
                insertCmd.ExecuteNonQuery();
            }

            if (imageFile.IsExifLoaded)
            {
                SaveExifRecord(conn, newImageId, imageFile);
            }

            return newImageId;
        }

        private void UpdateImageRecord(SqliteConnection conn, string imageId, string fullPath, string fileHash, ImageFile imageFile)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Images SET 
                    FileSize = @fileSize,
                    FileHash = @fileHash,
                    DateTaken = @dateTaken,
                    LastKnownFullPath = @fullPath,
                    LastScanTime = @scanTime
                WHERE ImageId = @imageId;
            ";
            cmd.Parameters.AddWithValue("@fileSize", imageFile.FileSize);
            cmd.Parameters.AddWithValue("@fileHash", fileHash);
            cmd.Parameters.AddWithValue("@dateTaken", imageFile.DateTaken ?? string.Empty);
            cmd.Parameters.AddWithValue("@fullPath", fullPath);
            cmd.Parameters.AddWithValue("@scanTime", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@imageId", imageId);
            cmd.ExecuteNonQuery();

            if (imageFile.IsExifLoaded)
            {
                SaveExifRecord(conn, imageId, imageFile);
            }
        }

        public void UpdateExifRecord(ImageFile imageFile)
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                string? imageId = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ImageId FROM Images WHERE LOWER(REPLACE(LastKnownFullPath, '/', '\')) = LOWER(REPLACE(@path, '/', '\'))";
                    cmd.Parameters.AddWithValue("@path", imageFile.FilePath);
                    imageId = cmd.ExecuteScalar() as string;
                }

                if (!string.IsNullOrEmpty(imageId))
                {
                    SaveExifRecord(conn, imageId, imageFile);
                }
            }
            catch { }
        }

        public void SaveExifRecord(SqliteConnection conn, string imageId, ImageFile imageFile)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ExifMetadata (ImageId, CameraModel, LensModel, FNumber, ExposureTime, IsoSpeed, FocalLength)
                VALUES (@imageId, @camera, @lens, @fNumber, @exp, @iso, @focal)
                ON CONFLICT(ImageId) DO UPDATE SET
                    CameraModel = @camera,
                    LensModel = @lens,
                    FNumber = @fNumber,
                    ExposureTime = @exp,
                    IsoSpeed = @iso,
                    FocalLength = @focal;
            ";
            cmd.Parameters.AddWithValue("@imageId", imageId);
            cmd.Parameters.AddWithValue("@camera", imageFile.CameraModel ?? string.Empty);
            cmd.Parameters.AddWithValue("@lens", imageFile.Lens ?? string.Empty);
            cmd.Parameters.AddWithValue("@fNumber", imageFile.FNumber ?? string.Empty);
            cmd.Parameters.AddWithValue("@exp", imageFile.ExposureTime ?? string.Empty);
            cmd.Parameters.AddWithValue("@iso", imageFile.IsoSpeed ?? string.Empty);
            cmd.Parameters.AddWithValue("@focal", imageFile.FocalLength ?? string.Empty);
            cmd.ExecuteNonQuery();
        }

        public void UpdateImageCategory(string filePath, string category)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Images SET Category = @category WHERE LOWER(REPLACE(LastKnownFullPath, '/', '\')) = LOWER(REPLACE(@fullPath, '/', '\'))";
            cmd.Parameters.AddWithValue("@category", category);
            cmd.Parameters.AddWithValue("@fullPath", filePath);
            cmd.ExecuteNonQuery();
        }

        public void UpdateImageFavorite(string filePath, bool isFavorite)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Images SET IsFavorite = @fav WHERE LOWER(REPLACE(LastKnownFullPath, '/', '\')) = LOWER(REPLACE(@fullPath, '/', '\'))";
            cmd.Parameters.AddWithValue("@fav", isFavorite ? 1 : 0);
            cmd.Parameters.AddWithValue("@fullPath", filePath);
            int rows = cmd.ExecuteNonQuery();

            if (rows == 0)
            {
                string dirPath = Path.GetDirectoryName(filePath) ?? string.Empty;
                string libId = string.IsNullOrEmpty(dirPath) ? "standalone" : "folder_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dirPath.ToLowerInvariant()))).Substring(0, 16);
                UpsertLibrary(libId, string.IsNullOrEmpty(dirPath) ? "Standalone" : Path.GetFileName(dirPath), dirPath);

                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Images (ImageId, LibraryId, RelativePath, FileName, FileSize, FileHash, IsFavorite, LastKnownFullPath, LastScanTime)
                    VALUES (@imageId, @libId, @fileName, @fileName, 0, '', @fav, @fullPath, @scanTime)";
                insertCmd.Parameters.AddWithValue("@imageId", Guid.NewGuid().ToString());
                insertCmd.Parameters.AddWithValue("@libId", libId);
                insertCmd.Parameters.AddWithValue("@fileName", Path.GetFileName(filePath));
                insertCmd.Parameters.AddWithValue("@fav", isFavorite ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@fullPath", filePath);
                insertCmd.Parameters.AddWithValue("@scanTime", DateTime.UtcNow.ToString("o"));
                insertCmd.ExecuteNonQuery();
            }
        }

        public void UpdateImageRating(string filePath, int rating)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Images SET Rating = @rating WHERE LOWER(REPLACE(LastKnownFullPath, '/', '\')) = LOWER(REPLACE(@fullPath, '/', '\'))";
            cmd.Parameters.AddWithValue("@rating", rating);
            cmd.Parameters.AddWithValue("@fullPath", filePath);
            int rows = cmd.ExecuteNonQuery();

            if (rows == 0)
            {
                string dirPath = Path.GetDirectoryName(filePath) ?? string.Empty;
                string libId = string.IsNullOrEmpty(dirPath) ? "standalone" : "folder_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dirPath.ToLowerInvariant()))).Substring(0, 16);
                UpsertLibrary(libId, string.IsNullOrEmpty(dirPath) ? "Standalone" : Path.GetFileName(dirPath), dirPath);

                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Images (ImageId, LibraryId, RelativePath, FileName, FileSize, FileHash, Rating, LastKnownFullPath, LastScanTime)
                    VALUES (@imageId, @libId, @fileName, @fileName, 0, '', @rating, @fullPath, @scanTime)";
                insertCmd.Parameters.AddWithValue("@imageId", Guid.NewGuid().ToString());
                insertCmd.Parameters.AddWithValue("@libId", libId);
                insertCmd.Parameters.AddWithValue("@fileName", Path.GetFileName(filePath));
                insertCmd.Parameters.AddWithValue("@rating", rating);
                insertCmd.Parameters.AddWithValue("@fullPath", filePath);
                insertCmd.Parameters.AddWithValue("@scanTime", DateTime.UtcNow.ToString("o"));
                insertCmd.ExecuteNonQuery();
            }
        }

        public void ExportDatabase(string backupFilePath)
        {
            if (File.Exists(backupFilePath))
            {
                try { File.Delete(backupFilePath); } catch { }
            }

            try
            {
                string normalizedPath = backupFilePath.Replace('\\', '/').Replace("'", "''");
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"VACUUM INTO '{normalizedPath}';";
                cmd.ExecuteNonQuery();
                return;
            }
            catch { }

            try
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(_dbPath))
                {
                    File.Copy(_dbPath, backupFilePath, true);
                    return;
                }
            }
            catch { }

            try
            {
                using var backupConn = new SqliteConnection($"Data Source={backupFilePath}");
                backupConn.Open();
            }
            catch { }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        public void ImportDatabase(string sourceDbFilePath)
        {
            if (!File.Exists(sourceDbFilePath)) return;

            try
            {
                SqliteConnection.ClearAllPools();
                File.Copy(sourceDbFilePath, _dbPath, overwrite: true);
                InitializeDatabase();
            }
            catch { }
        }

        public void RelocateFolderPath(string oldFolderPath, string newFolderPath)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT ImageId, RelativePath, LastKnownFullPath FROM Images WHERE LastKnownFullPath LIKE @oldPath || '%';";
            cmd.Parameters.AddWithValue("@oldPath", oldFolderPath);

            var itemsToUpdate = new List<(string ImageId, string NewRelativePath, string NewFullPath)>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string imageId = reader.GetString(0);
                    string relPath = reader.GetString(1);
                    string fullPath = reader.GetString(2);

                    string oldDirName = Path.GetFileName(oldFolderPath.TrimEnd('\\', '/'));
                    string newDirName = Path.GetFileName(newFolderPath.TrimEnd('\\', '/'));

                    string newFullPath = fullPath.Replace(oldFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase);
                    string newRelPath = string.IsNullOrEmpty(oldDirName) ? relPath : relPath.Replace(oldDirName, newDirName, StringComparison.OrdinalIgnoreCase);

                    itemsToUpdate.Add((imageId, newRelPath, newFullPath));
                }
            }

            foreach (var item in itemsToUpdate)
            {
                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = "UPDATE Images SET RelativePath = @relPath, LastKnownFullPath = @fullPath WHERE ImageId = @imageId;";
                updateCmd.Parameters.AddWithValue("@relPath", item.NewRelativePath);
                updateCmd.Parameters.AddWithValue("@fullPath", item.NewFullPath);
                updateCmd.Parameters.AddWithValue("@imageId", item.ImageId);
                updateCmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        #endregion

        #region Batch Operations & High Performance Folder Loading
        public class CachedImageRecord
        {
            public string ImageId { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public string LastKnownFullPath { get; set; } = string.Empty;
            public bool IsFavorite { get; set; }
            public int Rating { get; set; }
            public string? Category { get; set; }
            public string? DateTaken { get; set; }
            public string FileHash { get; set; } = string.Empty;
        }

        public Dictionary<string, CachedImageRecord> GetFolderImageRecordsMap(string folderPath)
        {
            var dict = new Dictionary<string, CachedImageRecord>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(folderPath)) return dict;

            try
            {
                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                string folderPrefix = folderPath.TrimEnd('\\', '/') + "\\";
                cmd.CommandText = @"
                    SELECT ImageId, RelativePath, LastKnownFullPath, IsFavorite, Rating, Category, DateTaken, FileHash 
                    FROM Images 
                    WHERE LastKnownFullPath LIKE @folderPattern OR LastKnownFullPath LIKE @exactFolder;
                ";
                cmd.Parameters.AddWithValue("@folderPattern", folderPrefix + "%");
                cmd.Parameters.AddWithValue("@exactFolder", folderPath.TrimEnd('\\', '/') + "/%");

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var rec = new CachedImageRecord
                    {
                        ImageId = reader.GetString(0),
                        RelativePath = reader.GetString(1),
                        LastKnownFullPath = reader.GetString(2),
                        IsFavorite = !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
                        Rating = !reader.IsDBNull(4) ? reader.GetInt32(4) : 0,
                        Category = !reader.IsDBNull(5) ? reader.GetString(5) : null,
                        DateTaken = !reader.IsDBNull(6) ? reader.GetString(6) : null,
                        FileHash = !reader.IsDBNull(7) ? reader.GetString(7) : string.Empty,
                    };
                    dict[rec.LastKnownFullPath] = rec;
                }
            }
            catch { }
            return dict;
        }

        public void BatchSyncImageRecords(IReadOnlyList<ImageFile> images, string libraryId, string libraryRootPath)
        {
            if (images == null || images.Count == 0) return;

            try
            {
                UpsertLibrary(libraryId, Path.GetFileName(libraryRootPath) ?? libraryId, libraryRootPath);

                using var conn = new SqliteConnection(_connectionString);
                conn.Open();

                using var transaction = conn.BeginTransaction();

                var existingMap = new Dictionary<string, CachedImageRecord>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT ImageId, RelativePath, LastKnownFullPath, IsFavorite, Rating, Category, DateTaken, FileHash FROM Images WHERE LibraryId = @libId";
                    cmd.Parameters.AddWithValue("@libId", libraryId);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var rec = new CachedImageRecord
                        {
                            ImageId = reader.GetString(0),
                            RelativePath = reader.GetString(1),
                            LastKnownFullPath = reader.GetString(2),
                            IsFavorite = !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
                            Rating = !reader.IsDBNull(4) ? reader.GetInt32(4) : 0,
                            Category = !reader.IsDBNull(5) ? reader.GetString(5) : null,
                            DateTaken = !reader.IsDBNull(6) ? reader.GetString(6) : null,
                            FileHash = !reader.IsDBNull(7) ? reader.GetString(7) : string.Empty,
                        };
                        existingMap[rec.LastKnownFullPath] = rec;
                        existingMap[rec.RelativePath] = rec;
                    }
                }

                foreach (var imageFile in images)
                {
                    string fullPath = imageFile.FilePath;
                    string relPath = Path.GetRelativePath(libraryRootPath, fullPath);

                    if (existingMap.TryGetValue(fullPath, out var existing) || existingMap.TryGetValue(relPath, out existing))
                    {
                        // Update existing record
                        using var updateCmd = conn.CreateCommand();
                        updateCmd.Transaction = transaction;
                        updateCmd.CommandText = @"
                            UPDATE Images SET 
                                FileSize = @fileSize,
                                DateTaken = @dateTaken,
                                LastKnownFullPath = @fullPath,
                                LastScanTime = @scanTime
                            WHERE ImageId = @imageId;
                        ";
                        updateCmd.Parameters.AddWithValue("@fileSize", imageFile.FileSize);
                        updateCmd.Parameters.AddWithValue("@dateTaken", imageFile.DateTaken ?? string.Empty);
                        updateCmd.Parameters.AddWithValue("@fullPath", fullPath);
                        updateCmd.Parameters.AddWithValue("@scanTime", DateTime.UtcNow.ToString("o"));
                        updateCmd.Parameters.AddWithValue("@imageId", existing.ImageId);
                        updateCmd.ExecuteNonQuery();
                    }
                    else
                    {
                        // Insert new record
                        string newImageId = Guid.NewGuid().ToString();
                        string fileHash = CalculateFileHash(fullPath);
                        using var insertCmd = conn.CreateCommand();
                        insertCmd.Transaction = transaction;
                        insertCmd.CommandText = @"
                            INSERT INTO Images (
                                ImageId, LibraryId, RelativePath, FileName, FileSize, FileHash, 
                                DateTaken, Width, Height, Category, IsFavorite, Rating, LastKnownFullPath, LastScanTime
                            ) VALUES (
                                @imageId, @libId, @relPath, @fileName, @fileSize, @fileHash, 
                                @dateTaken, @width, @height, @category, @isFav, @rating, @fullPath, @scanTime
                            );
                        ";
                        insertCmd.Parameters.AddWithValue("@imageId", newImageId);
                        insertCmd.Parameters.AddWithValue("@libId", libraryId);
                        insertCmd.Parameters.AddWithValue("@relPath", relPath);
                        insertCmd.Parameters.AddWithValue("@fileName", imageFile.FileName);
                        insertCmd.Parameters.AddWithValue("@fileSize", imageFile.FileSize);
                        insertCmd.Parameters.AddWithValue("@fileHash", fileHash);
                        insertCmd.Parameters.AddWithValue("@dateTaken", imageFile.DateTaken ?? string.Empty);
                        insertCmd.Parameters.AddWithValue("@width", imageFile.ImageWidth);
                        insertCmd.Parameters.AddWithValue("@height", imageFile.ImageHeight);
                        insertCmd.Parameters.AddWithValue("@category", imageFile.Category ?? string.Empty);
                        insertCmd.Parameters.AddWithValue("@isFav", imageFile.IsFavorite ? 1 : 0);
                        insertCmd.Parameters.AddWithValue("@rating", imageFile.Rating);
                        insertCmd.Parameters.AddWithValue("@fullPath", fullPath);
                        insertCmd.Parameters.AddWithValue("@scanTime", DateTime.UtcNow.ToString("o"));
                        insertCmd.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
            catch { }
        }
        #endregion
    }
}
