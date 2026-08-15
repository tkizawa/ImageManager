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
    public sealed partial class ExternalAppsDialog : ContentDialog
    {
        private readonly ISettingsService _settingsService;
        private readonly ObservableCollection<ExternalApp> _apps = new();

        public event EventHandler? SettingsChanged;

        public ExternalAppsDialog(ISettingsService settingsService)
        {
            this.InitializeComponent();
            _settingsService = settingsService;
            this.XamlRoot = App.MainWindow?.Content?.XamlRoot;

            AppsListView.ItemsSource = _apps;
            LoadApps();
        }

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

        private void SaveApps()
        {
            var settings = _settingsService.Load();
            settings.ExternalApps = _apps.ToList();
            _settingsService.Save(settings);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            EmptyListTextBlock.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

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

            // Clear input fields
            ProgramNameTextBox.Text = string.Empty;
            ExecutablePathTextBox.Text = string.Empty;
            ArgumentsTextBox.Text = "{path}";
        }

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
