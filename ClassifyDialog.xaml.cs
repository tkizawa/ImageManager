using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        this.InitializeComponent();
        _classifierService = classifierService;
        _images = images;
        _targetFolderPath = targetFolderPath;

        DeviceStatusText.Text = _classifierService.IsDirectMLActive 
            ? "判定エンジン: ONNX DirectML (GPU加速)" 
            : "判定エンジン: ONNX CPU モード";

        this.PrimaryButtonClick += ClassifyDialog_PrimaryButtonClick;
        this.CloseButtonClick += ClassifyDialog_CloseButtonClick;
    }

    private async void ClassifyDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            args.Cancel = true; // Prevent dialog from closing immediately

            CopyRadio.IsEnabled = false;
            MoveRadio.IsEnabled = false;
            TagOnlyRadio.IsEnabled = false;
            IsPrimaryButtonEnabled = false;

            ProgressArea.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

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
