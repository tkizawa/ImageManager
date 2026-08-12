using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageManager.Models;
using ImageManager.Services;
using ImageManager.ViewModels;
using Moq;
using Xunit;

namespace ImageManager.Tests
{
    public class MainViewModelTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly Mock<IFileSystemService> _mockFileSystemService;
        private readonly Mock<ISettingsService> _mockSettingsService;

        public MainViewModelTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "MainViewModelTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);

            _mockFileSystemService = new Mock<IFileSystemService>();
            _mockSettingsService = new Mock<ISettingsService>();

            _mockSettingsService.Setup(s => s.Load()).Returns(new AppSettings());
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch { }
            }
        }

        [Fact]
        public void AddFavoriteFolder_AddsNewFolderToFavoriteFolders()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string folderPath = @"C:\TestFolder\Fav1";

            // Act
            vm.AddFavoriteFolderCommand.Execute(folderPath);

            // Assert
            Assert.Contains(folderPath, vm.FavoriteFolders);
        }

        [Fact]
        public void AddFavoriteFolder_DoesNotAddDuplicate()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string folderPath = @"C:\TestFolder\Fav1";
            vm.FavoriteFolders.Add(folderPath);

            // Act
            vm.AddFavoriteFolderCommand.Execute(folderPath);

            // Assert
            Assert.Single(vm.FavoriteFolders);
        }

        [Fact]
        public void RemoveFavoriteFolder_RemovesFolderFromFavoriteFolders()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string folderPath = @"C:\TestFolder\Fav1";
            vm.FavoriteFolders.Add(folderPath);

            // Act
            vm.RemoveFavoriteFolderCommand.Execute(folderPath);

            // Assert
            Assert.DoesNotContain(folderPath, vm.FavoriteFolders);
        }

        [Fact]
        public async Task SelectFolderFromTreeAsync_LoadsImagesAndUpdatesSettings()
        {
            // Arrange
            string file1 = Path.Combine(_tempDirectory, "test1.jpg");
            string file2 = Path.Combine(_tempDirectory, "test2.png");
            File.WriteAllBytes(file1, new byte[] { 0 });
            File.WriteAllBytes(file2, new byte[] { 0 });

            _mockFileSystemService
                .Setup(f => f.GetImageFiles(_tempDirectory))
                .Returns(new List<string> { file1, file2 });

            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);

            // Act
            await vm.SelectFolderFromTreeAsync(_tempDirectory);

            // Assert
            Assert.Equal(_tempDirectory, vm.CurrentFolderPath);
            Assert.Equal(2, vm.Images.Count);
            Assert.Contains(vm.HistoryFolders, h => h == _tempDirectory);
            _mockSettingsService.Verify(s => s.Save(It.Is<AppSettings>(a => a.LastOpenedFolder == _tempDirectory)), Times.AtLeastOnce());
        }

        [Fact]
        public async Task SortImagesAsync_SortsByLastWriteTimeAscendingAndDescending()
        {
            // Arrange
            string file1 = Path.Combine(_tempDirectory, "old.jpg");
            string file2 = Path.Combine(_tempDirectory, "new.jpg");
            File.WriteAllBytes(file1, new byte[] { 0 });
            File.WriteAllBytes(file2, new byte[] { 0 });

            File.SetLastWriteTime(file1, DateTime.Now.AddDays(-10));
            File.SetLastWriteTime(file2, DateTime.Now);

            _mockFileSystemService
                .Setup(f => f.GetImageFiles(_tempDirectory))
                .Returns(new List<string> { file1, file2 });

            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);

            // SortFieldIndex = 0 (LastWriteTime), SortDirectionIndex = 0 (Ascending)
            vm.SortFieldIndex = 0;
            vm.SortDirectionIndex = 0;

            // Act
            await vm.SelectFolderFromTreeAsync(_tempDirectory);

            // Assert Ascending
            Assert.Equal("old.jpg", vm.Images[0].FileName);
            Assert.Equal("new.jpg", vm.Images[1].FileName);

            // Act Descending
            vm.SortDirectionIndex = 1; // triggers OnSortDirectionIndexChanged -> SortImagesAsync

            // Wait a brief moment for async sorting task if needed
            await Task.Delay(200);

            // Assert Descending
            Assert.Equal("new.jpg", vm.Images[0].FileName);
            Assert.Equal("old.jpg", vm.Images[1].FileName);
        }

        [Fact]
        public void DirectoryNodeViewModel_WithCustomDisplayName_UsesDisplayNameAsName()
        {
            // Arrange
            string fullPath = @"C:\";
            string displayName = "OS (C:)";

            // Act
            var node = new DirectoryNodeViewModel(fullPath, displayName);

            // Assert
            Assert.Equal(fullPath, node.FullPath);
            Assert.Equal(displayName, node.Name);
        }

        [Fact]
        public void DirectoryNodeViewModel_WithoutDisplayName_UsesFullPathForRootDrive()
        {
            // Arrange
            string fullPath = @"C:\";

            // Act
            var node = new DirectoryNodeViewModel(fullPath);

            // Assert
            Assert.Equal(fullPath, node.FullPath);
            Assert.Equal(fullPath, node.Name);
        }

        [Fact]
        public void MainViewModel_LoadDrives_PopulatesFoldersWithFormattedNames()
        {
            // Arrange & Act
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);

            // Assert
            Assert.NotEmpty(vm.Folders);
            foreach (var driveNode in vm.Folders)
            {
                Assert.False(string.IsNullOrWhiteSpace(driveNode.Name));
                Assert.False(string.IsNullOrWhiteSpace(driveNode.FullPath));
            }
        }
    }
}
