using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

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

        [ObservableProperty]
        private string _dateTaken = string.Empty;

        [ObservableProperty]
        private string _cameraModel = string.Empty;

        [ObservableProperty]
        private string _lens = string.Empty;

        [ObservableProperty]
        private string _exposureTime = string.Empty;

        [ObservableProperty]
        private string _fNumber = string.Empty;

        [ObservableProperty]
        private string _isoSpeed = string.Empty;

        [ObservableProperty]
        private string _focalLength = string.Empty;

        public bool IsExifLoaded { get; private set; }

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

        public async Task LoadExifAsync()
        {
            if (IsExifLoaded) return;
            IsExifLoaded = true;

            string dateTaken = string.Empty, cameraModel = string.Empty, lens = string.Empty, exposureTime = string.Empty, fNumber = string.Empty, isoSpeed = string.Empty, focalLength = string.Empty;

            await Task.Run(() =>
            {
                try
                {
                    var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(FilePath);
                    var exifIfd0 = directories.OfType<MetadataExtractor.Formats.Exif.ExifIfd0Directory>().FirstOrDefault();
                    var exifSubIfd = directories.OfType<MetadataExtractor.Formats.Exif.ExifSubIfdDirectory>().FirstOrDefault();

                    if (exifIfd0 != null)
                    {
                        cameraModel = exifIfd0.GetDescription(MetadataExtractor.Formats.Exif.ExifIfd0Directory.TagModel) ?? string.Empty;
                    }

                    if (exifSubIfd != null)
                    {
                        dateTaken = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagDateTimeOriginal) ?? string.Empty;
                        lens = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagLensModel) ?? string.Empty;
                        exposureTime = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagExposureTime) ?? string.Empty;
                        fNumber = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagFNumber) ?? string.Empty;
                        isoSpeed = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagIsoEquivalent) ?? string.Empty;
                        focalLength = exifSubIfd.GetDescription(MetadataExtractor.Formats.Exif.ExifSubIfdDirectory.TagFocalLength) ?? string.Empty;
                    }
                }
                catch { }
            });

            DateTaken = dateTaken;
            CameraModel = cameraModel;
            Lens = lens;
            ExposureTime = exposureTime;
            FNumber = fNumber;
            IsoSpeed = isoSpeed;
            FocalLength = focalLength;
        }
    }
}
