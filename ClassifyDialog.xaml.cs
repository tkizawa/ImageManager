using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ImageManager.Models;
using ImageManager.Services;

namespace ImageManager;

/// <summary>
/// 画像の自動分類実行ダイアログクラス。
/// 分類エンジンの選択（Ollama ローカルVLM / ONNX DirectML / ルールベース解析）、
/// 出力モード（フォルダへコピー/移動/タグ付与のみ）の指定、および進捗表示・キャンセル処理を提供します。
/// </summary>
public sealed partial class ClassifyDialog : ContentDialog
{
    private readonly ImageClassifierService _classifierService;
    private readonly IEnumerable<ImageFile> _images;
    private readonly string _targetFolderPath;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// <see cref="ClassifyDialog"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="classifierService">画像分類サービスクラス</param>
    /// <param name="images">分類対象の画像コレクション</param>
    /// <param name="targetFolderPath">ベース出力フォルダパス</param>
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

    /// <summary>
    /// ダイアログ表示時のイベントハンドラ。Ollama の接続確認とモデル一覧の非同期取得を行います。
    /// </summary>
    private async void ClassifyDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadOllamaModelsAsync();
    }

    /// <summary>
    /// Ollama サーバーの動作状態を確認し、利用可能な Vision モデル一覧をコンボボックスへバインドします。
    /// </summary>
    private async Task LoadOllamaModelsAsync()
    {
        bool available = await _classifierService.Ollama.IsAvailableAsync();
        if (available)
        {
            var models = await _classifierService.Ollama.GetInstalledModelsAsync();
            if (models.Count > 0)
            {
                OllamaModelComboBox.ItemsSource = models;
                
                // llava, vision, moondream などの Vision 対応モデルを優先選択
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

    /// <summary>
    /// 判定エンジンラジオボタン切り替え時のイベントハンドラ。
    /// </summary>
    private void EngineRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (OllamaSettingsPanel != null && OllamaRadio != null)
        {
            OllamaSettingsPanel.Visibility = OllamaRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateEngineStatusText();
    }

    /// <summary>
    /// 現在選択されているエンジンの動作状態テキスト（GPU加速、CPU、Ollama等）を更新します。
    /// </summary>
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

    /// <summary>
    /// 「実行」ボタン押下時のイベントハンドラ。分類処理を開始し、リアルタイムに進捗を表示します。
    /// </summary>
    private async void ClassifyDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = true; // 処理中はダイアログが閉じないようにキャンセル

            // 処理中は操作コントロールを無効化
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

    /// <summary>
    /// 「キャンセル / 閉じる」ボタン押下時のイベントハンドラ。実行中の処理を中断します。
    /// </summary>
    private void ClassifyDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _cts?.Cancel();
    }
}
