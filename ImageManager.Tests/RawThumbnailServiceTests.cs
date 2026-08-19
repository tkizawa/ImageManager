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
            jpegBytes[0] = 0xFF; jpegBytes[1] = 0xD8;
            jpegBytes[2] = 0xFF; jpegBytes[3] = 0xC0; // SOF0
            jpegBytes[4] = 0x00; jpegBytes[5] = 0x0B; jpegBytes[6] = 0x08;
            jpegBytes[7] = 0x01; jpegBytes[8] = 0x00; // H=256
            jpegBytes[9] = 0x01; jpegBytes[10] = 0x00; // W=256
            jpegBytes[11] = 0x03; jpegBytes[12] = 0x01; jpegBytes[13] = 0x11; jpegBytes[14] = 0x00;
            jpegBytes[15] = 0xFF; jpegBytes[16] = 0xDA; // SOS
            for (int i = 17; i < 1998; i++)
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

        [Fact]
        public void GetThumbnailCacheKey_ReturnsValidSha256HashForExistingFile()
        {
            // Arrange
            string tempFile = Path.Combine(_tempDirectory, "cache_key_test.cr3");
            File.WriteAllText(tempFile, "test image data for cache key");

            // Act
            string key1 = RawThumbnailService.GetThumbnailCacheKey(tempFile);
            string key2 = RawThumbnailService.GetThumbnailCacheKey(tempFile);

            // Assert
            Assert.False(string.IsNullOrEmpty(key1));
            Assert.Equal(64, key1.Length); // SHA256 hex string is 64 characters
            Assert.Equal(key1, key2);

            string? cacheFilePath = RawThumbnailService.GetCacheFilePath(tempFile);
            Assert.NotNull(cacheFilePath);
            Assert.EndsWith($"raw_v10_{key1}.jpg", cacheFilePath);

            // 標準画像（.jpg）はディスクキャッシュ対象外のため null が返る
            string jpgFile = Path.Combine(_tempDirectory, "cache_key_test.jpg");
            File.WriteAllText(jpgFile, "test jpg data");
            Assert.Null(RawThumbnailService.GetCacheFilePath(jpgFile));
        }

        [Fact]
        public async Task GetEmbeddedJpegBytesAsync_RawImage_CachesOnDisk()
        {
            // Arrange
            string tempFile = Path.Combine(_tempDirectory, "test_raw.cr2");
            byte[] jpegBytes = new byte[2000];
            jpegBytes[0] = 0xFF; jpegBytes[1] = 0xD8;
            jpegBytes[2] = 0xFF; jpegBytes[3] = 0xC0; // SOF0
            jpegBytes[4] = 0x00; jpegBytes[5] = 0x0B; jpegBytes[6] = 0x08;
            jpegBytes[7] = 0x01; jpegBytes[8] = 0x00; // H=256
            jpegBytes[9] = 0x01; jpegBytes[10] = 0x00; // W=256
            jpegBytes[11] = 0x03; jpegBytes[12] = 0x01; jpegBytes[13] = 0x11; jpegBytes[14] = 0x00;
            jpegBytes[15] = 0xFF; jpegBytes[16] = 0xDA; // SOS
            for (int i = 17; i < 1998; i++)
            {
                jpegBytes[i] = (byte)(i % 256);
            }
            jpegBytes[1998] = 0xFF;
            jpegBytes[1999] = 0xD9;

            byte[] dummyData = new byte[500 + jpegBytes.Length];
            System.Buffer.BlockCopy(jpegBytes, 0, dummyData, 500, jpegBytes.Length);
            File.WriteAllBytes(tempFile, dummyData);

            string? cacheFilePath = RawThumbnailService.GetCacheFilePath(tempFile);
            Assert.NotNull(cacheFilePath);
            if (File.Exists(cacheFilePath)) File.Delete(cacheFilePath);

            // Act
            byte[]? result = await RawThumbnailService.GetEmbeddedJpegBytesAsync(tempFile);

            // Assert
            Assert.NotNull(result);
            Assert.True(File.Exists(cacheFilePath));
        }

        [Fact]
        public async Task DiagnoseRealUserFile()
        {
            string folder = @"E:\KIZAWA\OneDrive - WoodStream Networks\01_写真\2026\2026-08\2026-08-14-江の島\CR3";
            if (!Directory.Exists(folder)) return;

            string[] testFiles = new[] { "IMG_5739.CR3", "IMG_5744.CR3", "IMG_5751.CR3" };

            foreach (var name in testFiles)
            {
                string path = Path.Combine(folder, name);
                if (!File.Exists(path)) continue;

                int ori = RawThumbnailService.GetRawOrientation(path);
                var jpeg = await RawThumbnailService.GetEmbeddedJpegBytesAsync(path);
                Assert.NotNull(jpeg);
                RawThumbnailService.GetJpegDimensions(jpeg, out int w, out int h);
                Console.WriteLine($"[TEST-RESULT] {name}: ori={ori}, dims={w}x{h}");

                if (ori == 6 || ori == 8)
                {
                    Assert.True(h > w, $"{name} should be vertical (h > w) but got {w}x{h}");
                }
                else if (ori == 1)
                {
                    Assert.True(w > h, $"{name} should be horizontal (w > h) but got {w}x{h}");
                }
            }
        }

        [Fact]
        public void ExtractCr3PreviewJpeg_ExtractsValidJpegFromPrvwBox()
        {
            string tempCr3 = Path.Combine(_tempDirectory, "sample.cr3");
            
            // Build dummy CR3 ISOBMFF structure: ftyp box -> moov box (contains PRVW box)
            byte[] ftypBox = new byte[] { 0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'c', (byte)'r', (byte)'x', (byte)' ', 0, 0, 0, 0 };
            
            byte[] dummyJpeg = new byte[500];
            dummyJpeg[0] = 0xFF; dummyJpeg[1] = 0xD8;
            dummyJpeg[2] = 0xFF; dummyJpeg[3] = 0xC0;
            dummyJpeg[4] = 0x00; dummyJpeg[5] = 0x0B; dummyJpeg[6] = 0x08;
            dummyJpeg[7] = 0x01; dummyJpeg[8] = 0x00;
            dummyJpeg[9] = 0x01; dummyJpeg[10] = 0x00;
            dummyJpeg[11] = 0x03; dummyJpeg[12] = 0x01; dummyJpeg[13] = 0x11; dummyJpeg[14] = 0x00;
            dummyJpeg[498] = 0xFF; dummyJpeg[499] = 0xD9;

            int prvwPayloadLen = 8 + dummyJpeg.Length; // 8 bytes header + jpeg
            int prvwTotalLen = 8 + prvwPayloadLen;
            byte[] prvwBox = new byte[prvwTotalLen];
            prvwBox[0] = (byte)((prvwTotalLen >> 24) & 0xFF);
            prvwBox[1] = (byte)((prvwTotalLen >> 16) & 0xFF);
            prvwBox[2] = (byte)((prvwTotalLen >> 8) & 0xFF);
            prvwBox[3] = (byte)(prvwTotalLen & 0xFF);
            prvwBox[4] = (byte)'P'; prvwBox[5] = (byte)'R'; prvwBox[6] = (byte)'V'; prvwBox[7] = (byte)'W';
            Buffer.BlockCopy(dummyJpeg, 0, prvwBox, 16, dummyJpeg.Length);

            int moovTotalLen = 8 + prvwBox.Length;
            byte[] moovBox = new byte[moovTotalLen];
            moovBox[0] = (byte)((moovTotalLen >> 24) & 0xFF);
            moovBox[1] = (byte)((moovTotalLen >> 16) & 0xFF);
            moovBox[2] = (byte)((moovTotalLen >> 8) & 0xFF);
            moovBox[3] = (byte)(moovTotalLen & 0xFF);
            moovBox[4] = (byte)'m'; moovBox[5] = (byte)'o'; moovBox[6] = (byte)'o'; moovBox[7] = (byte)'v';
            Buffer.BlockCopy(prvwBox, 0, moovBox, 8, prvwBox.Length);

            using (var fs = new FileStream(tempCr3, FileMode.Create))
            {
                fs.Write(ftypBox, 0, ftypBox.Length);
                fs.Write(moovBox, 0, moovBox.Length);
            }

            var extracted = RawThumbnailService.ExtractCr3PreviewJpeg(tempCr3);

            Assert.NotNull(extracted);
            Assert.Equal(dummyJpeg.Length, extracted.Length);
            Assert.Equal(0xFF, extracted[0]);
            Assert.Equal(0xD8, extracted[1]);
            Assert.Equal(0xFF, extracted[extracted.Length - 2]);
            Assert.Equal(0xD9, extracted[extracted.Length - 1]);
        }

        [Fact]
        public void ExtractCr3PreviewJpeg_PrefersHighResPrvwOverThmbEvenIfThmbComesFirst()
        {
            string tempCr3 = Path.Combine(_tempDirectory, "sample_thmb_prvw.cr3");

            byte[] ftypBox = new byte[] { 0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'c', (byte)'r', (byte)'x', (byte)' ', 0, 0, 0, 0 };

            // Small THMB (160x120 dummy JPEG with SOF0)
            byte[] thmbJpeg = new byte[300];
            thmbJpeg[0] = 0xFF; thmbJpeg[1] = 0xD8;
            thmbJpeg[2] = 0xFF; thmbJpeg[3] = 0xC0;
            thmbJpeg[4] = 0x00; thmbJpeg[5] = 0x0B; thmbJpeg[6] = 0x08;
            thmbJpeg[7] = 0x00; thmbJpeg[8] = 0x78; // Height = 120
            thmbJpeg[9] = 0x00; thmbJpeg[10] = 0xA0; // Width = 160
            thmbJpeg[11] = 0x03; thmbJpeg[12] = 0x01; thmbJpeg[13] = 0x11; thmbJpeg[14] = 0x00;
            thmbJpeg[15] = 0xFF; thmbJpeg[16] = 0xDA;
            thmbJpeg[17] = 0x00; thmbJpeg[18] = 0x08;
            thmbJpeg[298] = 0xFF; thmbJpeg[299] = 0xD9;

            // Large PRVW (1920x1080 dummy JPEG with SOF0)
            byte[] prvwJpeg = new byte[800];
            prvwJpeg[0] = 0xFF; prvwJpeg[1] = 0xD8;
            prvwJpeg[2] = 0xFF; prvwJpeg[3] = 0xC0;
            prvwJpeg[4] = 0x00; prvwJpeg[5] = 0x0B; prvwJpeg[6] = 0x08;
            prvwJpeg[7] = 0x04; prvwJpeg[8] = 0x38; // Height = 1080
            prvwJpeg[9] = 0x07; prvwJpeg[10] = 0x80; // Width = 1920
            prvwJpeg[11] = 0x03; prvwJpeg[12] = 0x01; prvwJpeg[13] = 0x11; prvwJpeg[14] = 0x00;
            prvwJpeg[15] = 0xFF; prvwJpeg[16] = 0xDA;
            prvwJpeg[17] = 0x00; prvwJpeg[18] = 0x08;
            prvwJpeg[798] = 0xFF; prvwJpeg[799] = 0xD9;

            // THMB box
            int thmbTotalLen = 8 + thmbJpeg.Length;
            byte[] thmbBox = new byte[thmbTotalLen];
            thmbBox[0] = (byte)((thmbTotalLen >> 24) & 0xFF);
            thmbBox[1] = (byte)((thmbTotalLen >> 16) & 0xFF);
            thmbBox[2] = (byte)((thmbTotalLen >> 8) & 0xFF);
            thmbBox[3] = (byte)(thmbTotalLen & 0xFF);
            thmbBox[4] = (byte)'T'; thmbBox[5] = (byte)'H'; thmbBox[6] = (byte)'M'; thmbBox[7] = (byte)'B';
            Buffer.BlockCopy(thmbJpeg, 0, thmbBox, 8, thmbJpeg.Length);

            // PRVW box
            int prvwTotalLen = 8 + prvwJpeg.Length;
            byte[] prvwBox = new byte[prvwTotalLen];
            prvwBox[0] = (byte)((prvwTotalLen >> 24) & 0xFF);
            prvwBox[1] = (byte)((prvwTotalLen >> 16) & 0xFF);
            prvwBox[2] = (byte)((prvwTotalLen >> 8) & 0xFF);
            prvwBox[3] = (byte)(prvwTotalLen & 0xFF);
            prvwBox[4] = (byte)'P'; prvwBox[5] = (byte)'R'; prvwBox[6] = (byte)'V'; prvwBox[7] = (byte)'W';
            Buffer.BlockCopy(prvwJpeg, 0, prvwBox, 8, prvwJpeg.Length);

            // uuid box containing THMB first, then PRVW
            int uuidPayloadLen = 16 + thmbBox.Length + prvwBox.Length; // 16 bytes uuid ID
            int uuidTotalLen = 8 + uuidPayloadLen;
            byte[] uuidBox = new byte[uuidTotalLen];
            uuidBox[0] = (byte)((uuidTotalLen >> 24) & 0xFF);
            uuidBox[1] = (byte)((uuidTotalLen >> 16) & 0xFF);
            uuidBox[2] = (byte)((uuidTotalLen >> 8) & 0xFF);
            uuidBox[3] = (byte)(uuidTotalLen & 0xFF);
            uuidBox[4] = (byte)'u'; uuidBox[5] = (byte)'u'; uuidBox[6] = (byte)'i'; uuidBox[7] = (byte)'d';
            Buffer.BlockCopy(thmbBox, 0, uuidBox, 24, thmbBox.Length);
            Buffer.BlockCopy(prvwBox, 0, uuidBox, 24 + thmbBox.Length, prvwBox.Length);

            using (var fs = new FileStream(tempCr3, FileMode.Create))
            {
                fs.Write(ftypBox, 0, ftypBox.Length);
                fs.Write(uuidBox, 0, uuidBox.Length);
            }

            var extracted = RawThumbnailService.ExtractCr3PreviewJpeg(tempCr3);

            Assert.NotNull(extracted);
            Assert.Equal(prvwJpeg.Length, extracted.Length);
            RawThumbnailService.GetJpegDimensions(extracted, out int w, out int h);
            Assert.Equal(1920, w);
            Assert.Equal(1080, h);
        }
    }
}
