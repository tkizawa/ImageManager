using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ImageManager.Services;

/// <summary>
/// ローカルLLM/VLM（Ollama）のAPIと通信し、モデルの取得や画像内容の自動分類（Vision AI）を行うサービスクラス。
/// 画像のリサイズ・Base64変換や、レスポンスからカテゴリへのマッピング処理を提供します。
/// </summary>
public class OllamaService
{
    private readonly HttpClient _httpClient;
    private string _endpoint;

    /// <summary>
    /// Ollama サーバーのエンドポイントURL（末尾のスラッシュは自動トリムされます）。
    /// </summary>
    public string Endpoint
    {
        get => _endpoint;
        set => _endpoint = value.TrimEnd('/');
    }

    /// <summary>
    /// <see cref="OllamaService"/> クラスの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="endpoint">Ollama サーバーのURL（デフォルト: http://localhost:11434）</param>
    /// <param name="httpClient">カスタム HttpClient（テスト用）</param>
    public OllamaService(string endpoint = "http://localhost:11434", HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _endpoint = endpoint.TrimEnd('/');
    }

    /// <summary>
    /// Ollama サーバーが稼働中かつ通信可能であるかを確認します。
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>接続可能な場合は true、接続不能な場合は false</returns>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_endpoint}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ollama サーバーにインストール済みのモデル一覧（例: "llava:latest", "llama3.2-vision"）を取得します。
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>モデル名のリスト</returns>
    public async Task<List<string>> GetInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_endpoint}/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var modelList = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var modelsElement) && modelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modelsElement.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameProp))
                    {
                        string name = nameProp.GetString() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            modelList.Add(name);
                        }
                    }
                }
            }
            return modelList;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 指定された画像ファイルを Vision モデルに送信し、日本語のカテゴリ（人物、風景、建物、食べ物など）を判定・取得します。
    /// </summary>
    /// <param name="imagePath">画像ファイルの絶対パス</param>
    /// <param name="modelName">使用するVisionモデル名（例: "llava"）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>判定されたカテゴリ文字列（"人物", "風景" 等）。失敗時は null。</returns>
    public async Task<string?> ClassifyImageAsync(string imagePath, string modelName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;

        try
        {
            // 画像を適切なサイズにリサイズしてBase64文字列に変換
            string base64Image = await PrepareBase64ImageAsync(imagePath);
            if (string.IsNullOrEmpty(base64Image)) return null;

            // カテゴリ判定用のプロンプト
            string prompt = "Classify this image into exactly one category from this list: [人物, 風景, 建物, 食べ物, 動物, 乗り物, 文書, その他]. Respond ONLY with the category name in Japanese.";

            var requestBody = new
            {
                model = modelName,
                prompt = prompt,
                images = new[] { base64Image },
                stream = false
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Ollama /api/generate エンドポイントへPOSTリクエスト
            var response = await _httpClient.PostAsync($"{_endpoint}/api/generate", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonResponse);

            if (doc.RootElement.TryGetProperty("response", out var responseProp))
            {
                string rawText = responseProp.GetString() ?? "";
                return MapResponseToCategory(rawText);
            }
        }
        catch
        {
            // 通信エラーやタイムアウト時は安全に null を返す
        }

        return null;
    }

    /// <summary>
    /// 画像をBase64文字列に変換します。
    /// 転送効率と推論速度を向上させるため、500KBを超える画像は最大512x512ピクセルのJPEGに縮小します。
    /// </summary>
    /// <param name="imagePath">画像ファイルのパス</param>
    /// <returns>Base64エンコードされた画像データ文字列</returns>
    private async Task<string> PrepareBase64ImageAsync(string imagePath)
    {
        try
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(imagePath);
            if (fileBytes.Length <= 500 * 1024) // 500KB以下の場合はそのまま使用
            {
                return Convert.ToBase64String(fileBytes);
            }

            // WinRT BitmapDecoder / Encoder を用いて最大 512x512 にリサイズ
            using var inputStream = new InMemoryRandomAccessStream();
            await inputStream.WriteAsync(fileBytes.AsBuffer());
            inputStream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(inputStream);
            uint origW = decoder.PixelWidth;
            uint origH = decoder.PixelHeight;

            uint maxDim = 512;
            double scale = Math.Min(1.0, (double)maxDim / Math.Max(origW, origH));
            uint newW = (uint)Math.Max(1, Math.Round(origW * scale));
            uint newH = (uint)Math.Max(1, Math.Round(origH * scale));

            var transform = new BitmapTransform
            {
                ScaledWidth = newW,
                ScaledHeight = newH,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            using var outputStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                newW,
                newH,
                96,
                96,
                pixelData.DetachPixelData());

            await encoder.FlushAsync();
            outputStream.Seek(0);

            byte[] resizedBytes = new byte[outputStream.Size];
            await outputStream.ReadAsync(resizedBytes.AsBuffer(), (uint)outputStream.Size, InputStreamOptions.None);

            return Convert.ToBase64String(resizedBytes);
        }
        catch
        {
            // リサイズ失敗時は元ファイルを直接Base64化してフォールバック
            byte[] rawBytes = await File.ReadAllBytesAsync(imagePath);
            return Convert.ToBase64String(rawBytes);
        }
    }

    /// <summary>
    /// LLMから返却された自由記述テキストから、定義済みの標準カテゴリ名に正規化・マッピングします。
    /// </summary>
    /// <param name="rawText">LLMの応答テキスト</param>
    /// <returns>標準化されたカテゴリ文字列（"人物", "風景", "建物", "食べ物", "動物", "乗り物", "文書", "その他"）</returns>
    public static string MapResponseToCategory(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "その他";

        string text = rawText.Trim();

        if (text.Contains("人物") || text.Contains("人") || text.Contains("Person") || text.Contains("People") || text.Contains("Face"))
            return "人物";
        if (text.Contains("風景") || text.Contains("自然") || text.Contains("空") || text.Contains("海") || text.Contains("山") || text.Contains("Landscape") || text.Contains("Nature"))
            return "風景";
        if (text.Contains("建物") || text.Contains("建築") || text.Contains("ビル") || text.Contains("家") || text.Contains("Building") || text.Contains("House"))
            return "建物";
        if (text.Contains("食べ物") || text.Contains("料理") || text.Contains("食品") || text.Contains("食事") || text.Contains("Food") || text.Contains("Meal"))
            return "食べ物";
        if (text.Contains("動物") || text.Contains("ペット") || text.Contains("犬") || text.Contains("猫") || text.Contains("鳥") || text.Contains("Animal") || text.Contains("Pet"))
            return "動物";
        if (text.Contains("乗り物") || text.Contains("車") || text.Contains("電車") || text.Contains("飛行機") || text.Contains("Vehicle") || text.Contains("Car"))
            return "乗り物";
        if (text.Contains("文書") || text.Contains("テキスト") || text.Contains("書類") || text.Contains("文字") || text.Contains("Document") || text.Contains("Text"))
            return "文書";

        return "その他";
    }
}
