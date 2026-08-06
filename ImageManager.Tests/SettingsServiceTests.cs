using System;
using System.IO;
using ImageManager.Models;
using ImageManager.Services;
using Xunit;

namespace ImageManager.Tests
{
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _tempSettingsFilePath;

        public SettingsServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ImageManagerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _tempSettingsFilePath = Path.Combine(_tempDirectory, "settings.json");
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
        public void Load_ReturnsDefaultSettings_WhenFileDoesNotExist()
        {
            // Arrange
            var service = new SettingsService(_tempSettingsFilePath);

            // Act
            var settings = service.Load();

            // Assert
            Assert.NotNull(settings);
            Assert.Empty(settings.FavoriteFolders);
            Assert.Empty(settings.HistoryFolders);
            Assert.Equal(string.Empty, settings.LastOpenedFolder);
        }

        [Fact]
        public void SaveAndLoad_PersistsSettingsCorrectly()
        {
            // Arrange
            var service = new SettingsService(_tempSettingsFilePath);
            var settingsToSave = new AppSettings
            {
                LastOpenedFolder = @"C:\TestFolder",
                FavoriteFolders = new() { @"C:\TestFolder\Fav1", @"C:\TestFolder\Fav2" },
                HistoryFolders = new() { @"C:\TestFolder\Hist1" }
            };

            // Act
            service.Save(settingsToSave);
            var loadedSettings = service.Load();

            // Assert
            Assert.NotNull(loadedSettings);
            Assert.Equal(@"C:\TestFolder", loadedSettings.LastOpenedFolder);
            Assert.Equal(2, loadedSettings.FavoriteFolders.Count);
            Assert.Contains(@"C:\TestFolder\Fav1", loadedSettings.FavoriteFolders);
            Assert.Single(loadedSettings.HistoryFolders);
        }

        [Fact]
        public void ExportAndImportSettings_WorksCorrectly()
        {
            // Arrange
            var service = new SettingsService(_tempSettingsFilePath);
            var exportFilePath = Path.Combine(_tempDirectory, "exported_settings.json");
            var originalSettings = new AppSettings
            {
                LastOpenedFolder = @"D:\Photos",
                FavoriteFolders = new() { @"D:\Photos\Vacation" }
            };

            // Act
            bool exportResult = service.ExportSettings(exportFilePath, originalSettings);
            var importedSettings = service.ImportSettings(exportFilePath);

            // Assert
            Assert.True(exportResult);
            Assert.True(File.Exists(exportFilePath));
            Assert.NotNull(importedSettings);
            Assert.Equal(@"D:\Photos", importedSettings!.LastOpenedFolder);
            Assert.Single(importedSettings.FavoriteFolders);
            Assert.Contains(@"D:\Photos\Vacation", importedSettings.FavoriteFolders);
        }

        [Fact]
        public void ImportSettings_ReturnsNull_ForNonExistentFile()
        {
            // Arrange
            var service = new SettingsService(_tempSettingsFilePath);
            var fakePath = Path.Combine(_tempDirectory, "non_existent.json");

            // Act
            var result = service.ImportSettings(fakePath);

            // Assert
            Assert.Null(result);
        }
    }
}
