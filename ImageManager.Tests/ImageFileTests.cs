using System;
using System.IO;
using System.Threading.Tasks;
using ImageManager.Models;
using Xunit;

namespace ImageManager.Tests
{
    public class ImageFileTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _tempFilePath;

        public ImageFileTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ImageFileTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _tempFilePath = Path.Combine(_tempDirectory, "sample.jpg");
            File.WriteAllBytes(_tempFilePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // Dummy JPG header
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                try
                {
                    Directory.Delete(_tempDirectory, true);
                }
                catch { }
            }
        }

        [Fact]
        public void ImageFile_Constructor_InitializesFileProperties()
        {
            // Act
            var imageFile = new ImageFile(_tempFilePath);

            // Assert
            Assert.Equal(_tempFilePath, imageFile.FilePath);
            Assert.Equal("sample.jpg", imageFile.FileName);
            Assert.Equal(4, imageFile.FileSize);
            Assert.False(imageFile.IsExifLoaded);
        }

        [Fact]
        public async Task LoadExifAsync_SetsIsExifLoadedTrue()
        {
            // Arrange
            var imageFile = new ImageFile(_tempFilePath);

            // Act
            await imageFile.LoadExifAsync();

            // Assert
            Assert.True(imageFile.IsExifLoaded);
        }

        [Fact]
        public void PropertyChanges_NotifyCorrectly()
        {
            // Arrange
            var imageFile = new ImageFile(_tempFilePath);
            bool categoryChanged = false;

            imageFile.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(ImageFile.Category))
                {
                    categoryChanged = true;
                }
            };

            // Act
            imageFile.Category = "Landscape";

            // Assert
            Assert.True(categoryChanged);
            Assert.Equal("Landscape", imageFile.Category);
        }

        [Fact]
        public void FormattedExposureSpecs_FormatsParametersCorrectly()
        {
            // Arrange
            var imageFile = new ImageFile(_tempFilePath)
            {
                FNumber = "2.8",
                ExposureTime = "1/1000",
                IsoSpeed = "100",
                FocalLength = "50 mm"
            };

            // Act & Assert
            Assert.Equal("f/2.8  |  1/1000s  |  ISO 100  |  50 mm", imageFile.FormattedExposureSpecs);
        }

        [Fact]
        public void FormattedFileSize_FormatsBytesCorrectly()
        {
            // Arrange
            var imageFile = new ImageFile(_tempFilePath);

            // Act & Assert
            Assert.Equal("4 B", imageFile.FormattedFileSize);
        }

        [Fact]
        public void Rating_ClampsBetween0And5_AndUpdatesFormattedRating()
        {
            // Arrange
            var imageFile = new ImageFile(_tempFilePath);

            // Default
            Assert.Equal(0, imageFile.Rating);
            Assert.False(imageFile.HasRating);
            Assert.Equal("なし", imageFile.FormattedRating);

            // Set Rating 3
            imageFile.Rating = 3;
            Assert.Equal(3, imageFile.Rating);
            Assert.True(imageFile.HasRating);
            Assert.Equal("★★★☆☆ (3)", imageFile.FormattedRating);

            // Clamp check
            imageFile.Rating = 5;
            Assert.Equal(5, imageFile.Rating);
            Assert.Equal("★★★★★ (5)", imageFile.FormattedRating);
        }
    }
}
