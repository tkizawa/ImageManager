using System;
using System.IO;
using ImageManager.Models;
using ImageManager.Services;
using Xunit;

namespace ImageManager.Tests
{
    public class DatabaseServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public DatabaseServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ImageManagerDbTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try { Directory.Delete(_tempDirectory, true); } catch { }
            }
        }

        [Fact]
        public void DatabaseService_InitializeDatabase_ExecutesWithoutErrors()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "init.db"));
            dbService.InitializeDatabase();
            // Succeeds without exception
        }

        [Fact]
        public void CalculateFileHash_ReturnsConsistentHashForSameFile()
        {
            string testFile = Path.Combine(_tempDirectory, "test.txt");
            File.WriteAllText(testFile, "Test image content data 12345");

            string hash1 = DatabaseService.CalculateFileHash(testFile);
            string hash2 = DatabaseService.CalculateFileHash(testFile);

            Assert.False(string.IsNullOrEmpty(hash1));
            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void SyncImageRecord_And_UpdateLibraryFullPaths_RelocatesAllFilesOnLibraryRootChange()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "relocate_root.db"));
            string libId = Guid.NewGuid().ToString();
            string oldRoot = Path.Combine(_tempDirectory, "OldPhotos");
            string newRoot = Path.Combine(_tempDirectory, "NewPhotos");

            Directory.CreateDirectory(oldRoot);
            string testFile = Path.Combine(oldRoot, "sample.jpg");
            File.WriteAllText(testFile, "Fake JPEG binary content");

            dbService.UpsertLibrary(libId, "My Travel Library", oldRoot);

            var imgFile = new ImageFile(testFile);
            string imageId = dbService.SyncImageRecord(imgFile, libId, oldRoot);

            Assert.False(string.IsNullOrEmpty(imageId));

            // Move the root directory
            Directory.CreateDirectory(newRoot);
            string movedFile = Path.Combine(newRoot, "sample.jpg");
            File.Move(testFile, movedFile);

            // Path Tracking Test: Update Library Root Path in DB
            dbService.UpsertLibrary(libId, "My Travel Library", newRoot);

            // Re-sync image at new location -> Should match relative path or hash!
            var movedImgFile = new ImageFile(movedFile);
            string syncedId = dbService.SyncImageRecord(movedImgFile, libId, newRoot);

            Assert.Equal(imageId, syncedId);
        }

        [Fact]
        public void SyncImageRecord_AutoRelocatesImage_WhenMovedToDifferentSubfolder()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "relocate_sub.db"));
            string libId = Guid.NewGuid().ToString();
            string rootDir = Path.Combine(_tempDirectory, "PhotosRoot");
            string subDir1 = Path.Combine(rootDir, "FolderA");
            string subDir2 = Path.Combine(rootDir, "FolderB");

            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);

            string originalFile = Path.Combine(subDir1, "photo.jpg");
            File.WriteAllText(originalFile, "Unique photo data 998877");

            dbService.UpsertLibrary(libId, "Photo Album", rootDir);

            var imgFile = new ImageFile(originalFile);
            string originalImageId = dbService.SyncImageRecord(imgFile, libId, rootDir);

            // User moves photo.jpg from FolderA to FolderB
            string movedFile = Path.Combine(subDir2, "photo_renamed.jpg");
            File.Move(originalFile, movedFile);

            // Auto-relocation Hash Matching Test: Sync newly discovered file at FolderB/photo_renamed.jpg
            var movedImgFile = new ImageFile(movedFile);
            string matchedId = dbService.SyncImageRecord(movedImgFile, libId, rootDir);

            // The DB record ID should be preserved across the file move & rename!
            Assert.Equal(originalImageId, matchedId);
        }

        [Fact]
        public void RelocateFolderPath_UpdatesImagePathsInDatabase()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "relocate_folder.db"));
            string libId = Guid.NewGuid().ToString();
            string oldFolder = Path.Combine(_tempDirectory, "TEST");
            string newFolder = Path.Combine(_tempDirectory, "TEST1");

            Directory.CreateDirectory(oldFolder);
            string testFile = Path.Combine(oldFolder, "wallpaper.jpg");
            File.WriteAllText(testFile, "Wallpaper data");

            dbService.UpsertLibrary(libId, "Wallpapers", oldFolder);
            var imgFile = new ImageFile(testFile);
            string imageId = dbService.SyncImageRecord(imgFile, libId, oldFolder);

            // Relocate folder from TEST to TEST1
            Directory.CreateDirectory(newFolder);
            string movedFile = Path.Combine(newFolder, "wallpaper.jpg");
            File.Move(testFile, movedFile);

            dbService.RelocateFolderPath(oldFolder, newFolder);

            var movedImgFile = new ImageFile(movedFile);
            string resyncedId = dbService.SyncImageRecord(movedImgFile, libId, newFolder);

            Assert.Equal(imageId, resyncedId);
        }

        [Fact]
        public void UpdateImageFavorite_PersistsFavoriteFlagAcrossResync()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "fav.db"));
            string folderPath = Path.Combine(_tempDirectory, "FavFolder");
            Directory.CreateDirectory(folderPath);
            string testFile = Path.Combine(folderPath, "fav_sample.jpg");
            File.WriteAllText(testFile, "Favorite Image Data 123");

            string libId = "folder_testlib";

            var imgFile = new ImageFile(testFile);
            dbService.SyncImageRecord(imgFile, libId, folderPath);

            // User marks image as favorite
            dbService.UpdateImageFavorite(testFile, true);

            // Simulate app restart / folder resync: create fresh ImageFile instance
            var resyncedImgFile = new ImageFile(testFile);
            dbService.SyncImageRecord(resyncedImgFile, libId, folderPath);

            Assert.True(resyncedImgFile.IsFavorite);
        }

        [Fact]
        public void UpdateImageRating_PersistsRatingAcrossResync()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "rating.db"));
            string folderPath = Path.Combine(_tempDirectory, "RatingFolder");
            Directory.CreateDirectory(folderPath);
            string testFile = Path.Combine(folderPath, "rating_sample.jpg");
            File.WriteAllText(testFile, "Rating Image Data 456");

            string libId = "folder_ratinglib";

            var imgFile = new ImageFile(testFile);
            dbService.SyncImageRecord(imgFile, libId, folderPath);

            // User sets rating to 4
            dbService.UpdateImageRating(testFile, 4);

            // Simulate resync
            var resyncedImgFile = new ImageFile(testFile);
            dbService.SyncImageRecord(resyncedImgFile, libId, folderPath);

            Assert.Equal(4, resyncedImgFile.Rating);
        }

        [Fact]
        public void BatchSyncImageRecords_And_GetFolderImageRecordsMap_WorksCorrectly()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "batch.db"));
            string folderPath = Path.Combine(_tempDirectory, "BatchFolder");
            Directory.CreateDirectory(folderPath);

            string file1 = Path.Combine(folderPath, "img1.jpg");
            string file2 = Path.Combine(folderPath, "img2.jpg");
            File.WriteAllText(file1, "img 1 content");
            File.WriteAllText(file2, "img 2 content");

            var img1 = new ImageFile(file1) { IsFavorite = true, Rating = 5 };
            var img2 = new ImageFile(file2) { IsFavorite = false, Rating = 3 };

            string libId = "batch_test_lib";

            // Batch sync
            dbService.BatchSyncImageRecords(new[] { img1, img2 }, libId, folderPath);

            // Fetch folder map in single query
            var map = dbService.GetFolderImageRecordsMap(folderPath);

            Assert.True(map.Count >= 2);
            Assert.True(map.ContainsKey(file1));
            Assert.True(map.ContainsKey(file2));

            Assert.True(map[file1].IsFavorite);
            Assert.Equal(5, map[file1].Rating);

            Assert.False(map[file2].IsFavorite);
            Assert.Equal(3, map[file2].Rating);
        }

        [Fact]
        public void UpdateImageFavorite_And_UpdateImageRating_PersistsStandaloneFileCorrectly()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "standalone_fav_rate.db"));
            string folderPath = Path.Combine(_tempDirectory, "StandaloneFolder");
            Directory.CreateDirectory(folderPath);

            string file1 = Path.Combine(folderPath, "standalone1.jpg");
            File.WriteAllText(file1, "standalone test image");

            // レコードがまだない状態で UpdateImageFavorite / UpdateImageRating を直接実行
            dbService.UpdateImageFavorite(file1, true);
            dbService.UpdateImageRating(file1, 5);

            // GetFolderImageRecordsMap で正しく取得できることを検証
            var map = dbService.GetFolderImageRecordsMap(folderPath);
            Assert.True(map.ContainsKey(file1));
            Assert.True(map[file1].IsFavorite);
            Assert.Equal(5, map[file1].Rating);

            // レーティングとお気に入りを変更
            dbService.UpdateImageFavorite(file1, false);
            dbService.UpdateImageRating(file1, 2);

            var updatedMap = dbService.GetFolderImageRecordsMap(folderPath);
            Assert.False(updatedMap[file1].IsFavorite);
            Assert.Equal(2, updatedMap[file1].Rating);
        }

        [Fact]
        public void UpdateImageFavorite_And_BatchSync_WithUnderscoresAndJapanesePaths_Works()
        {
            var dbService = new DatabaseService(Path.Combine(_tempDirectory, "japanese_underscore.db"));
            string folderPath = Path.Combine(_tempDirectory, @"OneDrive - WoodStream Networks\01_写真\00_現像");
            Directory.CreateDirectory(folderPath);

            string file1 = Path.Combine(folderPath, "PB250054.jpg");
            File.WriteAllText(file1, "dummy binary");

            // 1. フォルダを開いた時の初期バッチ同期（お気に入り=false）
            var img1 = new ImageFile(file1) { IsFavorite = false, Rating = 0 };
            dbService.BatchSyncImageRecords(new[] { img1 }, "lib_123", folderPath);

            // 2. ユーザーがお気に入りをONに設定
            dbService.UpdateImageFavorite(file1, true);

            // 3. 別フォルダに切り替えて、再度戻ってきたときの復元シミュレーション
            var map = dbService.GetFolderImageRecordsMap(folderPath);
            Assert.True(map.ContainsKey(file1));
            Assert.True(map[file1].IsFavorite);

            // 4. マージ後に再度バッチ同期が走ってもお気に入りが保持されること
            var reloadedImg = new ImageFile(file1);
            if (map.TryGetValue(reloadedImg.FilePath, out var rec))
            {
                reloadedImg.IsFavorite = rec.IsFavorite;
            }
            Assert.True(reloadedImg.IsFavorite);

            dbService.BatchSyncImageRecords(new[] { reloadedImg }, "lib_123", folderPath);

            var mapAfterSync = dbService.GetFolderImageRecordsMap(folderPath);
            Assert.True(mapAfterSync[file1].IsFavorite);
        }
    }
}
