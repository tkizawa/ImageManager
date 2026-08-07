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

public class OllamaService
{
    private readonly HttpClient _httpClient;
    private string _endpoint;

    public string Endpoint
    {
        get => _endpoint;
        set => _endpoint = value.TrimEnd('/');
    }

    public OllamaService(string endpoint = "http://localhost:11434", HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _endpoint = endpoint.TrimEnd('/');
    }

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

    public async Task<string?> ClassifyImageAsync(string imagePath, string modelName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;

        try
        {
            string base64Image = await PrepareBase64ImageAsync(imagePath);
            if (string.IsNullOrEmpty(base64Image)) return null;

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
            // Fallback gracefully on exception
        }

        return null;
    }

    private async Task<string> PrepareBase64ImageAsync(string imagePath)
    {
        try
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(imagePath);
            if (fileBytes.Length <= 500 * 1024) // If under 500KB, use directly
            {
                return Convert.ToBase64String(fileBytes);
            }

            // Otherwise, resize image to max 512x512 JPEG using WinRT BitmapDecoder/Encoder
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
            // Fallback to reading file directly
            byte[] rawBytes = await File.ReadAllBytesAsync(imagePath);
            return Convert.ToBase64String(rawBytes);
        }
    }

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
