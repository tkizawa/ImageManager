using Microsoft.UI.Xaml;

namespace ImageManager;

/// <summary>
/// アプリケーションのエントリポイントおよびライフサイクルを管理するクラス。
/// WinUI 3 アプリケーションの初期化、メインウィンドウの生成、例外ハンドリングを行います。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// アプリケーションのメインウィンドウのインスタンスを取得します。
    /// </summary>
    public static MainWindow MainWindow { get; private set; } = null!;

    /// <summary>
    /// メインUIスレッドの DispatcherQueue インスタンスを取得します。
    /// バックグラウンドタスクからUIスレッドへのディスパッチに使用されます。
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue? MainDispatcherQueue { get; private set; }

    /// <summary>
    /// <see cref="App"/> クラスの新しいインスタンスを初期化します。
    /// コンポーネントの初期化および未処理例外ハンドラを登録します。
    /// </summary>
    public App()
    {
        this.InitializeComponent();

        // 1. WinUI 3 XAML / UIスレッドの未処理例外ハンドラ
        this.UnhandledException += (s, e) =>
        {
            Services.AppLogService.LogFatalCrash("WinUI.UnhandledException", e.Exception ?? (object)e.Message);
        };

        // 2. バックグラウンドスレッド / CLR レベルの未処理例外ハンドラ
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Services.AppLogService.LogFatalCrash("AppDomain.UnhandledException", e.ExceptionObject);
        };

        // 3. Task の await 取りこぼし例外ハンドラ
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Services.AppLogService.LogFatalCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved(); // プロセス強制終了を回避
        };
    }

    /// <summary>
    /// アプリケーション起動時に呼び出されるメソッド。
    /// 必要なサービスクラスおよびメインビューモデルを初期化し、メインウィンドウを表示します。
    /// </summary>
    /// <param name="args">起動アクティベーションイベント引数。</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try 
        {
            // 言語オーバーライドのリセット（OS言語設定に準拠）
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = string.Empty;

            Services.AppLogService.Log("=== ImageManager Application Started ===");

            // UIスレッドのディスパッチャを取得
            MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // 依存サービスおよびViewModelの初期化
            var fileSystemService = new ImageManager.Services.FileSystemService();
            var settingsService = new ImageManager.Services.SettingsService();
            var mainViewModel = new ImageManager.ViewModels.MainViewModel(fileSystemService, settingsService);
            
            // メインウィンドウの生成と表示
            MainWindow = new MainWindow(settingsService, mainViewModel);
            MainWindow.Activate();
        }
        catch (System.Exception ex)
        {
            // 起動時の致命的エラーをログに記録
            Services.AppLogService.LogFatalCrash("App.OnLaunched", ex);
        }
    }
}

