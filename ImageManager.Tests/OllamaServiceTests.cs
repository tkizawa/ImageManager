using ImageManager.Services;
using Xunit;

namespace ImageManager.Tests;

public class OllamaServiceTests
{
    [Theory]
    [InlineData("人物", "人物")]
    [InlineData("この写真は人物です。", "人物")]
    [InlineData("Person in photo", "人物")]
    [InlineData("風景", "風景")]
    [InlineData("美しい風景写真", "風景")]
    [InlineData("Landscape view", "風景")]
    [InlineData("建物", "建物")]
    [InlineData("Building structure", "建物")]
    [InlineData("食べ物", "食べ物")]
    [InlineData("Food and meal", "食べ物")]
    [InlineData("動物", "動物")]
    [InlineData("Cat / Pet", "動物")]
    [InlineData("乗り物", "乗り物")]
    [InlineData("Vehicle / Car", "乗り物")]
    [InlineData("文書", "文書")]
    [InlineData("Document text", "文書")]
    [InlineData("不明なオブジェクト", "その他")]
    public void MapResponseToCategory_MapsTextCorrectly(string inputResponse, string expectedCategory)
    {
        string category = OllamaService.MapResponseToCategory(inputResponse);
        Assert.Equal(expectedCategory, category);
    }
}
