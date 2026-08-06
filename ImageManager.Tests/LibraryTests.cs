using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageManager.Models;
using ImageManager.Services;
using ImageManager.ViewModels;
using Moq;
using Xunit;

namespace ImageManager.Tests
{
    public class LibraryTests
    {
        private readonly Mock<IFileSystemService> _mockFileSystemService;
        private readonly Mock<ISettingsService> _mockSettingsService;
        private AppSettings _currentSettings;

        public LibraryTests()
        {
            _mockFileSystemService = new Mock<IFileSystemService>();
            _mockSettingsService = new Mock<ISettingsService>();

            _currentSettings = new AppSettings();
            _mockSettingsService.Setup(s => s.Load()).Returns(() => _currentSettings);
            _mockSettingsService.Setup(s => s.Save(It.IsAny<AppSettings>())).Callback<AppSettings>(s => _currentSettings = s);
        }

        [Fact]
        public void CreateLibrary_AddsLibraryToLibrariesCollectionAndSaves()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);

            // Act
            var libNode = vm.CreateLibrary("My Photo Library");

            // Assert
            Assert.Single(vm.Libraries);
            Assert.Equal("My Photo Library", vm.Libraries.First().Name);
            Assert.True(vm.Libraries.First().IsLibrary);
            Assert.Equal("\uE8F1", vm.Libraries.First().IconGlyph);
            Assert.Single(_currentSettings.Libraries);
            Assert.Equal("My Photo Library", _currentSettings.Libraries.First().Name);
        }

        [Fact]
        public void AddFolderToLibrary_AddsChildFolderNodeAndSaves()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var libNode = vm.CreateLibrary("Vacation");
            string folderPath = @"C:\Photos\2026_Summer";

            // Act
            var folderNode = vm.AddFolderToLibrary(libNode, folderPath);

            // Assert
            Assert.NotNull(folderNode);
            Assert.Single(libNode.Children);
            Assert.Equal("2026_Summer", folderNode!.Name);
            Assert.Equal(folderPath, folderNode.FullPath);
            Assert.False(folderNode.IsLibrary);
            Assert.Equal("\uE8B7", folderNode.IconGlyph);
            Assert.Equal(libNode, folderNode.ParentLibrary);

            Assert.Single(_currentSettings.Libraries.First().FolderPaths);
            Assert.Equal(folderPath, _currentSettings.Libraries.First().FolderPaths.First());
        }

        [Fact]
        public void AddFolderToLibrary_DoesNotAddDuplicate()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var libNode = vm.CreateLibrary("Vacation");
            string folderPath = @"C:\Photos\2026_Summer";

            // Act
            vm.AddFolderToLibrary(libNode, folderPath);
            var duplicateResult = vm.AddFolderToLibrary(libNode, folderPath);

            // Assert
            Assert.Null(duplicateResult);
            Assert.Single(libNode.Children);
        }

        [Fact]
        public void RemoveFolderFromLibrary_RemovesChildAndSaves()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var libNode = vm.CreateLibrary("Vacation");
            string folderPath = @"C:\Photos\2026_Summer";
            var folderNode = vm.AddFolderToLibrary(libNode, folderPath);

            // Act
            vm.RemoveFolderFromLibrary(folderNode!);

            // Assert
            Assert.Empty(libNode.Children);
            Assert.Empty(_currentSettings.Libraries.First().FolderPaths);
        }

        [Fact]
        public void DeleteLibrary_RemovesLibraryAndSaves()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var libNode = vm.CreateLibrary("Temp Library");

            // Act
            vm.DeleteLibrary(libNode);

            // Assert
            Assert.Empty(vm.Libraries);
            Assert.Empty(_currentSettings.Libraries);
        }

        [Fact]
        public void RenameLibrary_UpdatesNameAndSaves()
        {
            // Arrange
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
            var libNode = vm.CreateLibrary("Old Name");

            // Act
            vm.RenameLibrary(libNode, "New Name");

            // Assert
            Assert.Equal("New Name", libNode.Name);
            Assert.Equal("New Name", _currentSettings.Libraries.First().Name);
        }

        [Fact]
        public async Task InitializeAsync_LoadsLibrariesFromSettings()
        {
            // Arrange
            _currentSettings.Libraries = new List<LibraryGroup>
            {
                new LibraryGroup
                {
                    Id = "lib-1",
                    Name = "Restored Library",
                    FolderPaths = new List<string> { @"C:\Photos\Album1", @"C:\Photos\Album2" }
                }
            };
            var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);

            // Act
            await vm.InitializeAsync();

            // Assert
            Assert.Single(vm.Libraries);
            var libNode = vm.Libraries.First();
            Assert.Equal("Restored Library", libNode.Name);
            Assert.Equal(2, libNode.Children.Count);
            Assert.Equal(@"C:\Photos\Album1", libNode.Children[0].FullPath);
            Assert.Equal("Album1", libNode.Children[0].Name);
            Assert.Equal(@"C:\Photos\Album2", libNode.Children[1].FullPath);
            Assert.Equal("Album2", libNode.Children[1].Name);
        }

        [Fact]
        public void LibraryFolder_LoadSubDirectories_ExpandsSubDirectories()
        {
            // Arrange
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LibraryTest_" + System.Guid.NewGuid().ToString("N"));
            string subDir1 = System.IO.Path.Combine(tempDir, "Sub1");
            string subDir2 = System.IO.Path.Combine(tempDir, "Sub2");
            System.IO.Directory.CreateDirectory(subDir1);
            System.IO.Directory.CreateDirectory(subDir2);

            try
            {
                var vm = new MainViewModel(_mockFileSystemService.Object, _mockSettingsService.Object);
                var libNode = vm.CreateLibrary("Test Library");

                // Act
                var folderNode = vm.AddFolderToLibrary(libNode, tempDir);
                Assert.NotNull(folderNode);

                // IsExpanded = true triggers LoadSubDirectories
                folderNode!.IsExpanded = true;

                // Assert
                Assert.Equal(2, folderNode.Children.Count);
                Assert.Contains(folderNode.Children, c => c.Name == "Sub1");
                Assert.Contains(folderNode.Children, c => c.Name == "Sub2");
                Assert.False(folderNode.Children.First().IsLibrary);
            }
            finally
            {
                if (System.IO.Directory.Exists(tempDir))
                {
                    try { System.IO.Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
