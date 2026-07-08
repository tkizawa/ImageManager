using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageManager.ViewModels
{
    public partial class DirectoryNodeViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DirectoryNodeViewModel> _children = new();

        private bool _isExpanded;
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

        public DirectoryNodeViewModel(string fullPath)
        {
            FullPath = fullPath;
            Name = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(Name))
            {
                Name = fullPath; // for drives like C:\
            }

            // Dummy child to show expansion toggle (+)
            Children.Add(new DirectoryNodeViewModel { Name = "Loading..." });
        }

        // Private constructor for dummy node
        private DirectoryNodeViewModel() { }

        private void LoadChildren()
        {
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
                            Children.Add(new DirectoryNodeViewModel(dir));
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Access denied
            }
            catch (Exception)
            {
                // Other I/O errors
            }
        }
    }
}
