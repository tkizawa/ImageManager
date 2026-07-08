using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageManager.Models;
using ImageManager.Services;
using System.Linq;

namespace ImageManager.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;

        [ObservableProperty]
        private string _currentFolderPath;

        [ObservableProperty]
        private ObservableCollection<ImageFile> _images = new();

        [ObservableProperty]
        private ImageFile _selectedImage;

        [ObservableProperty]
        private ObservableCollection<DirectoryNodeViewModel> _folders = new();

        public MainViewModel(IFileSystemService fileSystemService)
        {
            _fileSystemService = fileSystemService;
            LoadDrives();
        }

        private void LoadDrives()
        {
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    Folders.Add(new DirectoryNodeViewModel(drive.Name));
                }
            }
        }

        public async Task SelectFolderFromTreeAsync(string folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && CurrentFolderPath != folderPath)
            {
                CurrentFolderPath = folderPath;
                await LoadImagesAsync(folderPath);
            }
        }

        [RelayCommand]
        private async Task SelectFolderAsync()
        {
            var folder = _fileSystemService.SelectFolder();
            if (!string.IsNullOrEmpty(folder))
            {
                CurrentFolderPath = folder;
                await LoadImagesAsync(folder);
            }
        }

        private async Task LoadImagesAsync(string folderPath)
        {
            Images.Clear();
            SelectedImage = null;
            
            // In a real app, this should run on a background thread to avoid freezing UI
            await Task.Run(() => 
            {
                var files = _fileSystemService.GetImageFiles(folderPath).ToList();
                var newImages = files.Select(f => new ImageFile(f)).ToList();
                
                App.Current.Dispatcher.Invoke(() => 
                {
                    foreach (var img in newImages)
                    {
                        Images.Add(img);
                    }
                });
            });
        }
    }
}
