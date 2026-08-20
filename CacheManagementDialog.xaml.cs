using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ImageManager.Models;
using ImageManager.Services;

namespace ImageManager
{
    /// <summary>
    /// サムネイルキャッシュの容量確認、手動全削除、エクスプローラーでのフォルダオープン、
    /// および終了時自動クリーンアップ（期間・容量制限）設定を行うダイアログクラス。
    /// </summary>
    public sealed partial class CacheManagementDialog : ContentDialog
    {
        private readonly ISettingsService _settingsService;

        /// <summary>
        /// <see cref="CacheManagementDialog"/> クラスの新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="settingsService">設定管理サービス</param>
        /// <param name="xamlRoot">ダイアログを表示するルートXAML要素</param>
        public CacheManagementDialog(ISettingsService settingsService, Microsoft.UI.Xaml.XamlRoot? xamlRoot = null)
        {
            this.InitializeComponent();
            _settingsService = settingsService;
            this.XamlRoot = xamlRoot ?? App.MainWindow?.Content?.XamlRoot;

            CachePathTextBlock.Text = ThumbnailCacheService.DefaultCacheDirectory;
            LoadSettings();

            this.Loaded += CacheManagementDialog_Loaded;
            this.PrimaryButtonClick += CacheManagementDialog_PrimaryButtonClick;
        }

        /// <summary>
        /// ダイアログ表示完了時のイベントハンドラ。現在のキャッシュ使用状況を非同期取得します。
        /// </summary>
        private async void CacheManagementDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshCacheInfoAsync();
        }

        /// <summary>
        /// キャッシュディレクトリの使用容量およびファイル数を再計算しUIを更新します。
        /// </summary>
        private async Task RefreshCacheInfoAsync()
        {
            var info = await ThumbnailCacheService.GetCacheInfoAsync();
            CacheUsageTextBlock.Text = $"{ThumbnailCacheService.FormatBytes(info.TotalSizeBytes)} ({info.FileCount:N0} ファイル)";
        }

        /// <summary>
        /// 現在の設定をコントロールへ反映します。
        /// </summary>
        private void LoadSettings()
        {
            var settings = _settingsService.Load();
            bool autoClean = settings.AutoCleanCacheOnExit;
            AutoCleanCheckBox.IsChecked = autoClean;
            PeriodComboBox.IsEnabled = autoClean;
            CapacityComboBox.IsEnabled = autoClean;

            // 保持期間の選択
            SelectComboBoxItemByTag(PeriodComboBox, settings.CacheCleanPeriodDays.ToString());

            // 最大容量制限の選択
            SelectComboBoxItemByTag(CapacityComboBox, settings.CacheCleanMaxSizeBytes.ToString());
        }

        /// <summary>
        /// ComboBoxItemのTag値に基づいて該当項目を選択します。
        /// </summary>
        private void SelectComboBoxItemByTag(ComboBox comboBox, string tagValue)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tagValue)
                {
                    comboBox.SelectedItem = cbi;
                    return;
                }
            }
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 自動クリーンアップチェックボックス変更時、コンボボックスの有効/無効を切り替えます。
        /// </summary>
        private void AutoCleanCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isEnabled = AutoCleanCheckBox.IsChecked == true;
            PeriodComboBox.IsEnabled = isEnabled;
            CapacityComboBox.IsEnabled = isEnabled;
        }

        /// <summary>
        /// 「保存」ボタン押下時のイベントハンドラ。設定を永続化します。
        /// </summary>
        private void CacheManagementDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var settings = _settingsService.Load();
            settings.AutoCleanCacheOnExit = AutoCleanCheckBox.IsChecked == true;

            if (PeriodComboBox.SelectedItem is ComboBoxItem periodItem &&
                int.TryParse(periodItem.Tag?.ToString(), out int period))
            {
                settings.CacheCleanPeriodDays = period;
            }

            if (CapacityComboBox.SelectedItem is ComboBoxItem capItem &&
                long.TryParse(capItem.Tag?.ToString(), out long capacity))
            {
                settings.CacheCleanMaxSizeBytes = capacity;
            }

            _settingsService.Save(settings);
        }

        /// <summary>
        /// 「今すぐキャッシュを全削除」ボタン押下時のイベントハンドラ。
        /// </summary>
        private async void ClearAllCache_Click(object sender, RoutedEventArgs e)
        {
            var result = await ThumbnailCacheService.ClearAllCacheAsync();
            await RefreshCacheInfoAsync();

            ActionResultInfoBar.Message = $"キャッシュを一括削除しました。（{result.DeletedCount:N0} ファイル削除、{ThumbnailCacheService.FormatBytes(result.FreedBytes)} 解放）";
            ActionResultInfoBar.IsOpen = true;
        }

        /// <summary>
        /// 「キャッシュフォルダを開く」ボタン押下時のイベントハンドラ。エクスプローラーを起動します。
        /// </summary>
        private void OpenCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = ThumbnailCacheService.DefaultCacheDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
