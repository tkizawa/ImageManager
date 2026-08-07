using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ImageManager.Models;
using ImageManager.Services;

namespace ImageManager;

public sealed partial class ClassifyDialog : ContentDialog
{
    private readonly ImageClassifierService _classifierService;
    private readonly IEnumerable<ImageFile> _images;
    private readonly string _targetFolderPath;
    private CancellationTokenSource? _cts;

    public ClassifyDialog(ImageClassifierService classifierService, IEnumerable<ImageFile> images, string targetFolderPath)
    {
        _classifierService = classifierService;
        _images = images;
        _targetFolderPath = targetFolderPath;

        this.InitializeComponent();

        UpdateEngineStatusText();

        this.Loaded += ClassifyDialog_Loaded;
        this.PrimaryButtonClick += ClassifyDialog_PrimaryButtonClick;
        this.CloseButtonClick += ClassifyDialog_CloseButtonClick;
    }

    private async void ClassifyDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadOllamaModelsAsync();
    }

    private async Task LoadOllamaModelsAsync()
    {
        bool available = await _classifierService.Ollama.IsAvailableAsync();
        if (available)
        {
            var models = await _classifierService.Ollama.GetInstalledModelsAsync();
            if (models.Count > 0)
            {
                OllamaModelComboBox.ItemsSource = models;
                
                // Select default or first vision-like model
                int defaultIndex = models.FindIndex(m => m.Contains("llava", StringComparison.OrdinalIgnoreCase) || 
                                                         m.Contains("vision", StringComparison.OrdinalIgnoreCase) ||
                                                         m.Contains("moondream", StringComparison.OrdinalIgnoreCase));
                OllamaModelComboBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
                OllamaStatusText.Text = $"Ollama 接続完了 ({models.Count} 個のモデル検出)";
            }
            else
            {
                OllamaStatusText.Text = "Ollama は動作中ですが、インストール済みモデルが見つかりません。";
            }
        }
        else
        {
            OllamaStatusText.Text = "Ollama (http://localhost:11434) に接続できませんでした。Ollamaが起動しているか確認してください。";
            OllamaRadio.IsEnabled = false;
        }
    }

    private void EngineRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (OllamaSettingsPanel != null && OllamaRadio != null)
        {
            OllamaSettingsPanel.Visibility = OllamaRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateEngineStatusText();
    }

    private void UpdateEngineStatusText()
    {
        if (DeviceStatusText == null || _classifierService == null) return;

        if (OllamaRadio != null && OllamaRadio.IsChecked == true)
        {
            string selectedModel = OllamaModelComboBox?.SelectedItem as string ?? "LLM Vision";
            DeviceStatusText.Text = $"判定エンジン: Ollama ローカルAI ({selectedModel})";
        }
        else
        {
            DeviceStatusText.Text = _classifierService.IsModelLoaded
                ? (_classifierService.IsDirectMLActive ? "判定エンジン: ONNX DirectML (GPU加速)" : "判定エンジン: ONNX CPU モード")
                : "判定エンジン: ルールベース AI (特徴・色調解析)";
        }
    }

    private async void ClassifyDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = true; // Prevent dialog from closing immediately

            RuleBasedRadio.IsEnabled = false;
            OllamaRadio.IsEnabled = false;
            OllamaModelComboBox.IsEnabled = false;
            CopyRadio.IsEnabled = false;
            MoveRadio.IsEnabled = false;
            TagOnlyRadio.IsEnabled = false;
            IsPrimaryButtonEnabled = false;

            ProgressArea.Visibility = Visibility.Visible;

            _classifierService.UseOllama = OllamaRadio.IsChecked == true;
            if (OllamaModelComboBox.SelectedItem is string selectedModel)
            {
                _classifierService.OllamaModelName = selectedModel;
            }

            ClassificationMode mode = ClassificationMode.CopyToCategoryFolder;
            if (MoveRadio.IsChecked == true) mode = ClassificationMode.MoveToCategoryFolder;
            if (TagOnlyRadio.IsChecked == true) mode = ClassificationMode.TagOnly;

            _cts = new CancellationTokenSource();

            var progress = new Progress<(int current, int total, string currentFile, string category)>(p =>
            {
                if (p.total > 0)
                {
                    ClassifyProgressBar.Value = (double)p.current / p.total * 100.0;
                    ProgressStatusText.Text = $"{p.current} / {p.total} 枚処理中: {p.currentFile} [{p.category}]";
                }
            });

            await _classifierService.ProcessClassificationAsync(_images, _targetFolderPath, mode, progress, _cts.Token);

            ProgressStatusText.Text = "分類が正常に完了しました！";
            await Task.Delay(800);
            this.Hide();
        }
        catch (Exception ex)
        {
            ProgressStatusText.Text = $"エラーが発生しました: {ex.Message}";
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ClassifyDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _cts?.Cancel();
    }
}
