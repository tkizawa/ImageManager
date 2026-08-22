using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ImageManager.Services
{
    /// <summary>
    /// アプリケーション全体の診断ログおよびクラッシュログの収集・ファイル出力を管理するサービスクラス。
    /// プロジェクト規約に従い、%LOCALAPPDATA%\ImageManager\Logs に UTF-8 で安全に追記保存します。
    /// </summary>
    public static class AppLogService
    {
        private static readonly object LockObject = new();
        private static string? _logDirectory;

        /// <summary>
        /// ログファイルの保存先ディレクトリ（AppData\Local\ImageManager\Logs）を取得します。
        /// </summary>
        public static string LogDirectory
        {
            get
            {
                if (_logDirectory == null)
                {
                    string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageManager");
                    _logDirectory = Path.Combine(baseDir, "Logs");
                    try
                    {
                        Directory.CreateDirectory(_logDirectory);
                    }
                    catch
                    {
                        // 作成失敗時はフォールバック
                    }
                }
                return _logDirectory;
            }
        }

        /// <summary>
        /// アプリケーション診断ログ（app.log）のファイルパスを取得します。
        /// </summary>
        public static string AppLogFilePath => Path.Combine(LogDirectory, "app.log");

        /// <summary>
        /// クラッシュログ（crash.log）のファイルパスを取得します。
        /// </summary>
        public static string CrashLogFilePath => Path.Combine(LogDirectory, "crash.log");

        /// <summary>
        /// 診断メッセージを app.log に追記します。
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        /// <param name="level">ログレベル（INFO, WARN, ERROR 等）</param>
        public static void Log(string message, string level = "INFO")
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

                lock (LockObject)
                {
                    File.AppendAllText(AppLogFilePath, logLine, Encoding.UTF8);
                }
            }
            catch
            {
                // ロギング処理自体の例外でアプリをクラッシュさせない
            }
        }

        /// <summary>
        /// 発生した例外情報を app.log に記録します。
        /// </summary>
        /// <param name="context">例外が発生した処理コンテキスト名</param>
        /// <param name="ex">発生した例外インスタンス</param>
        public static void LogException(string context, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"--- Exception in {context} ---");
                sb.AppendLine($"Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                    sb.AppendLine($"Inner StackTrace:\n{ex.InnerException.StackTrace}");
                }

                Log(sb.ToString(), "ERROR");
            }
            catch
            {
            }
        }

        /// <summary>
        /// 未処理例外などの致命的エラー情報を crash.log および app.log に記録します。
        /// </summary>
        /// <param name="source">例外の発生元（UI, AppDomain, TaskScheduler 等）</param>
        /// <param name="ex">発生した例外オブジェクト</param>
        public static void LogFatalCrash(string source, object? ex)
        {
            try
            {
                var sb = new StringBuilder();
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string appVersion = typeof(AppLogService).Assembly.GetName().Version?.ToString() ?? "Unknown";

                sb.AppendLine("==================================================");
                sb.AppendLine($"FATAL CRASH DETECTED - {timestamp}");
                sb.AppendLine($"Source: {source}");
                sb.AppendLine($"App Version: {appVersion}");
                sb.AppendLine($"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
                sb.AppendLine($".NET: {Environment.Version}");
                sb.AppendLine($"Working Set: {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");
                sb.AppendLine("--------------------------------------------------");

                if (ex is Exception exception)
                {
                    sb.AppendLine($"Exception Type: {exception.GetType().FullName}");
                    sb.AppendLine($"Message: {exception.Message}");
                    sb.AppendLine($"StackTrace:\n{exception.StackTrace}");
                    if (exception.InnerException != null)
                    {
                        sb.AppendLine($"Inner Exception: {exception.InnerException.GetType().FullName}: {exception.InnerException.Message}");
                        sb.AppendLine($"Inner StackTrace:\n{exception.InnerException.StackTrace}");
                    }
                }
                else if (ex != null)
                {
                    sb.AppendLine($"Exception Object: {ex}");
                }
                else
                {
                    sb.AppendLine("Exception object was null.");
                }
                sb.AppendLine("==================================================");
                sb.AppendLine();

                string content = sb.ToString();

                lock (LockObject)
                {
                    File.AppendAllText(CrashLogFilePath, content, Encoding.UTF8);
                    File.AppendAllText(AppLogFilePath, content, Encoding.UTF8);
                }
            }
            catch
            {
                // ロギング失敗時の二重クラッシュ防止
            }
        }

        /// <summary>
        /// エクスプローラーでログ保存先ディレクトリを開きます。
        /// </summary>
        public static void OpenLogFolder()
        {
            try
            {
                string dir = LogDirectory;
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                LogException("OpenLogFolder", ex);
            }
        }
    }
}
