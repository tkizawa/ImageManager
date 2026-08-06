using System.Collections.Generic;

namespace ImageManager.Models
{
    public class LibraryGroup
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public List<string> FolderPaths { get; set; } = new();
    }
}
