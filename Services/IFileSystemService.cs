using System.Collections.Generic;

namespace ImageManager.Services
{
    public interface IFileSystemService
    {
        string SelectFolder();
        IEnumerable<string> GetImageFiles(string folderPath);
    }
}
