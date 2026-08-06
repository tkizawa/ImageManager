using ImageManager.Models;

namespace ImageManager.Services
{
    public interface ISettingsService
    {
        AppSettings Load();
        void Save(AppSettings settings);
        bool ExportSettings(string filePath, AppSettings settings);
        AppSettings? ImportSettings(string filePath);
    }
}
