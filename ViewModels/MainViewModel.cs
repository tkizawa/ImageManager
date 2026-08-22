using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageManager.Models;
using ImageManager.Services;

namespace ImageManager.ViewModels
{
    /// <summary>
    /// メイン画面（MainWindow）のUI状態およびビジネスロジックを統括するViewModelクラス。
    /// フォルダ走査、非同期サムネイル読み込み、ソート・フィルタリング（お気に入り・レーティング・Exif撮影日）、
    /// 複数選択、クリップボード（コピー・切り取り・貼り付け）、ライブラリ管理、設定永続化を提供します。
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly ISettingsService _settingsService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
        private readonly DatabaseService _databaseService;

        /// <summary>ファイルシステムサービスへの参照を取得します。</summary>
        public IFileSystemService FileSystemService => _fileSystemService;

        /// <summary>現在選択されているフォルダの絶対パス</summary>
        [ObservableProperty]
        private string _currentFolderPath = string.Empty;

        /// <summary>
        /// 選択フォルダ変更時、貼り付けコマンドの有効状態（CanPaste）を更新します。
        /// </summary>
        /// <param name="value">変更後のフォルダパス</param>
        partial void OnCurrentFolderPathChanged(string value)
        {
            CanPaste = !string.IsNullOrEmpty(value) && _clipboardFilePaths.Count > 0;
        }

        /// <summary>アセンブリから取得したアプリケーションバージョン文字列</summary>
        public string AppVersion => $"Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.1.3.0"}";

        /// <summary>現在表示中の画像ファイル一覧コレクション</summary>
        [ObservableProperty]
        private ObservableCollection<ImageFile> _images = new();

        /// <summary>現在フォーカスまたは単一選択されている画像</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSingleImageSelected))]
        private ImageFile? _selectedImage;

        /// <summary>複数選択されている画像のコレクション</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedImagesCount))]
        [NotifyPropertyChangedFor(nameof(IsSingleImageSelected))]
        [NotifyPropertyChangedFor(nameof(HasMultipleImagesSelected))]
        [NotifyPropertyChangedFor(nameof(HasAnyImageSelected))]
        [NotifyPropertyChangedFor(nameof(MultiSelectionSummary))]
        private ObservableCollection<ImageFile> _selectedImages = new();

        /// <summary>選択中の画像件数</summary>
        public int SelectedImagesCount => SelectedImages.Count;

        /// <summary>1件のみ画像が選択されているかどうか</summary>
        public bool IsSingleImageSelected => SelectedImages.Count == 1 && SelectedImage != null;

        /// <summary>複数件の画像が選択されているかどうか</summary>
        public bool HasMultipleImagesSelected => SelectedImages.Count > 1;

        /// <summary>1件以上の画像が選択されているかどうか</summary>
        public bool HasAnyImageSelected => SelectedImages.Count > 0;

        /// <summary>複数選択状態の概要テキスト（例: "3 件の画像を選択中"）</summary>
        public string MultiSelectionSummary => $"{SelectedImages.Count} 件の画像を選択中";

        /// <summary>クリップボードに画像が存在し、貼り付け操作が可能であるか</summary>
        [ObservableProperty]
        private bool _canPaste;

        /// <summary>クリップボード保持中のファイルパス一覧</summary>
        private readonly List<string> _clipboardFilePaths = new();

        /// <summary>クリップボード操作が「切り取り（Cut）」であるか</summary>
        private bool _isClipboardCut = false;

        /// <summary>クリップボードに保持されたファイルパスの読み取り専用リスト</summary>
        public IReadOnlyList<string> ClipboardFilePaths => _clipboardFilePaths;

        /// <summary>クリップボードが切り取り状態であるか</summary>
        public bool IsClipboardCut => _isClipboardCut;

        /// <summary>フォルダ選択時にツリービューへ選択状態を伝播するイベント</summary>
        public event System.EventHandler<DirectoryNodeViewModel>? FolderSelectedEvent;

        /// <summary>ローカライズ文字列キーによるダイアログ表示要求イベント</summary>
        public event System.EventHandler<(string titleKey, string messageKey)>? ShowMessageRequested;

        /// <summary>直接指定文字列によるダイアログ表示要求イベント</summary>
        public event System.EventHandler<(string title, string message)>? ShowDirectMessageRequested;

        /// <summary>
        /// 選択画像変更時にExifの非同期読み込みを開始し、最終選択ファイルを設定へ保存します。
        /// </summary>
        /// <param name="value">変更後の選択画像</param>
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

        /// <summary>サムネイルグリッドのアイコン表示サイズ（ピクセル）</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ThumbnailPanelWidth))]
        private double _thumbnailSize = 100;

        /// <summary>
        /// サムネイルサイズ変更時に設定へ保存します。
        /// </summary>
        /// <param name="value">変更後のサイズ</param>
        partial void OnThumbnailSizeChanged(double value)
        {
            var settings = _settingsService.Load();
            if (Math.Abs(settings.ThumbnailSize - value) > 0.1)
            {
                settings.ThumbnailSize = value;
                _settingsService.Save(settings);
            }
        }

        /// <summary>サムネイルアイテム全体の幅（余白を含む）</summary>
        public double ThumbnailPanelWidth => ThumbnailSize + 20;

        /// <summary>ドライブおよびフォルダツリーのルートコレクション</summary>
        [ObservableProperty]
        private ObservableCollection<DirectoryNodeViewModel> _folders = new();

        /// <summary>お気に入りフォルダパスのコレクション</summary>
        [ObservableProperty]
        private ObservableCollection<string> _favoriteFolders = new();

        /// <summary>最近開いたフォルダ履歴のコレクション</summary>
        [ObservableProperty]
        private ObservableCollection<string> _historyFolders = new();

        /// <summary>登録済みカスタムライブラリグループのコレクション</summary>
        [ObservableProperty]
        private ObservableCollection<LibraryNodeViewModel> _libraries = new();

        /// <summary>ソート対象項目のインデックス（0: 更新日時, 1: 撮影日時, 2: レーティング）</summary>
        [ObservableProperty]
        private int _sortFieldIndex = 0;

        /// <summary>ソート昇順/降順のインデックス（0: 昇順, 1: 降順）</summary>
        [ObservableProperty]
        private int _sortDirectionIndex = 1;

        /// <summary>お気に入りのみ表示するフィルターフラグ</summary>
        [ObservableProperty]
        private bool _showOnlyFavorites;

        /// <summary>
        /// お気に入りフィルター切り替え時に画像一覧を再読み込みします。
        /// </summary>
        /// <param name="value">変更後のフィルターフラグ</param>
        partial void OnShowOnlyFavoritesChanged(bool value)
        {
            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                _ = LoadImagesAsync(CurrentFolderPath);
            }
        }

        /// <summary>現在実行中の画像読み込み非同期タスク</summary>
        public Task? CurrentLoadTask { get; private set; }

        /// <summary>
        /// 現在のフォルダの画像一覧を強制的に再読み込みします。
        /// </summary>
        public async Task ReloadCurrentFolderAsync()
        {
            if (!string.IsNullOrEmpty(CurrentFolderPath))
            {
                await LoadImagesAsync(CurrentFolderPath);
            }
        }

        /// <summary>レーティングフィルターのインデックス（0: すべて, 1〜5: ★1〜★5, 6: レーティングなし）</summary>
        [ObservableProperty]
        private int _ratingFilterIndex = 0;

        /// <summary>
        /// レーティングフィルター変更時に設定を保存し、画像一覧を再絞り込みします。
        /// </summary>
        /// <param name="value">変更後のインデックス</param>
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

        /// <summary>
        /// 指定画像の「お気に入り」状態を反転（トグル）します。
        /// </summary>
        /// <param name="image">対象の画像モデル</param>
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

        /// <summary>
        /// 選択中の画像（単一または複数）に指定されたレーティング（0〜5）を一括設定します。
        /// </summary>
        /// <param name="parameter">レーティング値（int または string）</param>
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

        /// <summary>
        /// 指定した星のレーティングをトグル設定します（既に同じ星が設定されていれば0に解除）。
        /// </summary>
        /// <param name="parameter">星番号（1〜5）</param>
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

        /// <summary>
        /// 現在のレーティングフィルター条件に合致しなくなった画像を一覧から除外します。
        /// </summary>
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

        /// <summary>
        /// ソート項目変更時のハンドラ。
        /// </summary>
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

        /// <summary>
        /// ソート昇順/降順変更時のハンドラ。
        /// </summary>
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

        /// <summary>
        /// 指定された画像リストのExifメタデータを並列（最大4並行）で非同期読み込みします。
        /// </summary>
        /// <param name="items">対象画像リスト</param>
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

        /// <summary>
        /// 現在の画像一覧をソート設定（更新日時・撮影日時・レーティング）に基づいて並び替えます。
        /// </summary>
        private async Task SortImagesAsync()
        {
            if (Images.Count == 0 || _isSorting) return;
            _isSorting = true;

            try
            {
                var list = Images.ToList();

                var sorted = await Task.Run(() =>
                {
                    if (SortFieldIndex == 0) // 更新日時 (LastWriteTime)
                    {
                        if (SortDirectionIndex == 0) // 昇順
                            return list.OrderBy(i => i.LastWriteTime).ToList();
                        else // 降順
                            return list.OrderByDescending(i => i.LastWriteTime).ToList();
                    }
                    else if (SortFieldIndex == 1) // 撮影日時 (DateTaken)
                    {
                        if (SortDirectionIndex == 0)
                            return list.OrderBy(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                        else
                            return list.OrderByDescending(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                    }
                    else // レーティング (Rating)
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
                    
                    // UIレイアウトイベントの連続発火を防ぐためコレクションを一括再作成
                    Images = new ObservableCollection<ImageFile>(sorted);

                    if (selected != null && Images.Contains(selected))
                    {
                        SelectedImage = selected;
                    }
                });

                // 撮影日時ソートの場合、未ロードのExifがあればバックグラウンドで補完し必要に応じて再ソート
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

        /// <summary>
        /// <see cref="MainViewModel"/> クラスの新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="fileSystemService">ファイルシステム操作サービス</param>
        /// <param name="settingsService">設定管理サービス</param>
        /// <param name="databaseService">データベースサービス（省略時はシングルトン）</param>
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
        }

        /// <summary>
        /// UIスレッド上でアクションを実行します。
        /// </summary>
        /// <param name="action">実行する処理</param>
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

        /// <summary>
        /// アプリ起動時に保存された設定（お気に入りフォルダ、履歴、ライブラリ、前回のフォルダ等）を非同期で復元します。
        /// </summary>
        public async Task InitializeAsync()
        {
            await LoadDrivesAsync();

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

        /// <summary>
        /// 指定されたパスに対応するツリービューノードを再帰的に展開・選択します。
        /// </summary>
        /// <param name="path">フォルダパス</param>
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
                
                if (string.IsNullOrEmpty(CurrentFolderPath))
                {
                    await SelectFolderFromTreeAsync(targetNode.FullPath);
                }
            }
        }

        #region Shell API Interop for Display Names
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

        /// <summary>
        /// Windows Shell API（SHGetFileInfo）を用いてエクスプローラーと同等のドライブ表示名を取得します。
        /// </summary>
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
        #endregion

        /// <summary>
        /// システム内の準備完了状態の全ドライブ（固定・リムーバブル・ネットワーク）を列挙しツリーに追加します。
        /// </summary>
        private async Task LoadDrivesAsync()
        {
            try
            {
                var drivesInfo = await Task.Run(() =>
                {
                    var list = new List<(string Name, string DisplayName)>();
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

                            list.Add((drive.Name, displayName));
                        }
                    }
                    return list;
                });

                RunOnUIThread(() =>
                {
                    foreach (var info in drivesInfo)
                    {
                        Folders.Add(new DirectoryNodeViewModel(info.Name, info.DisplayName));
                    }
                });
            }
            catch { }
        }

        /// <summary>
        /// ツリービューからフォルダが選択された際の処理。
        /// 履歴追加、設定保存、画像読み込みを実行します。
        /// </summary>
        /// <param name="folderPath">選択されたフォルダパス</param>
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

        /// <summary>
        /// フォルダ選択ダイアログを表示してフォルダを開くコマンド。
        /// </summary>
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

        /// <summary>
        /// 指定フォルダ内の画像を読み込みます。
        /// </summary>
        /// <param name="folderPath">フォルダパス</param>
        private async Task LoadImagesAsync(string folderPath)
        {
            var task = LoadImagesInternalAsync(folderPath);
            CurrentLoadTask = task;
            await task;
        }

        /// <summary>
        /// 画像一覧の内部読み込み処理。
        /// データベースからキャッシュ済みメタデータを一括マージし、フィルタ・ソートを適用してUIへ反映します。
        /// </summary>
        /// <param name="folderPath">フォルダパス</param>
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

                // DBキャッシュから1回のクエリでメタデータ（お気に入り・レーティング・Exif撮影日等）を高速マージ
                var cachedMap = _databaseService.GetFolderImageRecordsMap(folderPath, libId);
                foreach (var img in newImages)
                {
                    DatabaseService.CachedImageRecord? rec = null;
                    if (cachedMap.TryGetValue(img.FilePath, out var r1))
                    {
                        rec = r1;
                    }
                    else if (cachedMap.TryGetValue(img.FileName, out var r2))
                    {
                        rec = r2;
                    }

                    if (rec != null)
                    {
                        img.IsFavorite = rec.IsFavorite;
                        if (rec.Rating > 0 && img.Rating == 0)
                        {
                            img.Rating = rec.Rating;
                        }
                        if (!string.IsNullOrEmpty(rec.Category) && string.IsNullOrEmpty(img.Category))
                        {
                            img.Category = rec.Category;
                        }
                        if (!string.IsNullOrEmpty(rec.DateTaken) && string.IsNullOrEmpty(img.DateTaken))
                        {
                            img.DateTaken = rec.DateTaken;
                        }
                    }
                }

                // UIのサムネイル表示をブロックしないようバックグラウンドでDBバッチ同期を実行
                var imagesForSync = newImages.ToList();
                _ = Task.Run(() =>
                {
                    try
                    {
                        _databaseService.BatchSyncImageRecords(imagesForSync, libId, folderPath);
                    }
                    catch { }
                });

                // 1. お気に入りフィルター
                if (ShowOnlyFavorites)
                {
                    newImages = newImages.Where(i => i.IsFavorite).ToList();
                }

                // 2. レーティングフィルター
                if (RatingFilterIndex >= 1 && RatingFilterIndex <= 5)
                {
                    int targetRating = RatingFilterIndex;
                    newImages = newImages.Where(i => i.Rating == targetRating).ToList();
                }
                else if (RatingFilterIndex == 6)
                {
                    newImages = newImages.Where(i => i.Rating == 0).ToList();
                }

                // 3. ソート適用
                if (SortFieldIndex == 0) // 更新日時
                {
                    if (SortDirectionIndex == 0)
                        newImages = newImages.OrderBy(i => i.LastWriteTime).ToList();
                    else
                        newImages = newImages.OrderByDescending(i => i.LastWriteTime).ToList();
                }
                else if (SortFieldIndex == 1) // 撮影日時
                {
                    if (SortDirectionIndex == 0)
                        newImages = newImages.OrderBy(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                    else
                        newImages = newImages.OrderByDescending(i => string.IsNullOrWhiteSpace(i.DateTaken) ? i.LastWriteTime.ToString("yyyy:MM:dd HH:mm:ss") : i.DateTaken).ToList();
                }
                else // レーティング
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

                // 撮影日時ソート時のExifバックグラウンドロード
                if (SortFieldIndex == 1)
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

        /// <summary>
        /// フォルダパスから一意のライブラリ識別子文字列（folder_XXXX）を生成します。
        /// </summary>
        private static string GetFolderLibraryId(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return "folder_empty";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(folderPath.ToLowerInvariant());
            byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return "folder_" + System.Convert.ToHexString(hash).Substring(0, 16);
        }

        /// <summary>
        /// フォルダをお気に入りに追加するコマンド。
        /// </summary>
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

        /// <summary>
        /// フォルダをお気に入りから削除するコマンド。
        /// </summary>
        [RelayCommand]
        private void RemoveFavoriteFolder(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && FavoriteFolders.Contains(folderPath))
            {
                FavoriteFolders.Remove(folderPath);
                SaveFavorites();
            }
        }

        /// <summary>
        /// お気に入りフォルダを選択して開くコマンド。
        /// </summary>
        [RelayCommand]
        private async Task SelectFavoriteFolderAsync(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
            {
                await SelectFolderFromTreeAsync(folderPath);
                _ = ExpandAndSelectPathAsync(folderPath);
            }
        }

        /// <summary>お気に入りフォルダ一覧を設定ファイルへ保存します。</summary>
        private void SaveFavorites()
        {
            var settings = _settingsService.Load();
            settings.FavoriteFolders = FavoriteFolders.ToList();
            _settingsService.Save(settings);
        }

        /// <summary>
        /// 最近開いたフォルダ履歴に追加します（重複排除・最大20件保持）。
        /// </summary>
        private void AddHistoryFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;

            if (HistoryFolders.Contains(folderPath))
            {
                HistoryFolders.Remove(folderPath);
            }

            HistoryFolders.Insert(0, folderPath);

            while (HistoryFolders.Count > 20)
            {
                HistoryFolders.RemoveAt(HistoryFolders.Count - 1);
            }

            SaveHistory();
        }

        /// <summary>履歴フォルダを削除するコマンド。</summary>
        [RelayCommand]
        private void RemoveHistoryFolder(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && HistoryFolders.Contains(folderPath))
            {
                HistoryFolders.Remove(folderPath);
                SaveHistory();
            }
        }

        /// <summary>履歴フォルダを選択して開くコマンド。</summary>
        [RelayCommand]
        private async Task SelectHistoryFolderAsync(string? folderPath)
        {
            if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
            {
                await SelectFolderFromTreeAsync(folderPath);
                _ = ExpandAndSelectPathAsync(folderPath);
            }
        }

        /// <summary>履歴フォルダ一覧を設定ファイルへ保存します。</summary>
        private void SaveHistory()
        {
            var settings = _settingsService.Load();
            settings.HistoryFolders = HistoryFolders.ToList();
            _settingsService.Save(settings);
        }

        /// <summary>
        /// アプリ設定およびデータベースをZIP形式またはJSON形式でエクスポートするコマンド。
        /// </summary>
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

        /// <summary>
        /// 設定およびデータベースバックアップをインポートして復元するコマンド。
        /// </summary>
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

        /// <summary>
        /// 新しい仮想ライブラリグループを作成します。
        /// </summary>
        /// <param name="name">ライブラリ名</param>
        /// <returns>作成された <see cref="LibraryNodeViewModel"/></returns>
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

        /// <summary>
        /// 指定されたライブラリを削除します。
        /// </summary>
        /// <param name="libraryNode">削除対象のライブラリノード</param>
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

        /// <summary>
        /// ライブラリの名前を変更します。
        /// </summary>
        /// <param name="libraryNode">対象ライブラリノード</param>
        /// <param name="newName">新しいライブラリ名</param>
        public void RenameLibrary(LibraryNodeViewModel libraryNode, string newName)
        {
            if (libraryNode == null || !libraryNode.IsLibrary || string.IsNullOrWhiteSpace(newName)) return;
            libraryNode.Name = newName.Trim();
            SaveLibrariesToSettings();
        }

        /// <summary>
        /// ライブラリにフォルダを登録します。
        /// </summary>
        /// <param name="libraryNode">親ライブラリノード</param>
        /// <param name="folderPath">登録するフォルダパス</param>
        /// <returns>作成された子フォルダノード</returns>
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

        /// <summary>
        /// ライブラリから登録フォルダのリンクを解除（削除）します。
        /// </summary>
        /// <param name="folderNode">削除対象のフォルダノード</param>
        public void RemoveFolderFromLibrary(LibraryNodeViewModel folderNode)
        {
            if (folderNode == null || folderNode.IsLibrary || folderNode.ParentLibrary == null) return;
            folderNode.ParentLibrary.Children.Remove(folderNode);
            SaveLibrariesToSettings();
        }

        /// <summary>
        /// 現在のライブラリ構造を設定ファイルおよびデータベースへ同期保存します。
        /// </summary>
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

        /// <summary>
        /// ライブラリ内フォルダのパスが移動・変更された場合に、データベースのパス追跡および設定を更新します。
        /// </summary>
        /// <param name="folderNode">対象フォルダノード</param>
        /// <param name="newFolderPath">新しいフォルダパス</param>
        /// <returns>成功時は true</returns>
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

        /// <summary>
        /// UI上の選択画像コレクションを同期更新します。
        /// </summary>
        /// <param name="selected">選択された画像コレクション</param>
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

        /// <summary>
        /// 選択中の画像をクリップボードにコピーとして保持します。
        /// </summary>
        public void CopySelectedToClipboard()
        {
            if (SelectedImages.Count == 0) return;
            _clipboardFilePaths.Clear();
            _clipboardFilePaths.AddRange(SelectedImages.Select(i => i.FilePath));
            _isClipboardCut = false;
            CanPaste = !string.IsNullOrEmpty(CurrentFolderPath) && _clipboardFilePaths.Count > 0;
        }

        /// <summary>
        /// 選択中の画像をクリップボードに切り取り（移動）として保持します。
        /// </summary>
        public void CutSelectedToClipboard()
        {
            if (SelectedImages.Count == 0) return;
            _clipboardFilePaths.Clear();
            _clipboardFilePaths.AddRange(SelectedImages.Select(i => i.FilePath));
            _isClipboardCut = true;
            CanPaste = !string.IsNullOrEmpty(CurrentFolderPath) && _clipboardFilePaths.Count > 0;
        }

        /// <summary>
        /// 指定されたファイル群を対象ディレクトリへ非同期コピーします（同名ファイルは「 - コピー」を付与して自動リネーム）。
        /// </summary>
        /// <param name="filePaths">コピー元ファイルパス一覧</param>
        /// <param name="destDirectory">コピー先フォルダ</param>
        /// <returns>正常にコピーされた件数</returns>
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

        /// <summary>
        /// 指定されたファイル群を対象ディレクトリへ非同期移動します。
        /// </summary>
        /// <param name="filePaths">移動元ファイルパス一覧</param>
        /// <param name="destDirectory">移動先フォルダ</param>
        /// <returns>正常に移動された件数</returns>
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

        /// <summary>
        /// クリップボードに保持されているファイルを対象フォルダへ貼り付けます。
        /// </summary>
        /// <param name="targetFolder">貼り付け先フォルダ（省略時は現在フォルダ）</param>
        /// <returns>貼り付け処理されたファイル件数</returns>
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

        /// <summary>
        /// 選択中の画像をディスク上から完全に削除します。
        /// </summary>
        /// <returns>削除されたファイル件数</returns>
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

        /// <summary>
        /// ファイル重複時に「- コピー」「- コピー (2)」などの一意のファイル名を生成します。
        /// </summary>
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

        /// <summary>
        /// タイトルとメッセージを直接指定してダイアログ表示イベントを発火します。
        /// </summary>
        public void RaiseDirectMessage(string title, string message)
        {
            ShowDirectMessageRequested?.Invoke(this, (title, message));
        }

        #endregion
    }
}
