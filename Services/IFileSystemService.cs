using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageManager.Services
{
    /// <summary>
    /// ファイルシステムおよびピッカーダイアログ操作のインターフェース。
    /// フォルダ選択、ファイル保存・読み込みダイアログ、対応画像ファイルの列挙を提供します。
    /// </summary>
    public interface IFileSystemService
    {
        /// <summary>
        /// ユーザーにフォルダ選択ピッカーダイアログを表示し、選択されたフォルダパスを取得します。
        /// </summary>
        /// <returns>選択されたフォルダパス。キャンセルの場合は空文字列。</returns>
        Task<string> SelectFolderAsync();

        /// <summary>
        /// ファイル保存ピッカーダイアログを表示し、保存先ファイルパスを取得します。
        /// </summary>
        /// <param name="defaultFileName">初期ファイル名</param>
        /// <param name="fileTypeDescription">ファイル種類の表示名（例: "JSON 設定ファイル"）</param>
        /// <param name="fileExtension">拡張子（例: ".json" または "json"）</param>
        /// <returns>選択された保存先ファイルパス。キャンセルの場合は null。</returns>
        Task<string?> SaveFilePickerAsync(string defaultFileName, string fileTypeDescription, string fileExtension);

        /// <summary>
        /// ファイルオープンピッカーダイアログを表示し、選択されたファイルパスを取得します。
        /// </summary>
        /// <param name="fileTypeDescription">ファイル種類の表示名</param>
        /// <param name="fileExtension">拡張子</param>
        /// <returns>選択されたファイルパス。キャンセルの場合は null。</returns>
        Task<string?> OpenFilePickerAsync(string fileTypeDescription, string fileExtension);

        /// <summary>
        /// 指定されたフォルダ直下から、サポートされている画像ファイル（RAW・通常画像）のパス一覧を列挙します。
        /// </summary>
        /// <param name="folderPath">検索対象のフォルダパス</param>
        /// <returns>画像ファイルのフルパスのコレクション</returns>
        IEnumerable<string> GetImageFiles(string folderPath);
    }
}
