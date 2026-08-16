namespace ImageManager.Models
{
    public class AppSettings
    {
        public string LastOpenedFolder { get; set; } = string.Empty;
        public double WindowWidth { get; set; } = 900;
        public double WindowHeight { get; set; } = 600;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public int WindowState { get; set; } = 0; // 0: Normal, 1: Minimized, 2: Maximized
        public string TreeColumnWidth { get; set; } = "1*";
        public string ThumbnailsColumnWidth { get; set; } = "2*";
        public string PreviewColumnWidth { get; set; } = "1*";

        public double ImageWindowWidth { get; set; } = 1024;
        public double ImageWindowHeight { get; set; } = 768;
        public double ImageWindowLeft { get; set; } = double.NaN;
        public double ImageWindowTop { get; set; } = double.NaN;
        public bool ShowImageWindowInfo { get; set; } = true;

        public System.Collections.Generic.List<string> FavoriteFolders { get; set; } = new();
        public System.Collections.Generic.List<string> HistoryFolders { get; set; } = new();
        public System.Collections.Generic.List<LibraryGroup> Libraries { get; set; } = new();

        public double ThumbnailSize { get; set; } = 100;
        public int SortFieldIndex { get; set; } = 0;
        public int SortDirectionIndex { get; set; } = 1;
        public int RatingFilterIndex { get; set; } = 0;
        public string SelectedImageFilePath { get; set; } = string.Empty;

        public bool UseOllamaForClassification { get; set; } = false;
        public string OllamaEndpoint { get; set; } = "http://localhost:11434";
        public string OllamaModelName { get; set; } = "llava";

        public System.Collections.Generic.List<ExternalApp> ExternalApps { get; set; } = new();

        public bool AutoCleanCacheOnExit { get; set; } = false;
        public int CacheCleanPeriodDays { get; set; } = 30;
        public long CacheCleanMaxSizeBytes { get; set; } = 1073741824; // 1 GB
    }
}
