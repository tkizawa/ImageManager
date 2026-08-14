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
            var dbService = new DatabaseService();
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
            var dbService = new DatabaseService();
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
            var dbService = new DatabaseService();
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
            var dbService = new DatabaseService();
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
            var dbService = new DatabaseService();
            string folderPath = Path.Combine(_tempDirectory, "FavFolder");
            Directory.CreateDirectory(folderPath);
            string testFile = Path.Combine(folderPath, "fav_sample.jpg");
            File.WriteAllText(testFile, "Favorite Image Data 123");

            string libId = "folder_testlib";

            var imgFile = new ImageFile(testFile);
            dbService.SyncImageRecord(imgFile, libId, folderPath);

            // User marks image as favorite
            imgFile.IsFavorite = true;

            // Simulate app restart / folder resync: create fresh ImageFile instance
            var resyncedImgFile = new ImageFile(testFile);
            dbService.SyncImageRecord(resyncedImgFile, libId, folderPath);

            Assert.True(resyncedImgFile.IsFavorite);
        }
    }
}
