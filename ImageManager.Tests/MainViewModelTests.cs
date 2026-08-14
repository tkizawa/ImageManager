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

        [Fact]
        public void UpdateSelectedImages_WhenSingleImage_SetsSelectedImageAndFlags()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string file1 = Path.Combine(_tempDirectory, "img1.jpg");
            File.WriteAllBytes(file1, new byte[] { 1 });
            var img1 = new ImageFile(file1);

            // Act
            vm.UpdateSelectedImages(new[] { img1 });

            // Assert
            Assert.Equal(1, vm.SelectedImagesCount);
            Assert.Same(img1, vm.SelectedImage);
            Assert.True(vm.IsSingleImageSelected);
            Assert.False(vm.HasMultipleImagesSelected);
            Assert.True(vm.HasAnyImageSelected);
        }

        [Fact]
        public void UpdateSelectedImages_WhenMultipleImages_ClearsSelectedImageAndSetsMultipleFlag()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string file1 = Path.Combine(_tempDirectory, "img1.jpg");
            string file2 = Path.Combine(_tempDirectory, "img2.jpg");
            File.WriteAllBytes(file1, new byte[] { 1 });
            File.WriteAllBytes(file2, new byte[] { 2 });
            var img1 = new ImageFile(file1);
            var img2 = new ImageFile(file2);

            // Act
            vm.UpdateSelectedImages(new[] { img1, img2 });

            // Assert - Preview is not displayed when multiple items are selected
            Assert.Equal(2, vm.SelectedImagesCount);
            Assert.Null(vm.SelectedImage);
            Assert.False(vm.IsSingleImageSelected);
            Assert.True(vm.HasMultipleImagesSelected);
            Assert.True(vm.HasAnyImageSelected);
            Assert.Contains("2 件", vm.MultiSelectionSummary);
        }

        [Fact]
        public void UpdateSelectedImages_WhenEmpty_ClearsSelectedImageAndFlags()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);

            // Act
            vm.UpdateSelectedImages(Array.Empty<ImageFile>());

            // Assert
            Assert.Equal(0, vm.SelectedImagesCount);
            Assert.Null(vm.SelectedImage);
            Assert.False(vm.IsSingleImageSelected);
            Assert.False(vm.HasMultipleImagesSelected);
            Assert.False(vm.HasAnyImageSelected);
        }

        [Fact]
        public async Task CopyFilesToFolderAsync_CopiesFilesToTargetDirectory()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string srcFile = Path.Combine(_tempDirectory, "source.jpg");
            File.WriteAllBytes(srcFile, new byte[] { 10, 20, 30 });

            string destDir = Path.Combine(_tempDirectory, "DestCopy");
            Directory.CreateDirectory(destDir);

            // Act
            int count = await vm.CopyFilesToFolderAsync(new[] { srcFile }, destDir);

            // Assert
            Assert.Equal(1, count);
            string expectedDest = Path.Combine(destDir, "source.jpg");
            Assert.True(File.Exists(expectedDest));
            Assert.True(File.Exists(srcFile)); // Original remains
        }

        [Fact]
        public async Task MoveFilesToFolderAsync_MovesFilesAndRemovesFromOriginal()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string srcFile = Path.Combine(_tempDirectory, "source_move.jpg");
            File.WriteAllBytes(srcFile, new byte[] { 40, 50 });

            string destDir = Path.Combine(_tempDirectory, "DestMove");
            Directory.CreateDirectory(destDir);

            var img = new ImageFile(srcFile);
            vm.Images.Add(img);
            vm.UpdateSelectedImages(new[] { img });

            // Act
            int count = await vm.MoveFilesToFolderAsync(new[] { srcFile }, destDir);

            // Assert
            Assert.Equal(1, count);
            string expectedDest = Path.Combine(destDir, "source_move.jpg");
            Assert.True(File.Exists(expectedDest));
            Assert.False(File.Exists(srcFile)); // Original is moved
            Assert.DoesNotContain(img, vm.Images);
        }

        [Fact]
        public async Task PasteFromClipboardAsync_WithCut_PerformsMoveOperation()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string file1 = Path.Combine(_tempDirectory, "cut_test.jpg");
            File.WriteAllBytes(file1, new byte[] { 1, 2, 3 });
            var img1 = new ImageFile(file1);

            string destDir = Path.Combine(_tempDirectory, "PasteTarget");
            Directory.CreateDirectory(destDir);

            vm.UpdateSelectedImages(new[] { img1 });
            vm.CutSelectedToClipboard();
            Assert.True(vm.IsClipboardCut);

            // Act
            int count = await vm.PasteFromClipboardAsync(destDir);

            // Assert
            Assert.Equal(1, count);
            Assert.True(File.Exists(Path.Combine(destDir, "cut_test.jpg")));
            Assert.False(File.Exists(file1));
            Assert.False(vm.IsClipboardCut);
            Assert.Empty(vm.ClipboardFilePaths);
        }

        [Fact]
        public async Task DeleteSelectedImagesAsync_DeletesFilesFromDiskAndImagesCollection()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            string file1 = Path.Combine(_tempDirectory, "delete_me.jpg");
            File.WriteAllBytes(file1, new byte[] { 9, 9, 9 });
            var img1 = new ImageFile(file1);

            vm.Images.Add(img1);
            vm.UpdateSelectedImages(new[] { img1 });

            // Act
            int count = await vm.DeleteSelectedImagesAsync();

            // Assert
            Assert.Equal(1, count);
            Assert.False(File.Exists(file1));
            Assert.DoesNotContain(img1, vm.Images);
            Assert.Empty(vm.SelectedImages);
        }

        [Fact]
        public void SetRatingCommand_SetsRatingOnSingleAndMultipleSelection()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var img1 = new ImageFile(Path.Combine(_tempDirectory, "img1.jpg"));
            var img2 = new ImageFile(Path.Combine(_tempDirectory, "img2.jpg"));
            vm.Images.Add(img1);
            vm.Images.Add(img2);

            // Single selected image
            vm.SelectedImage = img1;
            vm.SetRatingCommand.Execute(3);
            Assert.Equal(3, img1.Rating);

            // Multiple selected images
            vm.UpdateSelectedImages(new[] { img1, img2 });
            vm.SetRatingCommand.Execute(5);
            Assert.Equal(5, img1.Rating);
            Assert.Equal(5, img2.Rating);

            // Clear rating
            vm.SetRatingCommand.Execute(0);
            Assert.Equal(0, img1.Rating);
            Assert.Equal(0, img2.Rating);
        }

        [Fact]
        public void ToggleStarRatingCommand_TogglesRatingWhenSameStarClicked()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var img1 = new ImageFile(Path.Combine(_tempDirectory, "img1.jpg"));
            vm.Images.Add(img1);
            vm.SelectedImage = img1;

            // First click on Star 4 -> Sets to 4
            vm.ToggleStarRatingCommand.Execute(4);
            Assert.Equal(4, img1.Rating);

            // Second click on Star 4 -> Toggles to 0 (Clear)
            vm.ToggleStarRatingCommand.Execute(4);
            Assert.Equal(0, img1.Rating);

            // Click on Star 2 -> Sets to 2
            vm.ToggleStarRatingCommand.Execute(2);
            Assert.Equal(2, img1.Rating);
        }

        [Fact]
        public async Task RatingFilter_FiltersImagesByRating()
        {
            // Arrange
            string file1 = Path.Combine(_tempDirectory, "r3.jpg");
            string file2 = Path.Combine(_tempDirectory, "r4.jpg");
            string file3 = Path.Combine(_tempDirectory, "r0.jpg");
            File.WriteAllBytes(file1, new byte[] { 1 });
            File.WriteAllBytes(file2, new byte[] { 2 });
            File.WriteAllBytes(file3, new byte[] { 3 });

            _mockFileSystemService
                .Setup(f => f.GetImageFiles(_tempDirectory))
                .Returns(new List<string> { file1, file2, file3 });

            var db = new DatabaseService(Path.Combine(_tempDirectory, "test_filter.db"));

            // Pre-seed rating in DB
            db.UpdateImageRating(file1, 3);
            db.UpdateImageRating(file2, 4);
            db.UpdateImageRating(file3, 0);

            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object, db);

            // Set RatingFilterIndex = 3 (★3)
            vm.RatingFilterIndex = 3;
            await vm.SelectFolderFromTreeAsync(_tempDirectory);

            // Assert
            Assert.Single(vm.Images);
            Assert.Equal("r3.jpg", vm.Images[0].FileName);

            // Change to RatingFilterIndex = 4 (★4)
            vm.RatingFilterIndex = 4;
            if (vm.CurrentLoadTask != null) await vm.CurrentLoadTask;
            Assert.Single(vm.Images);
            Assert.Equal("r4.jpg", vm.Images[0].FileName);

            // Change to RatingFilterIndex = 6 (No Rating)
            vm.RatingFilterIndex = 6;
            if (vm.CurrentLoadTask != null) await vm.CurrentLoadTask;
            Assert.Single(vm.Images);
            Assert.Equal("r0.jpg", vm.Images[0].FileName);

            // Change to RatingFilterIndex = 0 (All)
            vm.RatingFilterIndex = 0;
            if (vm.CurrentLoadTask != null) await vm.CurrentLoadTask;
            Assert.Equal(3, vm.Images.Count);
        }

        [Fact]
        public async Task RatingFilter_And_Favorites_WorksAsAndCondition()
        {
            // Arrange
            string file1 = Path.Combine(_tempDirectory, "fav_r3.jpg");
            string file2 = Path.Combine(_tempDirectory, "nonfav_r3.jpg");
            string file3 = Path.Combine(_tempDirectory, "fav_r4.jpg");
            File.WriteAllBytes(file1, new byte[] { 1 });
            File.WriteAllBytes(file2, new byte[] { 2 });
            File.WriteAllBytes(file3, new byte[] { 3 });

            _mockFileSystemService
                .Setup(f => f.GetImageFiles(_tempDirectory))
                .Returns(new List<string> { file1, file2, file3 });

            var db = new DatabaseService(Path.Combine(_tempDirectory, "test_and_filter.db"));

            db.UpdateImageFavorite(file1, true);
            db.UpdateImageRating(file1, 3);

            db.UpdateImageFavorite(file2, false);
            db.UpdateImageRating(file2, 3);

            db.UpdateImageFavorite(file3, true);
            db.UpdateImageRating(file3, 4);

            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object, db);

            // Filter by Favorites AND Rating == 3
            vm.ShowOnlyFavorites = true;
            vm.RatingFilterIndex = 3;

            await vm.SelectFolderFromTreeAsync(_tempDirectory);

            // Assert: Only file1 satisfies BOTH conditions
            Assert.Single(vm.Images);
            Assert.Equal("fav_r3.jpg", vm.Images[0].FileName);
        }

        [Fact]
        public void RatingFilter_DynamicRemovalOnRatingChange()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var img1 = new ImageFile(Path.Combine(_tempDirectory, "dyn_r3.jpg")) { Rating = 3 };
            vm.Images.Add(img1);
            vm.SelectedImage = img1;
            vm.RatingFilterIndex = 3;

            // Change rating to 4 -> Should be removed from Images
            vm.SetRatingCommand.Execute(4);

            // Assert
            Assert.Empty(vm.Images);
        }
    }
}
