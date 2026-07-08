using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageManager.Models
{
    public partial class ImageFile : ObservableObject
    {
        [ObservableProperty]
        private string _filePath;

        [ObservableProperty]
        private string _fileName;

        [ObservableProperty]
        private long _fileSize;

        [ObservableProperty]
        private DateTime _creationTime;

        [ObservableProperty]
        private int _imageWidth;

        [ObservableProperty]
        private int _imageHeight;

        public ImageFile(string path)
        {
            FilePath = path;
            FileName = Path.GetFileName(path);
            
            var info = new FileInfo(path);
            if (info.Exists)
            {
                FileSize = info.Length;
                CreationTime = info.CreationTime;
            }
        }
    }
}
