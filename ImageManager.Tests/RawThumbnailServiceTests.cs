using System;
using System.IO;
using ImageManager.Services;
using Xunit;

namespace ImageManager.Tests
{
    public class RawThumbnailServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public RawThumbnailServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "RawThumbnailServiceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
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

        [Theory]
        [InlineData("sample.CR2", true)]
        [InlineData("sample.cr3", true)]
        [InlineData("sample.NEF", true)]
        [InlineData("sample.arw", true)]
        [InlineData("sample.dng", true)]
        [InlineData("sample.orf", true)]
        [InlineData("sample.rw2", true)]
        [InlineData("sample.jpg", false)]
        [InlineData("sample.png", false)]
        [InlineData("sample.txt", false)]
        public void IsRawFile_CorrectlyIdentifiesRawExtensions(string fileName, bool expectedResult)
        {
            // Act
            bool result = RawThumbnailService.IsRawFile(fileName);

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [InlineData("photo.jpg", true)]
        [InlineData("photo.PNG", true)]
        [InlineData("photo.CR2", true)]
        [InlineData("photo.dng", true)]
        [InlineData("document.pdf", false)]
        public void IsSupportedImage_IdentifiesStandardAndRawImages(string fileName, bool expectedResult)
        {
            // Act
            bool result = RawThumbnailService.IsSupportedImage(fileName);

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public void ExtractEmbeddedJpegByBinaryScan_ExtractsValidJpegSegment()
        {
            // Arrange
            string tempRawPath = Path.Combine(_tempDirectory, "test.cr2");
            byte[] headerPadding = new byte[500]; // 500 bytes TIFF header dummy
            byte[] jpegBytes = new byte[2000];
            jpegBytes[0] = 0xFF;
            jpegBytes[1] = 0xD8;
            jpegBytes[2] = 0xFF;
            jpegBytes[3] = 0xE0;
            // Write payload pattern
            for (int i = 4; i < 1998; i++)
            {
                jpegBytes[i] = (byte)(i % 256);
            }
            jpegBytes[1998] = 0xFF;
            jpegBytes[1999] = 0xD9;

            byte[] tailPadding = new byte[300];

            using (var fs = new FileStream(tempRawPath, FileMode.Create))
            {
                fs.Write(headerPadding, 0, headerPadding.Length);
                fs.Write(jpegBytes, 0, jpegBytes.Length);
                fs.Write(tailPadding, 0, tailPadding.Length);
            }

            // Act
            byte[]? extracted = RawThumbnailService.ExtractEmbeddedJpegByBinaryScan(tempRawPath);

            // Assert
            Assert.NotNull(extracted);
            Assert.Equal(jpegBytes.Length, extracted.Length);
            Assert.Equal(0xFF, extracted[0]);
            Assert.Equal(0xD8, extracted[1]);
            Assert.Equal(0xFF, extracted[2]);
            Assert.Equal(0xFF, extracted[extracted.Length - 2]);
            Assert.Equal(0xD9, extracted[extracted.Length - 1]);
        }
    }
}
