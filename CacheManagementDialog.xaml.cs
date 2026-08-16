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
    public sealed partial class CacheManagementDialog : ContentDialog
    {
        private readonly ISettingsService _settingsService;

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

        private async void CacheManagementDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshCacheInfoAsync();
        }

        private async Task RefreshCacheInfoAsync()
        {
            var info = await ThumbnailCacheService.GetCacheInfoAsync();
            CacheUsageTextBlock.Text = $"{ThumbnailCacheService.FormatBytes(info.TotalSizeBytes)} ({info.FileCount:N0} ファイル)";
        }

        private void LoadSettings()
        {
            var settings = _settingsService.Load();
            bool autoClean = settings.AutoCleanCacheOnExit;
            AutoCleanCheckBox.IsChecked = autoClean;
            PeriodComboBox.IsEnabled = autoClean;
            CapacityComboBox.IsEnabled = autoClean;

            // Select Period
            SelectComboBoxItemByTag(PeriodComboBox, settings.CacheCleanPeriodDays.ToString());

            // Select Capacity
            SelectComboBoxItemByTag(CapacityComboBox, settings.CacheCleanMaxSizeBytes.ToString());
        }

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

        private void AutoCleanCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isEnabled = AutoCleanCheckBox.IsChecked == true;
            PeriodComboBox.IsEnabled = isEnabled;
            CapacityComboBox.IsEnabled = isEnabled;
        }

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

        private async void ClearAllCache_Click(object sender, RoutedEventArgs e)
        {
            var result = await ThumbnailCacheService.ClearAllCacheAsync();
            await RefreshCacheInfoAsync();

            ActionResultInfoBar.Message = $"キャッシュを一括削除しました。（{result.DeletedCount:N0} ファイル削除、{ThumbnailCacheService.FormatBytes(result.FreedBytes)} 解放）";
            ActionResultInfoBar.IsOpen = true;
        }

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
