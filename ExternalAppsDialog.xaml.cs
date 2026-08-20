using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using ImageManager.Models;
using ImageManager.Services;

namespace ImageManager
{
    /// <summary>
    /// 外部画像編集・閲覧アプリケーション（Photoshop, GIMP, 外部ビューアー等）の
    /// 登録・編集・削除を行う設定ダイアログクラス。
    /// </summary>
    public sealed partial class ExternalAppsDialog : ContentDialog
    {
        private readonly ISettingsService _settingsService;
        private readonly ObservableCollection<ExternalApp> _apps = new();

        /// <summary>外部アプリ設定が変更された際に発火するイベント</summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// <see cref="ExternalAppsDialog"/> クラスの新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="settingsService">設定管理サービス</param>
        /// <param name="xamlRoot">ルートXAML要素</param>
        public ExternalAppsDialog(ISettingsService settingsService, Microsoft.UI.Xaml.XamlRoot? xamlRoot = null)
        {
            this.InitializeComponent();
            _settingsService = settingsService;
            this.XamlRoot = xamlRoot ?? App.MainWindow?.Content?.XamlRoot;

            AppsListView.ItemsSource = _apps;
            LoadApps();
        }

        /// <summary>
        /// 保存されている外部アプリ一覧を設定から読み込み、リストを構築します。
        /// </summary>
        private void LoadApps()
        {
            _apps.Clear();
            var settings = _settingsService.Load();
            if (settings.ExternalApps != null)
            {
                foreach (var app in settings.ExternalApps)
                {
                    _apps.Add(app);
                }
            }
            UpdateEmptyState();
        }

        /// <summary>
        /// 外部アプリ一覧を設定ファイルへ保存し、変更通知を発行します。
        /// </summary>
        private void SaveApps()
        {
            var settings = _settingsService.Load();
            settings.ExternalApps = _apps.ToList();
            _settingsService.Save(settings);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            UpdateEmptyState();
        }

        /// <summary>
        /// 登録アプリが0件の場合の案内表示状態を更新します。
        /// </summary>
        private void UpdateEmptyState()
        {
            EmptyListTextBlock.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 「参照...」ボタン押下時、実行可能ファイル（.exe等）を選択するファイルピッカーを表示します。
        /// </summary>
        private async void BrowseExecutable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openPicker = new FileOpenPicker();
                openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                openPicker.FileTypeFilter.Add(".exe");
                openPicker.FileTypeFilter.Add(".bat");
                openPicker.FileTypeFilter.Add(".cmd");
                openPicker.FileTypeFilter.Add("*");

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WindowNative.GetWindowHandle(window);
                    InitializeWithWindow.Initialize(openPicker, hwnd);
                }

                var file = await openPicker.PickSingleFileAsync();
                if (file != null)
                {
                    ExecutablePathTextBox.Text = file.Path;
                    if (string.IsNullOrWhiteSpace(ProgramNameTextBox.Text))
                    {
                        ProgramNameTextBox.Text = Path.GetFileNameWithoutExtension(file.Path);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to pick executable: {ex.Message}");
            }
        }

        /// <summary>
        /// 「追加」ボタン押下時、入力されたアプリケーション情報を検証して登録します。
        /// </summary>
        private void AddProgram_Click(object sender, RoutedEventArgs e)
        {
            string name = ProgramNameTextBox.Text?.Trim() ?? string.Empty;
            string path = ExecutablePathTextBox.Text?.Trim() ?? string.Empty;
            string args = ArgumentsTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                ExecutablePathTextBox.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "External Program";
                }
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                args = "{path}";
            }

            var newApp = new ExternalApp
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                ExecutablePath = path,
                Arguments = args
            };

            _apps.Add(newApp);
            SaveApps();

            // 入力欄をクリア
            ProgramNameTextBox.Text = string.Empty;
            ExecutablePathTextBox.Text = string.Empty;
            ArgumentsTextBox.Text = "{path}";
        }

        /// <summary>
        /// 登録済みアプリの削除ボタン押下時、リストから削除して設定を保存します。
        /// </summary>
        private void DeleteApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ExternalApp app)
            {
                _apps.Remove(app);
                SaveApps();
            }
        }
    }
}
