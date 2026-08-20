using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ImageManager.Models;

namespace ImageManager.Services
{
    /// <summary>
    /// アプリケーション設定の永続化、インポート・エクスポート（ZIP一括バックアップ対応）を管理するサービスクラス。
    /// 設定ファイル保存先: AppData\Local\ImageManager\settings.json
    /// </summary>
    public class SettingsService : ISettingsService
    {
        /// <summary>設定ファイルの実ファイルパス</summary>
        private readonly string _settingsFilePath;

        /// <summary>
        /// <see cref="SettingsService"/> クラスの新しいインスタンスを初期化します。
        /// 保存先フォルダ（AppData\Local\ImageManager）が存在しない場合は自動作成します。
        /// </summary>
        /// <param name="customSettingsFilePath">テスト用等のカスタム設定ファイルパス（省略時はデフォルト保存先を使用）</param>
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
                // 規約に基づき AppData\Local\ImageManager に設定ファイルを配置
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(appData, "ImageManager");
                Directory.CreateDirectory(appFolder);
                _settingsFilePath = Path.Combine(appFolder, "settings.json");
            }
        }

        /// <summary>
        /// JSONシリアライズ設定。
        /// 日本語文字が \uXXXX 形式にエスケープされず可視テキストとして保存されるよう UnsafeRelaxedJsonEscaping を設定し、
        /// NaNやInfinityなどの浮動小数点リテラルもサポートします。
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        /// <summary>
        /// 設定ファイル（settings.json）から設定を読み込みます。
        /// </summary>
        /// <returns>読み込まれた <see cref="AppSettings"/>。存在しない・壊れている場合は新規インスタンス。</returns>
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

        /// <summary>
        /// 現在の設定オブジェクトを JSON 形式（UTF-8）でファイルへ保存します。
        /// </summary>
        /// <param name="settings">保存する設定オブジェクト</param>
        public void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }

        /// <summary>
        /// 設定情報をファイルにエクスポートします。
        /// .zip 拡張子の場合は設定（settings.json）とデータベース（imagemanager.db）を1つのZIPファイルにパッケージ化します。
        /// </summary>
        /// <param name="filePath">保存先ファイルパス（.json または .zip）</param>
        /// <param name="settings">エクスポートする設定オブジェクト</param>
        /// <returns>成功した場合は true、失敗時は false</returns>
        public bool ExportSettings(string filePath, AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions(_jsonOptions) { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);

                if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // 一時ディレクトリを作成して設定ファイルとDBバックアップを収集
                    string tempDir = Path.Combine(Path.GetTempPath(), "ImageManagerBackup_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        string settingsJsonPath = Path.Combine(tempDir, "settings.json");
                        File.WriteAllText(settingsJsonPath, json);

                        // データベースのバックアップファイルを出力
                        string dbBackupPath = Path.Combine(tempDir, "imagemanager.db");
                        DatabaseService.Instance.ExportDatabase(dbBackupPath);

                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }

                        // ZIPアーカイブとして圧縮出力
                        ZipFile.CreateFromDirectory(tempDir, filePath);
                        return true;
                    }
                    finally
                    {
                        // 一時ディレクトリのクリーンアップ
                        if (Directory.Exists(tempDir))
                        {
                            try { Directory.Delete(tempDir, true); } catch { }
                        }
                    }
                }
                else
                {
                    // 単一JSONファイルとして保存
                    File.WriteAllText(filePath, json);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 指定されたファイル（JSON または ZIPアーカイブ）から設定を復元・インポートします。
        /// </summary>
        /// <param name="filePath">読み込み元ファイルパス</param>
        /// <returns>復元された <see cref="AppSettings"/> オブジェクト。失敗時は null。</returns>
        public AppSettings? ImportSettings(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // ZIPアーカイブを一時ディレクトリに展開
                    string tempDir = Path.Combine(Path.GetTempPath(), "ImageManagerExtract_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        ZipFile.ExtractToDirectory(filePath, tempDir, overwriteFiles: true);

                        // データベースバックアップが存在すればインポートして復元
                        string dbBackupPath = Path.Combine(tempDir, "imagemanager.db");
                        if (File.Exists(dbBackupPath))
                        {
                            DatabaseService.Instance.ImportDatabase(dbBackupPath);
                        }

                        // 設定JSONファイルをデシリアライズ
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
                        // 一時ディレクトリのクリーンアップ
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
