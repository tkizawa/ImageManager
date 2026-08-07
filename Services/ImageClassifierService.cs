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

    public OllamaService Ollama { get; } = new OllamaService();
    public bool UseOllama { get; set; } = false;
    public string OllamaModelName { get; set; } = "llava";

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

    public async Task<string> ClassifyImageAsync(ImageFile imageFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(imageFile.FilePath) || !File.Exists(imageFile.FilePath))
        {
            return "その他";
        }

        // Priority 1: Ollama Vision AI (if enabled)
        if (UseOllama && !string.IsNullOrWhiteSpace(OllamaModelName))
        {
            try
            {
                string? ollamaCategory = await Ollama.ClassifyImageAsync(imageFile.FilePath, OllamaModelName, cancellationToken);
                if (!string.IsNullOrEmpty(ollamaCategory))
                {
                    return ollamaCategory;
                }
            }
            catch
            {
                // Fallback to local ONNX / Heuristics on failure
            }
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

            // Priority 2: ONNX Inference if model is loaded
            if (_session != null)
            {
                return RunOnnxInference(bytes);
            }

            // Priority 3: Face detection via Windows.Media.FaceAnalysis if supported
            bool hasFace = await DetectFaceAsync(storageFile);
            if (hasFace)
            {
                return "人物";
            }

            // Priority 4: Heuristic analysis (Document / Landscape / Portrait / Animal / Vehicle / General)
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

        int foodWarmPixels = 0;
        int centerFoodWarmPixels = 0;
        int buildingGrayPixels = 0;

        for (int y = 0; y < 224; y++)
        {
            bool isCenterY = y >= 45 && y <= 178;
            for (int x = 0; x < 224; x++)
            {
                int i = (y * 224 + x) * 4;
                byte b = bgraBytes[i];
                byte g = bgraBytes[i + 1];
                byte r = bgraBytes[i + 2];

                double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                bool isCenterX = x >= 45 && x <= 178;
                bool isCenter = isCenterY && isCenterX;

                if (lum > 230) whitePixels++;
                if (lum < 35) darkPixels++;

                // Green (nature/plants/trees)
                if (g > r + 10 && g > b + 10 && g > 40) greenPixels++;

                // Blue (sky/water/sea)
                if (b > r + 15 && b > g + 5 && b > 50) bluePixels++;

                // Strict Skin tone estimation using YCbCr + RGB combined rules
                // YCbCr: Cb 77..127, Cr 133..173
                // RGB: R > 95, G > 40, B > 20, R > G, R > B, R-G >= 15, |G-B| <= 40
                double cb = -0.168736 * r - 0.331264 * g + 0.5 * b + 128;
                double cr = 0.5 * r - 0.418688 * g - 0.081312 * b + 128;

                bool isSkin = (cb >= 77 && cb <= 127 && cr >= 133 && cr <= 173) &&
                              (r > 95 && g > 40 && b > 20 && r > g && r > b && (r - g) >= 15 && Math.Abs(g - b) <= 40);

                if (isSkin)
                {
                    skinPixels++;
                    if (isCenter) centerSkinPixels++;
                }

                // Food warm colors (vibrant red/orange/yellow/brown in center)
                // Non-skin high saturation warm tones
                int maxComponent = Math.Max(r, Math.Max(g, b));
                int minComponent = Math.Min(r, Math.Min(g, b));
                double saturation = maxComponent > 0 ? (double)(maxComponent - minComponent) / maxComponent : 0;

                if (r > 100 && r > b + 20 && saturation > 0.25 && !isSkin)
                {
                    foodWarmPixels++;
                    if (isCenter) centerFoodWarmPixels++;
                }

                // Building/Architecture neutral gray & structured tones
                if (Math.Abs(r - g) < 15 && Math.Abs(g - b) < 15 && lum >= 40 && lum <= 200)
                {
                    buildingGrayPixels++;
                }

                if (isCenter)
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
        double foodWarmRatio = (double)foodWarmPixels / totalPixels;
        double centerFoodWarmRatio = centerTotalPixels > 0 ? (double)centerFoodWarmPixels / centerTotalPixels : 0;
        double buildingGrayRatio = (double)buildingGrayPixels / totalPixels;

        // 1. High white background + dark text/lines -> Document / Screenshot ("文書")
        if (whiteRatio > 0.45 && darkRatio > 0.05) return "文書";

        // 2. Landscape check (Forest, Sky, Ocean, Mountains) ("風景")
        if (greenRatio + blueRatio > 0.18 || greenRatio > 0.10 || blueRatio > 0.15) return "風景";

        // 3. People check (Requires significant skin ratio in center or overall) ("人物")
        // Note: DetectFaceAsync runs before this and handles face detection.
        if (skinRatio >= 0.08 || centerSkinRatio >= 0.10) return "人物";

        // 4. Food check (Vibrant warm colors in center) ("食べ物")
        if (centerFoodWarmRatio > 0.15 || foodWarmRatio > 0.20) return "食べ物";

        // 5. Building check (Neutral architectural gray/concrete/stone tones) ("建物")
        if (buildingGrayRatio > 0.40) return "建物";

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
            string category = await ClassifyImageAsync(img, cancellationToken);
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
