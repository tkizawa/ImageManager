using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ImageManager.Services
{
    public class FileSystemService : IFileSystemService
    {
        public string SelectFolder()
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                return dialog.FolderName;
            }
            return null;
        }

        public IEnumerable<string> GetImageFiles(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return Enumerable.Empty<string>();

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                            .Where(s => extensions.Contains(Path.GetExtension(s).ToLowerInvariant()));
        }
    }
}
