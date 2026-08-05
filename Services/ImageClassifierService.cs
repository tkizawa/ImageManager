using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ImageManager.Models;

namespace ImageManager.Services;

public enum ClassificationMode
{
    CopyToCategoryFolder,
    MoveToCategoryFolder,
    TagOnly
}

public class ImageClassifierService
{
    private InferenceSession? _session;
    private bool _isDirectMLActive;
    private readonly string _modelPath;

    public bool IsDirectMLActive => _isDirectMLActive;
    public bool IsModelLoaded => _session != null;

    public ImageClassifierService(string? customModelPath = null)
    {
        _modelPath = customModelPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "models", "classifier.onnx");
        InitializeModel();
    }

    private void InitializeModel()
    {
        if (!File.Exists(_modelPath))
        {
            return;
        }

        try
        {
            var options = new SessionOptions();
            try
            {
                // Attempt DirectML GPU Acceleration
                options.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(_modelPath, options);
                _isDirectMLActive = true;
            }
            catch
            {
                // Fallback to CPU execution provider
                options = new SessionOptions();
                options.AppendExecutionProvider_CPU();
                _session = new InferenceSession(_modelPath, options);
                _isDirectMLActive = false;
            }
        }
        catch
        {
            _session = null;
        }
    }

    public async Task<string> ClassifyImageAsync(ImageFile imageFile)
    {
        if (string.IsNullOrEmpty(imageFile.FilePath) || !File.Exists(imageFile.FilePath))
        {
            return "その他";
        }

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(imageFile.FilePath);
            using var stream = await storageFile.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);

            var transform = new BitmapTransform
            {
                ScaledWidth = 224,
                ScaledHeight = 224,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            byte[] bytes = pixelData.DetachPixelData();

            // Run ONNX Inference if model is loaded
            if (_session != null)
            {
                return RunOnnxInference(bytes);
            }

            // Face detection via Windows.Media.FaceAnalysis if supported
            bool hasFace = await DetectFaceAsync(storageFile);
            if (hasFace)
            {
                return "人物";
            }

            // Heuristic analysis (Document / Landscape / Portrait / Animal / Vehicle / General)
            return RunHeuristicAnalysis(bytes, (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch
        {
            return "その他";
        }
    }

    private async Task<bool> DetectFaceAsync(StorageFile storageFile)
    {
        try
        {
            if (!Windows.Media.FaceAnalysis.FaceDetector.IsSupported)
            {
                return false;
            }

            using var stream = await storageFile.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);

            uint targetWidth = 640;
            uint targetHeight = (uint)Math.Max(1, Math.Round(640.0 * decoder.PixelHeight / decoder.PixelWidth));

            var transform = new BitmapTransform
            {
                ScaledWidth = targetWidth,
                ScaledHeight = targetHeight,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Gray8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var detector = await Windows.Media.FaceAnalysis.FaceDetector.CreateAsync();
            var faces = await detector.DetectFacesAsync(softwareBitmap);
            return faces != null && faces.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private string RunOnnxInference(byte[] bgraBytes)
    {
        if (_session == null) return "その他";

        var inputTensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });

        // ImageNet normalization
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        for (int y = 0; y < 224; y++)
        {
            for (int x = 0; x < 224; x++)
            {
                int i = (y * 224 + x) * 4;
                float b = bgraBytes[i] / 255.0f;
                float g = bgraBytes[i + 1] / 255.0f;
                float r = bgraBytes[i + 2] / 255.0f;

                inputTensor[0, 0, y, x] = (r - mean[0]) / std[0];
                inputTensor[0, 1, y, x] = (g - mean[1]) / std[1];
                inputTensor[0, 2, y, x] = (b - mean[2]) / std[2];
            }
        }

        var inputName = _session.InputNames.FirstOrDefault() ?? "input";
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };

        using var results = _session.Run(inputs);
        var outputTensor = results.First().AsTensor<float>();

        int predictedClass = 0;
        float maxVal = float.MinValue;
        int idx = 0;
        foreach (var val in outputTensor)
        {
            if (val > maxVal)
            {
                maxVal = val;
                predictedClass = idx;
            }
            idx++;
        }

        return MapClassIndexToCategory(predictedClass);
    }

    private string MapClassIndexToCategory(int classIdx)
    {
        return classIdx switch
        {
            >= 0 and <= 397 => "動物",
            >= 398 and <= 500 => "乗り物",
            >= 501 and <= 600 => "建物",
            >= 601 and <= 700 => "食べ物",
            >= 701 and <= 800 => "人物",
            >= 801 and <= 950 => "風景",
            _ => "その他"
        };
    }

    private string RunHeuristicAnalysis(byte[] bgraBytes, int originalWidth, int originalHeight)
    {
        int totalPixels = 224 * 224;
        int whitePixels = 0;
        int darkPixels = 0;
        int greenPixels = 0;
        int bluePixels = 0;
        int skinPixels = 0;
        int centerSkinPixels = 0;
        int centerTotalPixels = 0;

        for (int y = 0; y < 224; y++)
        {
            bool isCenterY = y >= 30 && y <= 194;
            for (int x = 0; x < 224; x++)
            {
                int i = (y * 224 + x) * 4;
                byte b = bgraBytes[i];
                byte g = bgraBytes[i + 1];
                byte r = bgraBytes[i + 2];

                double lum = 0.299 * r + 0.587 * g + 0.114 * b;

                if (lum > 230) whitePixels++;
                if (lum < 30) darkPixels++;

                // Green (nature/plants)
                if (g > r + 15 && g > b + 15) greenPixels++;

                // Blue (sky/water)
                if (b > r + 15 && b > g + 10) bluePixels++;

                // Skin tone estimation using YCbCr + RGB rules
                double cb = -0.168736 * r - 0.331264 * g + 0.5 * b + 128;
                double cr = 0.5 * r - 0.418688 * g - 0.081312 * b + 128;

                bool isSkin = (cb >= 77 && cb <= 127 && cr >= 133 && cr <= 173) ||
                              (r > 45 && g > 25 && b > 15 && r > g && (r - g) >= 6 && (r - b) >= 8 && Math.Abs(g - b) <= 50);

                if (isSkin)
                {
                    skinPixels++;
                    if (isCenterY && x >= 30 && x <= 194)
                    {
                        centerSkinPixels++;
                    }
                }

                if (isCenterY && x >= 30 && x <= 194)
                {
                    centerTotalPixels++;
                }
            }
        }

        double whiteRatio = (double)whitePixels / totalPixels;
        double darkRatio = (double)darkPixels / totalPixels;
        double greenRatio = (double)greenPixels / totalPixels;
        double blueRatio = (double)bluePixels / totalPixels;
        double skinRatio = (double)skinPixels / totalPixels;
        double centerSkinRatio = centerTotalPixels > 0 ? (double)centerSkinPixels / centerTotalPixels : 0;

        // 1. High white background + contrast -> Document / Screenshot
        if (whiteRatio > 0.45 && darkRatio > 0.08) return "文書";

        // 2. People check (skin ratio in center or overall skin ratio)
        // Even in outdoor settings (greenery background), if there is a person (skin ratio >= 1.8% or center skin ratio >= 2.2%), classify as "人物"
        if (skinRatio >= 0.018 || centerSkinRatio >= 0.022) return "人物";

        // 3. Landscape check
        if (greenRatio + blueRatio > 0.22) return "風景";

        // 4. Fallback for subjects with subtle skin tones (neck/hands/ear profile)
        if (skinRatio >= 0.008 && greenRatio < 0.15) return "人物";

        return "その他";
    }

    public async Task ProcessClassificationAsync(
        IEnumerable<ImageFile> images,
        string targetDirectory,
        ClassificationMode mode,
        IProgress<(int current, int total, string currentFile, string category)> progress,
        CancellationToken cancellationToken)
    {
        var imageList = images.ToList();
        int total = imageList.Count;
        int current = 0;

        foreach (var img in imageList)
        {
            if (cancellationToken.IsCancellationRequested) break;

            current++;
            string category = await ClassifyImageAsync(img);
            img.Category = category;

            if (mode != ClassificationMode.TagOnly && !string.IsNullOrEmpty(targetDirectory) && Directory.Exists(targetDirectory))
            {
                string categoryDir = Path.Combine(targetDirectory, $"分類_{category}");
                if (!Directory.Exists(categoryDir))
                {
                    Directory.CreateDirectory(categoryDir);
                }

                string destPath = Path.Combine(categoryDir, img.FileName);

                if (mode == ClassificationMode.CopyToCategoryFolder)
                {
                    File.Copy(img.FilePath, destPath, overwrite: true);
                }
                else if (mode == ClassificationMode.MoveToCategoryFolder)
                {
                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(img.FilePath, destPath);
                    img.FilePath = destPath;
                }
            }

            progress.Report((current, total, img.FileName, category));
        }
    }
}
