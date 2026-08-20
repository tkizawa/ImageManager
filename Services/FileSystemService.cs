using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ImageManager.Services
{
    /// <summary>
    /// ファイルシステムおよびWinUI 3ピッカーダイアログ操作を実装するサービスクラス。
    /// </summary>
    public class FileSystemService : IFileSystemService
    {
        /// <summary>
        /// フォルダ選択ダイアログを表示し、選択されたフォルダパスを取得します。
        /// WinUI 3 のウィンドウハンドル（HWND）をピッカーに関連付けて表示します。
        /// </summary>
        /// <returns>選択されたフォルダパス。キャンセルの場合は空文字列。</returns>
        public async Task<string> SelectFolderAsync()
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            // WinUI 3 ではモーダルピッカー表示のためにHWNDの関連付けが必須
            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path ?? string.Empty;
        }

        /// <summary>
        /// ファイル保存ダイアログを表示し、保存先パスを取得します。
        /// </summary>
        /// <param name="defaultFileName">初期ファイル名</param>
        /// <param name="fileTypeDescription">ファイル種類の表示名</param>
        /// <param name="fileExtension">拡張子（例: ".json"）</param>
        /// <returns>保存先ファイルパス。キャンセルの場合は null。</returns>
        public async Task<string?> SaveFilePickerAsync(string defaultFileName, string fileTypeDescription, string fileExtension)
        {
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.Desktop;
            savePicker.SuggestedFileName = defaultFileName;
            var ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
            savePicker.FileTypeChoices.Add(fileTypeDescription, new List<string> { ext });

            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(savePicker, hwnd);
            }

            var file = await savePicker.PickSaveFileAsync();
            return file?.Path;
        }

        /// <summary>
        /// ファイル選択ダイアログを表示し、選択されたファイルパスを取得します。
        /// </summary>
        /// <param name="fileTypeDescription">ファイル種類の表示名</param>
        /// <param name="fileExtension">拡張子（例: ".json"）</param>
        /// <returns>選択されたファイルパス。キャンセルの場合は null。</returns>
        public async Task<string?> OpenFilePickerAsync(string fileTypeDescription, string fileExtension)
        {
            var openPicker = new FileOpenPicker();
            openPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            var ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
            openPicker.FileTypeFilter.Add(ext);

            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(openPicker, hwnd);
            }

            var file = await openPicker.PickSingleFileAsync();
            return file?.Path;
        }

        /// <summary>
        /// 指定されたフォルダ直下から、サポートされている画像ファイル（RAW・通常画像）のパス一覧を列挙します。
        /// </summary>
        /// <param name="folderPath">フォルダパス</param>
        /// <returns>画像ファイルパスの列挙</returns>
        public IEnumerable<string> GetImageFiles(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return Enumerable.Empty<string>();

            return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(s => RawThumbnailService.IsSupportedImage(s));
        }
    }
}
