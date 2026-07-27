using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageManager.Services
{
    public interface IFileSystemService
    {
        Task<string> SelectFolderAsync();
        Task<string?> SaveFilePickerAsync(string defaultFileName, string fileTypeDescription, string fileExtension);
        Task<string?> OpenFilePickerAsync(string fileTypeDescription, string fileExtension);
        IEnumerable<string> GetImageFiles(string folderPath);
    }
}
