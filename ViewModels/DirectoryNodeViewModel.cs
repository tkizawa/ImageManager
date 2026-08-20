using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageManager.ViewModels
{
    /// <summary>
    /// ディレクトリツリー（TreeView）の各フォルダノードを表現するViewModelクラス。
    /// 遅延読み込み（Lazy Loading / 展開時に初めてサブフォルダを探索）に対応しています。
    /// </summary>
    public partial class DirectoryNodeViewModel : ObservableObject
    {
        /// <summary>ディレクトリの絶対パス</summary>
        [ObservableProperty]
        private string _fullPath = string.Empty;

        /// <summary>ツリー表示名（フォルダ名またはドライブレター）</summary>
        [ObservableProperty]
        private string _name = string.Empty;

        /// <summary>現在選択中かどうか</summary>
        [ObservableProperty]
        private bool _isSelected;

        /// <summary>子ディレクトリノードのコレクション</summary>
        [ObservableProperty]
        private ObservableCollection<DirectoryNodeViewModel> _children = new();

        private bool _isExpanded;

        /// <summary>
        /// ツリーノードが展開されているかどうかを取得または設定します。
        /// 展開時に子フォルダの遅延読み込み（LoadChildren）をトリガーします。
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value) && value)
                {
                    LoadChildren();
                }
            }
        }

        /// <summary>
        /// <see cref="DirectoryNodeViewModel"/> クラスの新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="fullPath">ディレクトリの絶対パス</param>
        /// <param name="displayName">カスタム表示名（省略時はフォルダ名）</param>
        public DirectoryNodeViewModel(string fullPath, string? displayName = null)
        {
            FullPath = fullPath;
            if (!string.IsNullOrEmpty(displayName))
            {
                Name = displayName;
            }
            else
            {
                Name = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(Name))
                {
                    Name = fullPath; // ドライブ直下（C:\ 等）の場合
                }
            }

            // ツリーの展開アイコン（+マーク）を表示させるためのダミー子ノードを追加
            Children.Add(new DirectoryNodeViewModel { Name = "Loading..." });
        }

        /// <summary>ダミーノード用プライベートコンストラクタ</summary>
        private DirectoryNodeViewModel() { }

        /// <summary>
        /// サブディレクトリを探索し、子ノードとして読み込みます。
        /// 隠し属性・システム属性のフォルダおよびアクセス権のないフォルダは安全に除外します。
        /// </summary>
        private void LoadChildren()
        {
            // 既に読み込み済み（ダミーノードが解消されている）の場合はスキップ
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
                            Children.Add(new DirectoryNodeViewModel(dir));
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // アクセス拒否時は安全に無視
            }
            catch (Exception)
            {
                // その他I/Oエラー
            }
        }
    }
}
