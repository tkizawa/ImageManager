using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageManager.Services
{
    public interface IFileSystemService
    {
        Task<string> SelectFolderAsync();
        IEnumerable<string> GetImageFiles(string folderPath);
    }
}
