using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageManager.ViewModels
{
    public partial class LibraryNodeViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private bool _isLibrary = false;

        private bool _isExpanded;
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

        public string IconGlyph => IsLibrary ? "\uE8F1" : "\uE8B7";

        public LibraryNodeViewModel? ParentLibrary { get; set; }

        public bool IsTopLevelFolder => !IsLibrary && ParentLibrary != null && ParentLibrary.IsLibrary;

        public ObservableCollection<LibraryNodeViewModel> Children { get; } = new();

        public LibraryNodeViewModel()
        {
        }

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

        public void LoadSubDirectories()
        {
            if (IsLibrary) return;
            if (string.IsNullOrEmpty(FullPath) || !Directory.Exists(FullPath)) return;

            // If already loaded (dummy node removed), return
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
