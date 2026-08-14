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

        public IFileSystemService FileSystemService => _fileSystemService;

        [ObservableProperty]
        private string _currentFolderPath = string.Empty;

        partial void OnCurrentFolderPathChanged(string value)
        {
            CanPaste = !string.IsNullOrEmpty(value) && _clipboardFilePaths.Count > 0;
        }

        public string AppVersion => $"Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.1.0.0"}";

        [ObservableProperty]
        private ObservableCollection<ImageFile> _images = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSingleImageSelected))]
        private ImageFile? _selectedImage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedImagesCount))]
        [NotifyPropertyChangedFor(nameof(IsSingleImageSelected))]
        [NotifyPropertyChangedFor(nameof(HasMultipleImagesSelected))]
        [NotifyPropertyChangedFor(nameof(HasAnyImageSelected))]
        [NotifyPropertyChangedFor(nameof(MultiSelectionSummary))]
        private ObservableCollection<ImageFile> _selectedImages = new();

        public int SelectedImagesCount => SelectedImages.Count;
        public bool IsSingleImageSelected => SelectedImages.Count == 1 && SelectedImage != null;
        public bool HasMultipleImagesSelected => SelectedImages.Count > 1;
        public bool HasAnyImageSelected => SelectedImages.Count > 0;
        public string MultiSelectionSummary => $"{SelectedImages.Count} 件の画像を選択中";

        [ObservableProperty]
        private bool _canPaste;

        private readonly List<string> _clipboardFilePaths = new();
        private bool _isClipboardCut = false;
        public IReadOnlyList<string> ClipboardFilePaths => _clipboardFilePaths;
        public bool IsClipboardCut => _isClipboardCut;

        public event System.EventHandler<DirectoryNodeViewModel>? FolderSelectedEvent;
        public event System.EventHandler<(string titleKey, string messageKey)>? ShowMessageRequested;
        public event System.EventHandler<(string title, string message)>? ShowDirectMessageRequested;

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

        [ObservableProperty]
        private bool _showOnlyFavorites;

        partial void OnShowOnlyFavoritesChanged(bool value)
        {
            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                _ = LoadImagesAsync(CurrentFolderPath);
            }
        }

        public Task? CurrentLoadTask { get; private set; }

        public async Task ReloadCurrentFolderAsync()
        {
            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                await LoadImagesAsync(CurrentFolderPath);
            }
        }

        [ObservableProperty]
        private int _ratingFilterIndex = 0; // 0: All, 1: ★1, 2: ★2, 3: ★3, 4: ★4, 5: ★5, 6: No Rating (0)

        partial void OnRatingFilterIndexChanged(int value)
        {
            var settings = _settingsService.Load();
            if (settings.RatingFilterIndex != value)
            {
                settings.RatingFilterIndex = value;
                _settingsService.Save(settings);
            }

            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                _ = LoadImagesAsync(CurrentFolderPath);
            }
        }

        [RelayCommand]
        private void ToggleFavorite(ImageFile? image)
        {
            if (image == null) return;
            image.IsFavorite = !image.IsFavorite;
            if (ShowOnlyFavorites && !image.IsFavorite)
            {
                Images.Remove(image);
            }
        }

        [RelayCommand]
        public void SetRating(object? parameter)
        {
            if (parameter == null) return;
            int targetRating = 0;
            if (parameter is int r) targetRating = r;
            else if (parameter is string s && int.TryParse(s, out int parsed)) targetRating = parsed;

            var targets = SelectedImages.Count > 0 ? SelectedImages.ToList() : (SelectedImage != null ? new List<ImageFile> { SelectedImage } : new List<ImageFile>());
            if (targets.Count == 0) return;

            int clamped = Math.Clamp(targetRating, 0, 5);
            foreach (var img in targets)
            {
                img.Rating = clamped;
                CheckAndRemoveIfFilteredOut(img);
            }
        }

        [RelayCommand]
        public void ToggleStarRating(object? parameter)
        {
            if (parameter == null) return;
            int star = 0;
            if (parameter is int r) star = r;
            else if (parameter is string s && int.TryParse(s, out int parsed)) star = parsed;

            var targets = SelectedImages.Count > 0 ? SelectedImages.ToList() : (SelectedImage != null ? new List<ImageFile> { SelectedImage } : new List<ImageFile>());
            if (targets.Count == 0) return;

            int newRating = (targets.Count == 1 && targets[0].Rating == star) ? 0 : Math.Clamp(star, 0, 5);
            foreach (var img in targets)
            {
                img.Rating = newRating;
                CheckAndRemoveIfFilteredOut(img);
            }
        }

        private void CheckAndRemoveIfFilteredOut(ImageFile image)
        {
            if (RatingFilterIndex >= 1 && RatingFilterIndex <= 5 && image.Rating != RatingFilterIndex)
            {
                Images.Remove(image);
            }
            else if (RatingFilterIndex == 6 && image.Rating != 0)
            {
                Images.Remove(image);
            }
        }

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

        private static async Task LoadExifForListAsync(IEnumerable<ImageFile> items)
        {
            var unloaded = items.Where(i => !i.IsExifLoaded).ToList();
            if (unloaded.Count == 0) return;

            using var semaphore = new System.Threading.SemaphoreSlim(4);
            foreach (var chunk in unloaded.Chunk(50))
            {
                var tasks = chunk.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await item.LoadExifAsync();
                    }
                    catch { }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                await Task.WhenAll(tasks);
            }
        }

        private async Task SortImagesAsync()
        {
            if (Images.Count == 0 || _isSorting) return;
            _isSorting = true;

            try
            {
                var list = Images.ToList();

                var sorted = await Task.Run(() =>
                {
                    if (SortFieldIndex == 0) // LastWriteTime
                    {
                        if (SortDirectionIndex == 0) // Ascending
                            return list.OrderBy(i => i.LastWriteTime).ToList();
                        else
                            return list.OrderByDescending(i => i.LastWriteTime).ToList();
                    }
                    else if (SortFieldIndex == 1) // DateTaken
                    {
                        if (SortDirectionIndex == 0)
                            return list.OrderBy(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                        else
                            return list.OrderByDescending(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                    }
                    else // Rating
                    {
                        if (SortDirectionIndex == 0)
                            return list.OrderBy(i => i.Rating).ThenByDescending(i => i.LastWriteTime).ToList();
                        else
                            return list.OrderByDescending(i => i.Rating).ThenByDescending(i => i.LastWriteTime).ToList();
                    }
                });

                RunOnUIThread(() =>
                {
                    var selected = SelectedImage;
                    
                    // Assign new collection at once to prevent thousands of UI layout Move events
                    Images = new ObservableCollection<ImageFile>(sorted);

                    if (selected != null && Images.Contains(selected))
                    {
                        SelectedImage = selected;
                    }
                });

                // Load EXIF asynchronously in background if DateTaken sorting selected
                if (SortFieldIndex == 1)
                {
                    _ = Task.Run(async () =>
                    {
                        await LoadExifForListAsync(list);

                        var reSorted = list.OrderBy(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                        if (SortDirectionIndex != 0) reSorted.Reverse();

                        if (!list.SequenceEqual(reSorted))
                        {
                            RunOnUIThread(() =>
                            {
                                var sel = SelectedImage;
                                Images = new ObservableCollection<ImageFile>(reSorted);
                                if (sel != null && Images.Contains(sel)) SelectedImage = sel;
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during SortImagesAsync: {ex}");
            }
            finally
            {
                _isSorting = false;
            }
        }

        private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
        private readonly DatabaseService _databaseService;

        public MainViewModel(IFileSystemService fileSystemService, ISettingsService settingsService, DatabaseService? databaseService = null)
        {
            _fileSystemService = fileSystemService;
            _settingsService = settingsService;
            _databaseService = databaseService ?? DatabaseService.Instance;
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
            RatingFilterIndex = settings.RatingFilterIndex;

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
                var node = currentList.FirstOrDefault(n => 
                    n.Name.Equals(part, System.StringComparison.OrdinalIgnoreCase) || 
                    n.FullPath.Equals(currentPath, System.StringComparison.OrdinalIgnoreCase) ||
                    n.FullPath.TrimEnd('\\').Equals(currentPath.TrimEnd('\\'), System.StringComparison.OrdinalIgnoreCase));
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

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_DISPLAYNAME = 0x000000200;

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        private static string GetShellDisplayName(string path)
        {
            try
            {
                var shfi = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(path, 0, ref shfi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(shfi), SHGFI_DISPLAYNAME);
                if (result != IntPtr.Zero && !string.IsNullOrWhiteSpace(shfi.szDisplayName))
                {
                    return shfi.szDisplayName;
                }
            }
            catch { }
            return string.Empty;
        }

        private void LoadDrives()
        {
            try
            {
                foreach (var drive in System.IO.DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        string volumeLabel = string.Empty;
                        try
                        {
                            volumeLabel = drive.VolumeLabel;
                        }
                        catch { }

                        string driveLetter = drive.Name.TrimEnd('\\');
                        string displayName;

                        if (!string.IsNullOrWhiteSpace(volumeLabel))
                        {
                            displayName = $"{volumeLabel} ({driveLetter})";
                        }
                        else
                        {
                            string shellDisplayName = GetShellDisplayName(drive.Name);
                            if (!string.IsNullOrWhiteSpace(shellDisplayName))
                            {
                                displayName = shellDisplayName;
                            }
                            else
                            {
                                string defaultLabel = drive.DriveType switch
                                {
                                    DriveType.Fixed => "ローカル ディスク",
                                    DriveType.Removable => "USB ドライブ",
                                    DriveType.CDRom => "CD ドライブ",
                                    DriveType.Network => "ネットワーク ドライブ",
                                    _ => "ローカル ディスク"
                                };
                                displayName = $"{defaultLabel} ({driveLetter})";
                            }
                        }

                        Folders.Add(new DirectoryNodeViewModel(drive.Name, displayName));
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
            var task = LoadImagesInternalAsync(folderPath);
            CurrentLoadTask = task;
            await task;
        }

        private async Task LoadImagesInternalAsync(string folderPath)
        {
            RunOnUIThread(() =>
            {
                Images.Clear();
                SelectedImages.Clear();
                SelectedImage = null;
                OnPropertyChanged(nameof(SelectedImagesCount));
                OnPropertyChanged(nameof(IsSingleImageSelected));
                OnPropertyChanged(nameof(HasMultipleImagesSelected));
                OnPropertyChanged(nameof(HasAnyImageSelected));
                OnPropertyChanged(nameof(MultiSelectionSummary));
            });
            
            await Task.Run(async () => 
            {
                var files = _fileSystemService.GetImageFiles(folderPath).ToList();
                var newImages = files.Select(f => new ImageFile(f)).ToList();
                
                string libId = GetFolderLibraryId(folderPath);
                foreach (var img in newImages)
                {
                    try
                    {
                        _databaseService.SyncImageRecord(img, libId, folderPath);
                    }
                    catch { }
                }

                // 1. お気に入りフィルター
                if (ShowOnlyFavorites)
                {
                    newImages = newImages.Where(i => i.IsFavorite).ToList();
                }

                // 2. レートフィルター (AND条件)
                if (RatingFilterIndex >= 1 && RatingFilterIndex <= 5)
                {
                    int targetRating = RatingFilterIndex;
                    newImages = newImages.Where(i => i.Rating == targetRating).ToList();
                }
                else if (RatingFilterIndex == 6)
                {
                    newImages = newImages.Where(i => i.Rating == 0).ToList();
                }

                // 3. ソート
                if (SortFieldIndex == 0) // LastWriteTime
                {
                    if (SortDirectionIndex == 0) // Ascending
                        newImages = newImages.OrderBy(i => i.LastWriteTime).ToList();
                    else
                        newImages = newImages.OrderByDescending(i => i.LastWriteTime).ToList();
                }
                else if (SortFieldIndex == 1) // DateTaken
                {
                    if (SortDirectionIndex == 0)
                        newImages = newImages.OrderBy(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                    else
                        newImages = newImages.OrderByDescending(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                }
                else // Rating
                {
                    if (SortDirectionIndex == 0)
                        newImages = newImages.OrderBy(i => i.Rating).ThenByDescending(i => i.LastWriteTime).ToList();
                    else
                        newImages = newImages.OrderByDescending(i => i.Rating).ThenByDescending(i => i.LastWriteTime).ToList();
                }
                
                RunOnUIThread(() => 
                {
                    Images = new ObservableCollection<ImageFile>(newImages);

                    var settings = _settingsService.Load();
                    if (!string.IsNullOrEmpty(settings.SelectedImageFilePath))
                    {
                        var target = Images.FirstOrDefault(i => i.FilePath.Equals(settings.SelectedImageFilePath, System.StringComparison.OrdinalIgnoreCase));
                        if (target != null)
                        {
                            SelectedImage = target;
                            SelectedImages.Clear();
                            SelectedImages.Add(target);
                        }
                        else if (Images.Count > 0)
                        {
                            SelectedImage = Images[0];
                            SelectedImages.Clear();
                            SelectedImages.Add(Images[0]);
                        }
                    }
                    else if (Images.Count > 0)
                    {
                        SelectedImage = Images[0];
                        SelectedImages.Clear();
                        SelectedImages.Add(Images[0]);
                    }
                    OnPropertyChanged(nameof(SelectedImagesCount));
                    OnPropertyChanged(nameof(IsSingleImageSelected));
                    OnPropertyChanged(nameof(HasMultipleImagesSelected));
                    OnPropertyChanged(nameof(HasAnyImageSelected));
                    OnPropertyChanged(nameof(MultiSelectionSummary));
                });

                if (SortFieldIndex == 1) // DateTaken background loading
                {
                    await LoadExifForListAsync(newImages);

                    var reSorted = newImages.OrderBy(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                    if (SortDirectionIndex != 0) reSorted.Reverse();

                    if (!newImages.SequenceEqual(reSorted))
                    {
                        RunOnUIThread(() =>
                        {
                            var sel = SelectedImage;
                            Images = new ObservableCollection<ImageFile>(reSorted);
                            if (sel != null && Images.Contains(sel)) SelectedImage = sel;
                        });
                    }
                }
            });
        }

        private static string GetFolderLibraryId(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return "folder_empty";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(folderPath.ToLowerInvariant());
            byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return "folder_" + System.Convert.ToHexString(hash).Substring(0, 16);
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

            var filePath = await _fileSystemService.SaveFilePickerAsync("ImageManagerBackup.zip", "Zip Package (*.zip)", ".zip");
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
            var filePath = await _fileSystemService.OpenFilePickerAsync("Zip Package / JSON File (*.zip)", ".zip");
            if (string.IsNullOrEmpty(filePath))
            {
                filePath = await _fileSystemService.OpenFilePickerAsync("JSON File (*.json)", ".json");
            }
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
            try
            {
                DatabaseService.Instance.DeleteLibrary(libraryNode.Id);
            }
            catch { }
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

            foreach (var lib in Libraries)
            {
                try
                {
                    DatabaseService.Instance.UpsertLibrary(lib.Id, lib.Name, lib.FullPath ?? string.Empty);
                }
                catch { }
            }
        }

        public async Task<bool> RelocateLibraryFolderAsync(LibraryNodeViewModel folderNode, string newFolderPath)
        {
            if (folderNode == null || string.IsNullOrWhiteSpace(newFolderPath) || !System.IO.Directory.Exists(newFolderPath))
                return false;

            string oldPath = folderNode.FullPath;
            string newFolderName = System.IO.Path.GetFileName(newFolderPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(newFolderName)) newFolderName = newFolderPath;

            folderNode.FullPath = newFolderPath;
            folderNode.Name = newFolderName;

            try
            {
                DatabaseService.Instance.RelocateFolderPath(oldPath, newFolderPath);
            }
            catch { }

            SaveLibrariesToSettings();

            await SelectFolderFromTreeAsync(newFolderPath);
            return true;
        }

        #region Multi-Selection and File Operations

        public void UpdateSelectedImages(IEnumerable<ImageFile> selected)
        {
            var list = selected.ToList();
            SelectedImages.Clear();
            foreach (var item in list)
            {
                SelectedImages.Add(item);
            }

            if (SelectedImages.Count == 1)
            {
                SelectedImage = SelectedImages[0];
            }
            else
            {
                SelectedImage = null;
            }

            OnPropertyChanged(nameof(SelectedImagesCount));
            OnPropertyChanged(nameof(IsSingleImageSelected));
            OnPropertyChanged(nameof(HasMultipleImagesSelected));
            OnPropertyChanged(nameof(HasAnyImageSelected));
            OnPropertyChanged(nameof(MultiSelectionSummary));
        }

        public void CopySelectedToClipboard()
        {
            if (SelectedImages.Count == 0) return;
            _clipboardFilePaths.Clear();
            _clipboardFilePaths.AddRange(SelectedImages.Select(i => i.FilePath));
            _isClipboardCut = false;
            CanPaste = !string.IsNullOrEmpty(CurrentFolderPath) && _clipboardFilePaths.Count > 0;
        }

        public void CutSelectedToClipboard()
        {
            if (SelectedImages.Count == 0) return;
            _clipboardFilePaths.Clear();
            _clipboardFilePaths.AddRange(SelectedImages.Select(i => i.FilePath));
            _isClipboardCut = true;
            CanPaste = !string.IsNullOrEmpty(CurrentFolderPath) && _clipboardFilePaths.Count > 0;
        }

        public async Task<int> CopyFilesToFolderAsync(IEnumerable<string> filePaths, string destDirectory)
        {
            var files = filePaths.Where(f => System.IO.File.Exists(f)).ToList();
            if (files.Count == 0 || string.IsNullOrEmpty(destDirectory) || !System.IO.Directory.Exists(destDirectory))
                return 0;

            int copiedCount = 0;
            bool isCurrentFolder = destDirectory.Equals(CurrentFolderPath, System.StringComparison.OrdinalIgnoreCase);
            string libId = GetFolderLibraryId(destDirectory);

            await Task.Run(() =>
            {
                foreach (var srcPath in files)
                {
                    try
                    {
                        string fileName = System.IO.Path.GetFileName(srcPath);
                        string destPath = GetUniqueDestinationPath(destDirectory, fileName);
                        System.IO.File.Copy(srcPath, destPath, overwrite: false);
                        copiedCount++;

                        if (isCurrentFolder)
                        {
                            var newImg = new ImageFile(destPath);
                            try
                            {
                                DatabaseService.Instance.SyncImageRecord(newImg, libId, destDirectory);
                            }
                            catch { }

                            RunOnUIThread(() =>
                            {
                                Images.Add(newImg);
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to copy file '{srcPath}': {ex.Message}");
                    }
                }
            });

            if (isCurrentFolder && copiedCount > 0)
            {
                _ = SortImagesAsync();
            }

            return copiedCount;
        }

        public async Task<int> MoveFilesToFolderAsync(IEnumerable<string> filePaths, string destDirectory)
        {
            var files = filePaths.Where(f => System.IO.File.Exists(f)).ToList();
            if (files.Count == 0 || string.IsNullOrEmpty(destDirectory) || !System.IO.Directory.Exists(destDirectory))
                return 0;

            bool isCurrentFolder = destDirectory.Equals(CurrentFolderPath, System.StringComparison.OrdinalIgnoreCase);
            int movedCount = 0;
            var movedImages = new List<ImageFile>();

            await Task.Run(() =>
            {
                foreach (var srcPath in files)
                {
                    try
                    {
                        string fileName = System.IO.Path.GetFileName(srcPath);
                        string destPath = GetUniqueDestinationPath(destDirectory, fileName);
                        
                        // If destination is exact same path, skip
                        if (srcPath.Equals(destPath, System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        System.IO.File.Move(srcPath, destPath);
                        movedCount++;

                        var matchingImg = Images.FirstOrDefault(i => i.FilePath.Equals(srcPath, System.StringComparison.OrdinalIgnoreCase));
                        if (matchingImg != null)
                        {
                            movedImages.Add(matchingImg);
                        }

                        try
                        {
                            DatabaseService.Instance.RelocateFolderPath(srcPath, destPath);
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to move file '{srcPath}': {ex.Message}");
                    }
                }
            });

            if (!isCurrentFolder && movedImages.Count > 0)
            {
                RunOnUIThread(() =>
                {
                    foreach (var img in movedImages)
                    {
                        Images.Remove(img);
                        SelectedImages.Remove(img);
                    }
                    if (SelectedImages.Count == 1)
                    {
                        SelectedImage = SelectedImages[0];
                    }
                    else
                    {
                        SelectedImage = null;
                    }
                    OnPropertyChanged(nameof(SelectedImagesCount));
                    OnPropertyChanged(nameof(IsSingleImageSelected));
                    OnPropertyChanged(nameof(HasMultipleImagesSelected));
                    OnPropertyChanged(nameof(HasAnyImageSelected));
                    OnPropertyChanged(nameof(MultiSelectionSummary));
                });
            }

            return movedCount;
        }

        public async Task<int> PasteFromClipboardAsync(string? targetFolder = null)
        {
            string destDir = targetFolder ?? CurrentFolderPath;
            if (string.IsNullOrEmpty(destDir) || !System.IO.Directory.Exists(destDir) || _clipboardFilePaths.Count == 0)
                return 0;

            int count = 0;
            if (_isClipboardCut)
            {
                count = await MoveFilesToFolderAsync(_clipboardFilePaths, destDir);
                _clipboardFilePaths.Clear();
                _isClipboardCut = false;
                CanPaste = false;
            }
            else
            {
                count = await CopyFilesToFolderAsync(_clipboardFilePaths, destDir);
            }
            return count;
        }

        public async Task<int> DeleteSelectedImagesAsync()
        {
            var list = SelectedImages.ToList();
            if (list.Count == 0) return 0;

            int deletedCount = 0;
            await Task.Run(() =>
            {
                foreach (var img in list)
                {
                    try
                    {
                        if (System.IO.File.Exists(img.FilePath))
                        {
                            System.IO.File.Delete(img.FilePath);
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to delete file '{img.FilePath}': {ex.Message}");
                    }
                }
            });

            RunOnUIThread(() =>
            {
                foreach (var img in list)
                {
                    Images.Remove(img);
                    SelectedImages.Remove(img);
                }
                SelectedImage = null;
                OnPropertyChanged(nameof(SelectedImagesCount));
                OnPropertyChanged(nameof(IsSingleImageSelected));
                OnPropertyChanged(nameof(HasMultipleImagesSelected));
                OnPropertyChanged(nameof(HasAnyImageSelected));
                OnPropertyChanged(nameof(MultiSelectionSummary));
            });

            return deletedCount;
        }

        private static string GetUniqueDestinationPath(string destDir, string originalFileName)
        {
            string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(originalFileName);
            string ext = System.IO.Path.GetExtension(originalFileName);
            string destPath = System.IO.Path.Combine(destDir, originalFileName);
            int count = 1;

            while (System.IO.File.Exists(destPath))
            {
                string newName = count == 1 
                    ? $"{nameWithoutExt} - コピー{ext}" 
                    : $"{nameWithoutExt} - コピー ({count}){ext}";
                destPath = System.IO.Path.Combine(destDir, newName);
                count++;
            }

            return destPath;
        }

        public void RaiseDirectMessage(string title, string message)
        {
            ShowDirectMessageRequested?.Invoke(this, (title, message));
        }

        #endregion
    }
}
