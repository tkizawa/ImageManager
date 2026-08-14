using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ImageManager.Models;

namespace ImageManager.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService(string? customSettingsFilePath = null)
        {
            if (!string.IsNullOrEmpty(customSettingsFilePath))
            {
                _settingsFilePath = customSettingsFilePath;
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(appData, "ImageManager");
                Directory.CreateDirectory(appFolder);
                _settingsFilePath = Path.Combine(appFolder, "settings.json");
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public AppSettings Load()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
                }
                catch { }
            }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }

        public bool ExportSettings(string filePath, AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions(_jsonOptions) { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);

                if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "ImageManagerBackup_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        string settingsJsonPath = Path.Combine(tempDir, "settings.json");
                        File.WriteAllText(settingsJsonPath, json);

                        string dbBackupPath = Path.Combine(tempDir, "imagemanager.db");
                        DatabaseService.Instance.ExportDatabase(dbBackupPath);

                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }

                        ZipFile.CreateFromDirectory(tempDir, filePath);
                        return true;
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir))
                        {
                            try { Directory.Delete(tempDir, true); } catch { }
                        }
                    }
                }
                else
                {
                    File.WriteAllText(filePath, json);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public AppSettings? ImportSettings(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "ImageManagerExtract_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        ZipFile.ExtractToDirectory(filePath, tempDir, overwriteFiles: true);

                        string dbBackupPath = Path.Combine(tempDir, "imagemanager.db");
                        if (File.Exists(dbBackupPath))
                        {
                            DatabaseService.Instance.ImportDatabase(dbBackupPath);
                        }

                        string settingsJsonPath = Path.Combine(tempDir, "settings.json");
                        if (File.Exists(settingsJsonPath))
                        {
                            var json = File.ReadAllText(settingsJsonPath);
                            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                        }
                        return null;
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir))
                        {
                            try { Directory.Delete(tempDir, true); } catch { }
                        }
                    }
                }
                else
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
