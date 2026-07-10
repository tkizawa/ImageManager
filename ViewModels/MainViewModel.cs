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
        private readonly SettingsService _settingsService;

        [ObservableProperty]
        private string _currentFolderPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<ImageFile> _images = new();

        [ObservableProperty]
        private ImageFile? _selectedImage;

        partial void OnSelectedImageChanged(ImageFile? value)
        {
            if (value != null)
            {
                _ = value.LoadExifAsync();
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ThumbnailPanelWidth))]
        private double _thumbnailSize = 100;

        public double ThumbnailPanelWidth => ThumbnailSize + 20;

        [ObservableProperty]
        private ObservableCollection<DirectoryNodeViewModel> _folders = new();

        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        public MainViewModel(IFileSystemService fileSystemService, SettingsService settingsService)
        {
            _fileSystemService = fileSystemService;
            _settingsService = settingsService;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            LoadDrives();

            var settings = _settingsService.Load();
            if (!string.IsNullOrEmpty(settings.LastOpenedFolder) && System.IO.Directory.Exists(settings.LastOpenedFolder))
            {
                _ = ExpandAndSelectPathAsync(settings.LastOpenedFolder);
            }
        }

        private async Task ExpandAndSelectPathAsync(string path)
        {
            var parts = path.Split(System.IO.Path.DirectorySeparatorChar, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            
            parts[0] += System.IO.Path.DirectorySeparatorChar;
            
            ObservableCollection<DirectoryNodeViewModel> currentList = Folders;
            DirectoryNodeViewModel? targetNode = null;
            string currentPath = "";

            foreach (var part in parts)
            {
                currentPath = string.IsNullOrEmpty(currentPath) ? part : System.IO.Path.Combine(currentPath, part);
                var node = currentList.FirstOrDefault(n => n.Name.Equals(part, System.StringComparison.OrdinalIgnoreCase) || n.FullPath.Equals(currentPath, System.StringComparison.OrdinalIgnoreCase));
                if (node == null) break;

                targetNode = node;
                node.IsExpanded = true;
                currentList = node.Children;
            }

            if (targetNode != null)
            {
                targetNode.IsSelected = true;
                // TreeView item selection logic will trigger the SelectedItemChanged event, 
                // but just in case, we also explicitly load the images here if the current path isn't set yet.
                if (string.IsNullOrEmpty(CurrentFolderPath))
                {
                    await SelectFolderFromTreeAsync(targetNode.FullPath);
                }
            }
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
                var settings = _settingsService.Load();
                settings.LastOpenedFolder = folderPath;
                _settingsService.Save(settings);
                
                await LoadImagesAsync(folderPath);
            }
        }

        [RelayCommand]
        private async Task SelectFolderAsync()
        {
            var folder = await _fileSystemService.SelectFolderAsync();
            if (!string.IsNullOrEmpty(folder))
            {
                CurrentFolderPath = folder;
                var settings = _settingsService.Load();
                settings.LastOpenedFolder = folder;
                _settingsService.Save(settings);
                
                await LoadImagesAsync(folder);
                _ = ExpandAndSelectPathAsync(folder);
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
                
                _dispatcherQueue?.TryEnqueue(() => 
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
