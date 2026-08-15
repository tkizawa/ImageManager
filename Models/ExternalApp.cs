using System;

namespace ImageManager.Models
{
    public class ExternalApp
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty; // e.g. "{path}"
    }
}
