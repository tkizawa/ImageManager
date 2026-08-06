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
    }
}
