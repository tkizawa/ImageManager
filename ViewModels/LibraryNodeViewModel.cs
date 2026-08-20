using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageManager.ViewModels
{
    /// <summary>
    /// カスタムライブラリグループおよびそれに含まれるフォルダツリーのノードを表現するViewModelクラス。
    /// ライブラリノード（ルート）と所属フォルダノードの両方を階層管理します。
    /// </summary>
    public partial class LibraryNodeViewModel : ObservableObject
    {
        /// <summary>ノードの一意識別子（GUID）</summary>
        [ObservableProperty]
        private string _id = string.Empty;

        /// <summary>ノードの表示名（ライブラリ名またはフォルダ名）</summary>
        [ObservableProperty]
        private string _name = string.Empty;

        /// <summary>フォルダの絶対パス（ライブラリルートの場合は空文字列）</summary>
        [ObservableProperty]
        private string _fullPath = string.Empty;

        /// <summary>ライブラリ自体（ルート）か、所属するフォルダかを表すフラグ</summary>
        [ObservableProperty]
        private bool _isLibrary = false;

        private bool _isExpanded;

        /// <summary>
        /// ツリーノードが展開されているかどうかを取得または設定します。
        /// フォルダノード展開時にサブディレクトリを遅延読み込みします。
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value)
                {
                    LoadSubDirectories();
                }
            }
        }

        /// <summary>UI表示用のアイコンUnicodeグリフ（ライブラリ: \uE8F1, フォルダ: \uE8B7）</summary>
        public string IconGlyph => IsLibrary ? "\uE8F1" : "\uE8B7";

        /// <summary>親ライブラリノードへの参照</summary>
        public LibraryNodeViewModel? ParentLibrary { get; set; }

        /// <summary>ライブラリ直下に登録されたトップレベルフォルダであるかどうか</summary>
        public bool IsTopLevelFolder => !IsLibrary && ParentLibrary != null && ParentLibrary.IsLibrary;

        /// <summary>子ノード（サブフォルダ）のコレクション</summary>
        public ObservableCollection<LibraryNodeViewModel> Children { get; } = new();

        /// <summary>
        /// <see cref="LibraryNodeViewModel"/> クラスの新しいインスタンスを初期化します。
        /// </summary>
        public LibraryNodeViewModel()
        {
        }

        /// <summary>
        /// サブディレクトリが存在する場合に展開アイコン（+）を表示させるためのダミー子ノードを追加します。
        /// </summary>
        public void CheckAndAddDummyChild()
        {
            if (IsLibrary) return;
            if (string.IsNullOrEmpty(FullPath) || !Directory.Exists(FullPath)) return;

            try
            {
                var subDirs = Directory.GetDirectories(FullPath);
                if (subDirs.Length > 0)
                {
                    Children.Clear();
                    Children.Add(new LibraryNodeViewModel { Name = "Loading..." });
                }
            }
            catch { }
        }

        /// <summary>
        /// フォルダ配下のサブディレクトリを遅延読み込みします。
        /// </summary>
        public void LoadSubDirectories()
        {
            if (IsLibrary) return;
            if (string.IsNullOrEmpty(FullPath) || !Directory.Exists(FullPath)) return;

            // 既に読み込み済みの場合はスキップ
            if (Children.Count > 0 && Children[0].Name != "Loading...")
                return;

            Children.Clear();

            try
            {
                var dirs = Directory.GetDirectories(FullPath);
                foreach (var dir in dirs)
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        if (!info.Attributes.HasFlag(FileAttributes.Hidden) &&
                            !info.Attributes.HasFlag(FileAttributes.System))
                        {
                            var folderName = info.Name;
                            var subFolderNode = new LibraryNodeViewModel
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = folderName,
                                FullPath = dir,
                                IsLibrary = false,
                                ParentLibrary = this.ParentLibrary
                            };
                            subFolderNode.CheckAndAddDummyChild();
                            Children.Add(subFolderNode);
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }
        }
    }
}
