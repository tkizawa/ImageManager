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
                HistoryFolders = new() { @"C:\TestFolder\Hist1" },
                ShowImageWindowInfo = false
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
            Assert.False(loadedSettings.ShowImageWindowInfo);
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

        [Fact]
        public void ExportAndImportSettings_ZipWithDatabase_WorksCorrectly()
        {
            var service = new SettingsService(_tempSettingsFilePath);
            var exportZipPath = Path.Combine(_tempDirectory, "exported_backup.zip");
            var originalSettings = new AppSettings
            {
                LastOpenedFolder = @"D:\Photos\2026",
                FavoriteFolders = new() { @"D:\Photos\2026\Summer" }
            };

            // Act
            bool exportResult = service.ExportSettings(exportZipPath, originalSettings);
            var importedSettings = service.ImportSettings(exportZipPath);

            // Assert
            Assert.True(exportResult);
            Assert.True(File.Exists(exportZipPath));
            Assert.NotNull(importedSettings);
            Assert.Equal(@"D:\Photos\2026", importedSettings!.LastOpenedFolder);
            Assert.Single(importedSettings.FavoriteFolders);
            Assert.Contains(@"D:\Photos\2026\Summer", importedSettings.FavoriteFolders);
        }

        [Fact]
        public void SaveAndLoad_PersistsExternalAppsCorrectly()
        {
            var service = new SettingsService(_tempSettingsFilePath);
            var settingsToSave = new AppSettings
            {
                ExternalApps = new()
                {
                    new ExternalApp { Name = "Photoshop", ExecutablePath = @"C:\Program Files\Adobe\Photoshop.exe", Arguments = "{path}" },
                    new ExternalApp { Name = "GIMP", ExecutablePath = @"C:\Program Files\GIMP 2\bin\gimp-2.10.exe", Arguments = "\"{path}\"" }
                }
            };

            service.Save(settingsToSave);
            var loadedSettings = service.Load();

            Assert.NotNull(loadedSettings);
            Assert.Equal(2, loadedSettings.ExternalApps.Count);
            Assert.Equal("Photoshop", loadedSettings.ExternalApps[0].Name);
            Assert.Equal(@"C:\Program Files\Adobe\Photoshop.exe", loadedSettings.ExternalApps[0].ExecutablePath);
            Assert.Equal("{path}", loadedSettings.ExternalApps[0].Arguments);
            Assert.Equal("GIMP", loadedSettings.ExternalApps[1].Name);
        }

        [Fact]
        public void SaveAndLoad_PersistsCacheCleanSettingsCorrectly()
        {
            var service = new SettingsService(_tempSettingsFilePath);
            var settingsToSave = new AppSettings
            {
                AutoCleanCacheOnExit = true,
                CacheCleanPeriodDays = 14,
                CacheCleanMaxSizeBytes = 5368709120 // 5 GB
            };

            service.Save(settingsToSave);
            var loadedSettings = service.Load();

            Assert.NotNull(loadedSettings);
            Assert.True(loadedSettings.AutoCleanCacheOnExit);
            Assert.Equal(14, loadedSettings.CacheCleanPeriodDays);
            Assert.Equal(5368709120, loadedSettings.CacheCleanMaxSizeBytes);
        }

        [Fact]
        public void SaveAndLoad_PersistsNavigationTabOrderAndSelectedTab()
        {
            var service = new SettingsService(_tempSettingsFilePath);
            var settingsToSave = new AppSettings
            {
                NavigationTabOrder = new() { "Library", "Folder", "Favorites", "History" },
                SelectedNavigationTab = "Library"
            };

            service.Save(settingsToSave);
            var loadedSettings = service.Load();

            Assert.NotNull(loadedSettings);
            Assert.Equal(4, loadedSettings.NavigationTabOrder.Count);
            Assert.Equal("Library", loadedSettings.NavigationTabOrder[0]);
            Assert.Equal("Folder", loadedSettings.NavigationTabOrder[1]);
            Assert.Equal("Favorites", loadedSettings.NavigationTabOrder[2]);
            Assert.Equal("History", loadedSettings.NavigationTabOrder[3]);
            Assert.Equal("Library", loadedSettings.SelectedNavigationTab);
        }
    }
}
