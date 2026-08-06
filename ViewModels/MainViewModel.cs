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
        private readonly ISettingsService _settingsService;

        [ObservableProperty]
        private string _currentFolderPath = string.Empty;

        public string AppVersion => $"Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.6.0"}";

        [ObservableProperty]
        private ObservableCollection<ImageFile> _images = new();

        [ObservableProperty]
        private ImageFile? _selectedImage;

        public event System.EventHandler<DirectoryNodeViewModel>? FolderSelectedEvent;
        public event System.EventHandler<(string titleKey, string messageKey)>? ShowMessageRequested;

        partial void OnSelectedImageChanged(ImageFile? value)
        {
            if (value != null)
            {
                _ = value.LoadExifAsync();
                var settings = _settingsService.Load();
                if (settings.SelectedImageFilePath != value.FilePath)
                {
                    settings.SelectedImageFilePath = value.FilePath;
                    _settingsService.Save(settings);
                }
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ThumbnailPanelWidth))]
        private double _thumbnailSize = 100;

        partial void OnThumbnailSizeChanged(double value)
        {
            var settings = _settingsService.Load();
            if (Math.Abs(settings.ThumbnailSize - value) > 0.1)
            {
                settings.ThumbnailSize = value;
                _settingsService.Save(settings);
            }
        }

        public double ThumbnailPanelWidth => ThumbnailSize + 20;

        [ObservableProperty]
        private ObservableCollection<DirectoryNodeViewModel> _folders = new();

        [ObservableProperty]
        private ObservableCollection<string> _favoriteFolders = new();

        [ObservableProperty]
        private ObservableCollection<string> _historyFolders = new();

        [ObservableProperty]
        private ObservableCollection<LibraryNodeViewModel> _libraries = new();

        [ObservableProperty]
        private int _sortFieldIndex = 0; // 0: LastWriteTime, 1: DateTaken

        [ObservableProperty]
        private int _sortDirectionIndex = 1; // 0: Ascending, 1: Descending

        private bool _isSorting = false;

        partial void OnSortFieldIndexChanged(int value)
        {
            var settings = _settingsService.Load();
            settings.SortFieldIndex = value;
            _settingsService.Save(settings);

            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                _ = SortImagesAsync();
            }
        }

        partial void OnSortDirectionIndexChanged(int value)
        {
            var settings = _settingsService.Load();
            settings.SortDirectionIndex = value;
            _settingsService.Save(settings);

            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                _ = SortImagesAsync();
            }
        }

        private async Task SortImagesAsync()
        {
            if (Images.Count == 0 || _isSorting) return;
            _isSorting = true;

            try
            {
                var sorted = await Task.Run(async () => 
                {
                    var list = Images.ToList();
                    
                    if (SortFieldIndex == 1) // DateTaken
                    {
                        var tasks = list.Where(i => !i.IsExifLoaded).Select(i => i.LoadExifAsync());
                        await Task.WhenAll(tasks);
                    }

                    if (SortFieldIndex == 0) // LastWriteTime
                    {
                        if (SortDirectionIndex == 0) // Ascending
                            return list.OrderBy(i => i.LastWriteTime).ToList();
                        else
                            return list.OrderByDescending(i => i.LastWriteTime).ToList();
                    }
                    else // DateTaken
                    {
                        if (SortDirectionIndex == 0)
                            return list.OrderBy(i => string.IsNullOrEmpty(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                        else
                            return list.OrderByDescending(i => string.IsNullOrEmpty(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                    }
                });

                RunOnUIThread(() => 
                {
                    var selected = SelectedImage;
                    Images.Clear();
                    foreach (var img in sorted)
                    {
                        Images.Add(img);
                    }
                    SelectedImage = selected;
                });
            }
            finally
            {
                _isSorting = false;
            }
        }

        private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

        public MainViewModel(IFileSystemService fileSystemService, ISettingsService settingsService)
        {
            _fileSystemService = fileSystemService;
            _settingsService = settingsService;
            try
            {
                _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            }
            catch
            {
                _dispatcherQueue = null;
            }
            LoadDrives();
        }

        private void RunOnUIThread(System.Action action)
        {
            if (_dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }

        public async Task InitializeAsync()
        {
            var settings = _settingsService.Load();
            
            if (settings.ThumbnailSize > 0)
            {
                ThumbnailSize = settings.ThumbnailSize;
            }

            SortFieldIndex = settings.SortFieldIndex;
            SortDirectionIndex = settings.SortDirectionIndex;

            if (settings.FavoriteFolders != null)
            {
                foreach (var folder in settings.FavoriteFolders)
                {
                    FavoriteFolders.Add(folder);
                }
            }
            
            if (settings.HistoryFolders != null)
            {
                foreach (var folder in settings.HistoryFolders)
                {
                    HistoryFolders.Add(folder);
                }
            }

            if (settings.Libraries != null)
            {
                Libraries.Clear();
                foreach (var libGroup in settings.Libraries)
                {
                    var libNode = new LibraryNodeViewModel
                    {
                        Id = libGroup.Id,
                        Name = libGroup.Name,
                        IsLibrary = true
                    };
                    if (libGroup.FolderPaths != null)
                    {
                        foreach (var folderPath in libGroup.FolderPaths)
                        {
                            var folderName = System.IO.Path.GetFileName(folderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                            if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                            var folderNode = new LibraryNodeViewModel
                            {
                                Id = System.Guid.NewGuid().ToString(),
                                Name = folderName,
                                FullPath = folderPath,
                                IsLibrary = false,
                                ParentLibrary = libNode
                            };
                            folderNode.CheckAndAddDummyChild();
                            libNode.Children.Add(folderNode);
                        }
                    }
                    Libraries.Add(libNode);
                }
            }

            if (!string.IsNullOrEmpty(settings.LastOpenedFolder) && System.IO.Directory.Exists(settings.LastOpenedFolder))
            {
                await ExpandAndSelectPathAsync(settings.LastOpenedFolder);
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
                FolderSelectedEvent?.Invoke(this, targetNode);
                
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
            try
            {
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        Folders.Add(new DirectoryNodeViewModel(drive.Name));
                    }
                }
            }
            catch { }
        }

        public async Task SelectFolderFromTreeAsync(string folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && CurrentFolderPath != folderPath)
            {
                CurrentFolderPath = folderPath;
                var settings = _settingsService.Load();
                settings.LastOpenedFolder = folderPath;
                _settingsService.Save(settings);
                
                AddHistoryFolder(folderPath);
                
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
                
                AddHistoryFolder(folder);
                
                await LoadImagesAsync(folder);
                _ = ExpandAndSelectPathAsync(folder);
            }
        }

        private async Task LoadImagesAsync(string folderPath)
        {
            Images.Clear();
            SelectedImage = null;
            
            // In a real app, this should run on a background thread to avoid freezing UI
            await Task.Run(async () => 
            {
                var files = _fileSystemService.GetImageFiles(folderPath).ToList();
                var newImages = files.Select(f => new ImageFile(f)).ToList();
                
                if (SortFieldIndex == 1) // DateTaken
                {
                    var tasks = newImages.Where(i => !i.IsExifLoaded).Select(i => i.LoadExifAsync());
                    await Task.WhenAll(tasks);
                }

                if (SortFieldIndex == 0) // LastWriteTime
                {
                    if (SortDirectionIndex == 0) // Ascending
                        newImages = newImages.OrderBy(i => i.LastWriteTime).ToList();
                    else
                        newImages = newImages.OrderByDescending(i => i.LastWriteTime).ToList();
                }
                else // DateTaken
                {
                    if (SortDirectionIndex == 0)
                        newImages = newImages.OrderBy(i => string.IsNullOrEmpty(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                    else
                        newImages = newImages.OrderByDescending(i => string.IsNullOrEmpty(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                }
                
                RunOnUIThread(() => 
                {
                    foreach (var img in newImages)
                    {
                        Images.Add(img);
                    }

                    var settings = _settingsService.Load();
                    if (!string.IsNullOrEmpty(settings.SelectedImageFilePath))
                    {
                        var target = Images.FirstOrDefault(i => i.FilePath.Equals(settings.SelectedImageFilePath, System.StringComparison.OrdinalIgnoreCase));
                        if (target != null)
                        {
                            SelectedImage = target;
                        }
                        else if (Images.Count > 0)
                        {
                            SelectedImage = Images[0];
                        }
                    }
                    else if (Images.Count > 0)
                    {
                        SelectedImage = Images[0];
                    }
                });
            });
        }

        [RelayCommand]
        private void AddFavoriteFolder(object? parameter)
        {
            string? path = null;
            if (parameter is DirectoryNodeViewModel node)
            {
                path = node.FullPath;
            }
            else if (parameter is string strPath)
            {
                path = strPath;
            }

            if (!string.IsNullOrEmpty(path) && !FavoriteFolders.Contains(path))
            {
                FavoriteFolders.Add(path);
                SaveFavorites();
            }
        }

        [RelayCommand]
        private void RemoveFavoriteFolder(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && FavoriteFolders.Contains(folderPath))
            {
                FavoriteFolders.Remove(folderPath);
                SaveFavorites();
            }
        }

        [RelayCommand]
        private async Task SelectFavoriteFolderAsync(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
            {
                // 画像一覧を直接更新する
                await SelectFolderFromTreeAsync(folderPath);
                
                // ツリービュー側の選択状態の同期を試みる（バックグラウンド）
                _ = ExpandAndSelectPathAsync(folderPath);
            }
        }

        private void SaveFavorites()
        {
            var settings = _settingsService.Load();
            settings.FavoriteFolders = FavoriteFolders.ToList();
            _settingsService.Save(settings);
        }

        private void AddHistoryFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;

            // Remove if exists to avoid duplicates
            if (HistoryFolders.Contains(folderPath))
            {
                HistoryFolders.Remove(folderPath);
            }

            // Add to the top of the list (most recent)
            HistoryFolders.Insert(0, folderPath);

            // Optional: limit history size, e.g., to 20
            while (HistoryFolders.Count > 20)
            {
                HistoryFolders.RemoveAt(HistoryFolders.Count - 1);
            }

            SaveHistory();
        }

        [RelayCommand]
        private void RemoveHistoryFolder(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && HistoryFolders.Contains(folderPath))
            {
                HistoryFolders.Remove(folderPath);
                SaveHistory();
            }
        }

        [RelayCommand]
        private async Task SelectHistoryFolderAsync(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
            {
                await SelectFolderFromTreeAsync(folderPath);
                _ = ExpandAndSelectPathAsync(folderPath);
            }
        }

        private void SaveHistory()
        {
            var settings = _settingsService.Load();
            settings.HistoryFolders = HistoryFolders.ToList();
            _settingsService.Save(settings);
        }

        [RelayCommand]
        private async Task ExportSettingsAsync()
        {
            var settings = _settingsService.Load();
            settings.FavoriteFolders = FavoriteFolders.ToList();
            settings.HistoryFolders = HistoryFolders.ToList();
            settings.LastOpenedFolder = CurrentFolderPath;

            var filePath = await _fileSystemService.SaveFilePickerAsync("ImageManagerSettings.json", "JSON File (*.json)", ".json");
            if (!string.IsNullOrEmpty(filePath))
            {
                bool success = _settingsService.ExportSettings(filePath, settings);
                if (success)
                {
                    ShowMessageRequested?.Invoke(this, ("ExportSuccessTitle", "ExportSuccessMessage"));
                }
                else
                {
                    ShowMessageRequested?.Invoke(this, ("ExportErrorTitle", "ExportErrorMessage"));
                }
            }
        }

        [RelayCommand]
        private async Task ImportSettingsAsync()
        {
            var filePath = await _fileSystemService.OpenFilePickerAsync("JSON File (*.json)", ".json");
            if (!string.IsNullOrEmpty(filePath))
            {
                var imported = _settingsService.ImportSettings(filePath);
                if (imported != null)
                {
                    _settingsService.Save(imported);

                    FavoriteFolders.Clear();
                    if (imported.FavoriteFolders != null)
                    {
                        foreach (var folder in imported.FavoriteFolders)
                        {
                            FavoriteFolders.Add(folder);
                        }
                    }

                    HistoryFolders.Clear();
                    if (imported.HistoryFolders != null)
                    {
                        foreach (var folder in imported.HistoryFolders)
                        {
                            HistoryFolders.Add(folder);
                        }
                    }

                    Libraries.Clear();
                    if (imported.Libraries != null)
                    {
                        foreach (var libGroup in imported.Libraries)
                        {
                            var libNode = new LibraryNodeViewModel
                            {
                                Id = libGroup.Id,
                                Name = libGroup.Name,
                                IsLibrary = true
                            };
                            if (libGroup.FolderPaths != null)
                            {
                                foreach (var folderPath in libGroup.FolderPaths)
                                {
                                    var folderName = System.IO.Path.GetFileName(folderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                                    if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                                    var folderNode = new LibraryNodeViewModel
                                    {
                                        Id = System.Guid.NewGuid().ToString(),
                                        Name = folderName,
                                        FullPath = folderPath,
                                        IsLibrary = false,
                                        ParentLibrary = libNode
                                    };
                                    folderNode.CheckAndAddDummyChild();
                                    libNode.Children.Add(folderNode);
                                }
                            }
                            Libraries.Add(libNode);
                        }
                    }

                    if (!string.IsNullOrEmpty(imported.LastOpenedFolder) && System.IO.Directory.Exists(imported.LastOpenedFolder))
                    {
                        await SelectFolderFromTreeAsync(imported.LastOpenedFolder);
                        _ = ExpandAndSelectPathAsync(imported.LastOpenedFolder);
                    }

                    ShowMessageRequested?.Invoke(this, ("ImportSuccessTitle", "ImportSuccessMessage"));
                }
                else
                {
                    ShowMessageRequested?.Invoke(this, ("ImportErrorTitle", "ImportErrorMessage"));
                }
            }
        }

        public LibraryNodeViewModel CreateLibrary(string name)
        {
            var library = new LibraryGroup
            {
                Id = System.Guid.NewGuid().ToString(),
                Name = name,
                FolderPaths = new System.Collections.Generic.List<string>()
            };

            var libNode = new LibraryNodeViewModel
            {
                Id = library.Id,
                Name = library.Name,
                IsLibrary = true
            };

            Libraries.Add(libNode);
            SaveLibrariesToSettings();
            return libNode;
        }

        public void DeleteLibrary(LibraryNodeViewModel libraryNode)
        {
            if (libraryNode == null || !libraryNode.IsLibrary) return;
            Libraries.Remove(libraryNode);
            SaveLibrariesToSettings();
        }

        public void RenameLibrary(LibraryNodeViewModel libraryNode, string newName)
        {
            if (libraryNode == null || !libraryNode.IsLibrary || string.IsNullOrWhiteSpace(newName)) return;
            libraryNode.Name = newName.Trim();
            SaveLibrariesToSettings();
        }

        public LibraryNodeViewModel? AddFolderToLibrary(LibraryNodeViewModel libraryNode, string folderPath)
        {
            if (libraryNode == null || !libraryNode.IsLibrary || string.IsNullOrWhiteSpace(folderPath)) return null;

            if (libraryNode.Children.Any(c => c.FullPath.Equals(folderPath, System.StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var folderName = System.IO.Path.GetFileName(folderPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

            var folderNode = new LibraryNodeViewModel
            {
                Id = System.Guid.NewGuid().ToString(),
                Name = folderName,
                FullPath = folderPath,
                IsLibrary = false,
                ParentLibrary = libraryNode
            };
            folderNode.CheckAndAddDummyChild();

            libraryNode.Children.Add(folderNode);
            libraryNode.IsExpanded = true;
            SaveLibrariesToSettings();
            return folderNode;
        }

        public void RemoveFolderFromLibrary(LibraryNodeViewModel folderNode)
        {
            if (folderNode == null || folderNode.IsLibrary || folderNode.ParentLibrary == null) return;
            folderNode.ParentLibrary.Children.Remove(folderNode);
            SaveLibrariesToSettings();
        }

        public void SaveLibrariesToSettings()
        {
            var settings = _settingsService.Load();
            settings.Libraries = Libraries.Select(lib => new LibraryGroup
            {
                Id = lib.Id,
                Name = lib.Name,
                FolderPaths = lib.Children.Select(c => c.FullPath).ToList()
            }).ToList();
            _settingsService.Save(settings);
        }
    }
}
