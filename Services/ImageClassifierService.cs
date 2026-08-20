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

/// <summary>
/// 画像分類実行時のファイル操作モード。
/// </summary>
public enum ClassificationMode
{
    /// <summary>分類別フォルダ（分類_人物、分類_風景 等）へコピー</summary>
    CopyToCategoryFolder,

    /// <summary>分類別フォルダへ移動</summary>
    MoveToCategoryFolder,

    /// <summary>ファイル操作は行わず、タグ（メタデータ）の付与のみ行う</summary>
    TagOnly
}

/// <summary>
/// 画像コンテンツの自動分類を行うサービスクラス。
/// 以下の優先順位で分類パイプラインを実行します：
/// 1. Ollama Vision AI（ローカルVLM、有効時）
/// 2. ローカル ONNX モデル（DirectML GPU加速またはCPU）
/// 3. Windows.Media.FaceAnalysis（人物・顔検出）
/// 4. YCbCr / RGB 色空間ヒューリスティック解析（文書・風景・人物・料理・建物など）
/// </summary>
public class ImageClassifierService
{
    private InferenceSession? _session;
    private bool _isDirectMLActive;
    private readonly string _modelPath;

    /// <summary>DirectMLによるGPUアクセラレーションが有効かどうかを取得します。</summary>
    public bool IsDirectMLActive => _isDirectMLActive;

    /// <summary>ONNX推論モデルが読み込まれているかどうかを取得します。</summary>
    public bool IsModelLoaded => _session != null;

    /// <summary>Ollama 連携サービスインスタンスを取得します。</summary>
    public OllamaService Ollama { get; } = new OllamaService();

    /// <summary>Ollama を最優先の分類器として使用するかどうか</summary>
    public bool UseOllama { get; set; } = false;

    /// <summary>Ollama で使用する Vision モデル名</summary>
    public string OllamaModelName { get; set; } = "llava";

    /// <summary>
    /// <see cref="ImageClassifierService"/> クラスの新しいインスタンスを初期化します。
    /// ONNXモデルファイルが存在すれば読み込みとDirectMLプロバイダの初期化を行います。
    /// </summary>
    /// <param name="customModelPath">カスタムONNXモデルパス（省略時は Assets/models/classifier.onnx）</param>
    public ImageClassifierService(string? customModelPath = null)
    {
        _modelPath = customModelPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "models", "classifier.onnx");
        InitializeModel();
    }

    /// <summary>
    /// ONNX Runtime の推論セッションを初期化します。
    /// DirectML (GPU) を優先し、利用できない環境では CPU に安全にフォールバックします。
    /// </summary>
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
                // DirectML による GPU ハードウェアアクセラレーションを試行
                options.AppendExecutionProvider_DML(0);
                _session = new InferenceSession(_modelPath, options);
                _isDirectMLActive = true;
            }
            catch
            {
                // DirectML 利用不可の場合は CPU 実行プロバイダへフォールバック
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

    /// <summary>
    /// 単一の画像ファイルを解析し、分類カテゴリ文字列（"人物", "風景", "建物", "食べ物", "動物", "乗り物", "文書", "その他"）を返します。
    /// </summary>
    /// <param name="imageFile">対象の画像ファイルモデル</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>分類カテゴリ名</returns>
    public async Task<string> ClassifyImageAsync(ImageFile imageFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(imageFile.FilePath) || !File.Exists(imageFile.FilePath))
        {
            return "その他";
        }

        // 優先度 1: Ollama Vision AI（ユーザー設定で有効化されている場合）
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
                // Ollama 接続失敗時はローカルONNX/ヒューリスティックに自動フォールバック
            }
        }

        try
        {
            // 画像ピクセルデータをデコード（224x224に縮小して解析効率を向上）
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

            // 優先度 2: ONNX モデル推論（モデルが存在する場合）
            if (_session != null)
            {
                return RunOnnxInference(bytes);
            }

            // 優先度 3: Windows.Media.FaceAnalysis による顔検出
            bool hasFace = await DetectFaceAsync(storageFile);
            if (hasFace)
            {
                return "人物";
            }

            // 優先度 4: 色空間・輝度・空間分布によるヒューリスティック解析
            return RunHeuristicAnalysis(bytes, (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch
        {
            return "その他";
        }
    }

    /// <summary>
    /// Windows.Media.FaceAnalysis API を使用して画像内に人物の顔が存在するか検出します。
    /// </summary>
    /// <param name="storageFile">画像ファイルオブジェクト</param>
    /// <returns>顔が検出された場合は true</returns>
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

            // 顔検出用に幅640pxにアスペクト比を維持して縮小
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

    /// <summary>
    /// ONNX モデルに対してテンソル正規化（ImageNet mean/std）を行って推論を実行します。
    /// </summary>
    /// <param name="bgraBytes">224x224 BGRA 形式のピクセルデータ</param>
    /// <returns>推定されたカテゴリ文字列</returns>
    private string RunOnnxInference(byte[] bgraBytes)
    {
        if (_session == null) return "その他";

        var inputTensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });

        // ImageNet 標準正規化パラメータ
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        // BGRA から RGB テンソルへの変換および正規化
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

        // 最大確率を持つクラスインデックスを探索
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

    /// <summary>
    /// ImageNet 1000クラスインデックスをアプリの主要カテゴリにマッピングします。
    /// </summary>
    /// <param name="classIdx">ImageNetクラス番号</param>
    /// <returns>カテゴリ名</returns>
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

    /// <summary>
    /// 色相、彩度、輝度分布、中央領域の肌色/暖色比率からカテゴリを推定するヒューリスティックアルゴリズム。
    /// </summary>
    /// <param name="bgraBytes">224x224 BGRA ピクセルデータ</param>
    /// <param name="originalWidth">元画像の幅</param>
    /// <param name="originalHeight">元画像の高さ</param>
    /// <returns>判定されたカテゴリ名</returns>
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

                // 輝度（ITU-R BT.601）
                double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                bool isCenterX = x >= 45 && x <= 178;
                bool isCenter = isCenterY && isCenterX;

                if (lum > 230) whitePixels++;
                if (lum < 35) darkPixels++;

                // 緑系統（自然・植物・森林）
                if (g > r + 10 && g > b + 10 && g > 40) greenPixels++;

                // 青系統（空・海・水面）
                if (b > r + 15 && b > g + 5 && b > 50) bluePixels++;

                // YCbCr + RGB 複合条件による厳格な肌色判定
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

                // 料理・食品の暖色系（中央部の鮮やかな赤・橙・茶色など）
                int maxComponent = Math.Max(r, Math.Max(g, b));
                int minComponent = Math.Min(r, Math.Min(g, b));
                double saturation = maxComponent > 0 ? (double)(maxComponent - minComponent) / maxComponent : 0;

                if (r > 100 && r > b + 20 && saturation > 0.25 && !isSkin)
                {
                    foodWarmPixels++;
                    if (isCenter) centerFoodWarmPixels++;
                }

                // 建造物・人工構造物の無彩色・ニュートラルグレー
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

        // 1. 白背景かつ黒文字・黒線が多い -> 文書・スクリーンショット ("文書")
        if (whiteRatio > 0.45 && darkRatio > 0.05) return "文書";

        // 2. 空・海・森林の比率が高い -> 風景写真 ("風景")
        if (greenRatio + blueRatio > 0.18 || greenRatio > 0.10 || blueRatio > 0.15) return "風景";

        // 3. 肌色の分布割合が高い -> 人物写真 ("人物")
        if (skinRatio >= 0.08 || centerSkinRatio >= 0.10) return "人物";

        // 4. 中央部に暖色系の高彩度領域が存在 -> 料理 ("食べ物")
        if (centerFoodWarmRatio > 0.15 || foodWarmRatio > 0.20) return "食べ物";

        // 5. グレー・コンクリート等の構造比率が高い -> 建物 ("建物")
        if (buildingGrayRatio > 0.40) return "建物";

        return "その他";
    }

    /// <summary>
    /// 複数画像の一括分類処理を実行し、指定されたモード（コピー、移動、タグ付与）に応じてファイルを整理します。
    /// </summary>
    /// <param name="images">分類対象の画像コレクション</param>
    /// <param name="targetDirectory">整理先のベースディレクトリ</param>
    /// <param name="mode">分類モード（コピー/移動/タグのみ）</param>
    /// <param name="progress">進捗通知ハンドラ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
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
            // カテゴリ判定
            string category = await ClassifyImageAsync(img, cancellationToken);
            img.Category = category;

            // ファイル操作（コピーまたは移動）
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

            // 進捗を呼び出し元へ通知
            progress.Report((current, total, img.FileName, category));
        }
    }
}
