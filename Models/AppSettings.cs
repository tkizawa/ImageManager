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
    }
}
