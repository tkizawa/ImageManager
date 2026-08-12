using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ImageManager.Services
{
    public class FileSystemService : IFileSystemService
    {
        public async Task<string> SelectFolderAsync()
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            var window = App.MainWindow;
            if (window != null)
            {
                var hwnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path ?? string.Empty;
        }

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

        public IEnumerable<string> GetImageFiles(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return Enumerable.Empty<string>();

            return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(s => RawThumbnailService.IsSupportedImage(s));
        }
    }
}
