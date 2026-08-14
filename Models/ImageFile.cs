using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ImageManager.Services;

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
        private DateTime _lastWriteTime;

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

        [ObservableProperty]
        private string _category = string.Empty;

        [ObservableProperty]
        private bool _isFavorite;

        partial void OnIsFavoriteChanged(bool value)
        {
            if (!string.IsNullOrEmpty(FilePath))
            {
                try
                {
                    DatabaseService.Instance.UpdateImageFavorite(FilePath, value);
                }
                catch { }
            }
        }

        [ObservableProperty]
        private int _rating; // 0: None, 1..5: ★1..★5

        partial void OnRatingChanged(int value)
        {
            if (value < 0 || value > 5)
            {
                int clamped = Math.Clamp(value, 0, 5);
                if (_rating != clamped)
                {
                    _rating = clamped;
                }
            }
            OnPropertyChanged(nameof(FormattedRating));
            OnPropertyChanged(nameof(HasRating));

            if (!string.IsNullOrEmpty(FilePath))
            {
                try
                {
                    DatabaseService.Instance.UpdateImageRating(FilePath, _rating);
                }
                catch { }
            }
        }

        public bool HasRating => Rating > 0;

        public string FormattedRating
        {
            get
            {
                if (Rating <= 0) return "なし";
                return new string('★', Math.Min(5, Rating)) + new string('☆', Math.Max(0, 5 - Rating)) + $" ({Rating})";
            }
        }

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
                LastWriteTime = info.LastWriteTime;
            }
        }

        public async Task LoadExifAsync()
        {
            if (IsExifLoaded) return;
            IsExifLoaded = true;

            string dateTaken = string.Empty, cameraModel = string.Empty, lens = string.Empty, exposureTime = string.Empty, fNumber = string.Empty, isoSpeed = string.Empty, focalLength = string.Empty;
            int metaRating = 0;

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (!File.Exists(FilePath)) return;

                        var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(FilePath);
                        var exifIfd0 = directories.OfType<MetadataExtractor.Formats.Exif.ExifIfd0Directory>().FirstOrDefault();
                        var exifSubIfd = directories.OfType<MetadataExtractor.Formats.Exif.ExifSubIfdDirectory>().FirstOrDefault();

                        if (exifIfd0 != null)
                        {
                            cameraModel = exifIfd0.GetDescription(MetadataExtractor.Formats.Exif.ExifIfd0Directory.TagModel) ?? string.Empty;
                            if (exifIfd0.ContainsTag(18246))
                            {
                                var ratingStr = exifIfd0.GetDescription(18246);
                                if (int.TryParse(ratingStr, out int r) && r >= 1 && r <= 5)
                                {
                                    metaRating = r;
                                }
                            }
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
            }
            catch { }

            DateTaken = dateTaken;
            CameraModel = cameraModel;
            Lens = lens;
            ExposureTime = exposureTime;
            FNumber = fNumber;
            IsoSpeed = isoSpeed;
            FocalLength = focalLength;

            if (Rating == 0 && metaRating > 0)
            {
                Rating = metaRating;
            }

            OnPropertyChanged(nameof(FormattedDateTaken));
            OnPropertyChanged(nameof(FormattedExposureSpecs));
            OnPropertyChanged(nameof(FormattedFileSize));

            try
            {
                DatabaseService.Instance.UpdateExifRecord(this);
            }
            catch { }
        }

        public string FormattedDateTaken
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DateTaken))
                    return DateTaken;
                return LastWriteTime != default ? LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss") : string.Empty;
            }
        }

        public string FormattedExposureSpecs
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(FNumber))
                {
                    var f = FNumber.StartsWith("f/", StringComparison.OrdinalIgnoreCase) ? FNumber : $"f/{FNumber}";
                    parts.Add(f);
                }
                if (!string.IsNullOrWhiteSpace(ExposureTime))
                {
                    var exp = ExposureTime.EndsWith("s", StringComparison.OrdinalIgnoreCase) || ExposureTime.EndsWith("sec", StringComparison.OrdinalIgnoreCase) ? ExposureTime : $"{ExposureTime}s";
                    parts.Add(exp);
                }
                if (!string.IsNullOrWhiteSpace(IsoSpeed))
                {
                    var iso = IsoSpeed.StartsWith("ISO", StringComparison.OrdinalIgnoreCase) ? IsoSpeed : $"ISO {IsoSpeed}";
                    parts.Add(iso);
                }
                if (!string.IsNullOrWhiteSpace(FocalLength))
                {
                    parts.Add(FocalLength);
                }
                return string.Join("  |  ", parts);
            }
        }

        public string FormattedFileSize
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024.0):F2} MB";
            }
        }
    }
}
