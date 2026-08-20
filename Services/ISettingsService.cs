using ImageManager.Models;

namespace ImageManager.Services
{
    /// <summary>
    /// アプリケーション設定の読み込み、保存、エクスポート、インポートを提供するサービスのインターフェース。
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// 設定ファイルから設定情報を読み込みます。
        /// ファイルが存在しない、または読み込み失敗時はデフォルト値を返します。
        /// </summary>
        /// <returns><see cref="AppSettings"/> オブジェクト</returns>
        AppSettings Load();

        /// <summary>
        /// 現在の設定情報を設定ファイルへ保存します。
        /// </summary>
        /// <param name="settings">保存する設定情報オブジェクト</param>
        void Save(AppSettings settings);

        /// <summary>
        /// 設定情報を指定ファイルパス（JSONまたはZIPアーカイブ）にエクスポートします。
        /// ZIP形式の場合はデータベース（imagemanager.db）も含めてバックアップします。
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <param name="settings">エクスポートする設定情報</param>
        /// <returns>成功した場合は true、失敗した場合は false</returns>
        bool ExportSettings(string filePath, AppSettings settings);

        /// <summary>
        /// 指定ファイルパス（JSONまたはZIPアーカイブ）から設定情報をインポートします。
        /// ZIP形式の場合はデータベースの復元も同時に行います。
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <returns>読み込まれた <see cref="AppSettings"/>。失敗した場合は null。</returns>
        AppSettings? ImportSettings(string filePath);
    }
}
