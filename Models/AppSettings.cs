namespace ImageManager.Models
{
    /// <summary>
    /// アプリケーション全体の設定情報を保持するモデルクラス。
    /// ウィンドウの位置・サイズ、表示カラム幅、フィルタ・ソート状態、外部連携アプリ、キャッシュ設定などを管理します。
    /// </summary>
    public class AppSettings
    {
        /// <summary>前回開いていたフォルダのフルパス</summary>
        public string LastOpenedFolder { get; set; } = string.Empty;

        /// <summary>メインウィンドウの幅（ピクセル）</summary>
        public double WindowWidth { get; set; } = 900;

        /// <summary>メインウィンドウの高さ（ピクセル）</summary>
        public double WindowHeight { get; set; } = 600;

        /// <summary>メインウィンドウの左座標（スクリーン座標系、未設定時は NaN）</summary>
        public double WindowLeft { get; set; } = double.NaN;

        /// <summary>メインウィンドウの上座標（スクリーン座標系、未設定時は NaN）</summary>
        public double WindowTop { get; set; } = double.NaN;

        /// <summary>メインウィンドウの表示状態（0: 通常, 1: 最小化, 2: 最大化）</summary>
        public int WindowState { get; set; } = 0; // 0: Normal, 1: Minimized, 2: Maximized

        /// <summary>フォルダツリー列のグリッド幅</summary>
        public string TreeColumnWidth { get; set; } = "1*";

        /// <summary>サムネイルグリッド列のグリッド幅</summary>
        public string ThumbnailsColumnWidth { get; set; } = "2*";

        /// <summary>プレビューパネル列のグリッド幅</summary>
        public string PreviewColumnWidth { get; set; } = "1*";

        /// <summary>画像プレビューウィンドウの幅</summary>
        public double ImageWindowWidth { get; set; } = 1024;

        /// <summary>画像プレビューウィンドウの高さ</summary>
        public double ImageWindowHeight { get; set; } = 768;

        /// <summary>画像プレビューウィンドウの左座標（スクリーン座標系）</summary>
        public double ImageWindowLeft { get; set; } = double.NaN;

        /// <summary>画像プレビューウィンドウの上座標（スクリーン座標系）</summary>
        public double ImageWindowTop { get; set; } = double.NaN;

        /// <summary>画像プレビューウィンドウで情報オーバーレイを表示するかどうか</summary>
        public bool ShowImageWindowInfo { get; set; } = true;

        /// <summary>お気に入りフォルダのパス一覧</summary>
        public System.Collections.Generic.List<string> FavoriteFolders { get; set; } = new();

        /// <summary>最近開いたフォルダの履歴一覧</summary>
        public System.Collections.Generic.List<string> HistoryFolders { get; set; } = new();

        /// <summary>カスタムライブラリグループの一覧</summary>
        public System.Collections.Generic.List<LibraryGroup> Libraries { get; set; } = new();

        /// <summary>サムネイルグリッドのアイコン表示サイズ（スライダー値）</summary>
        public double ThumbnailSize { get; set; } = 100;

        /// <summary>ソート対象項目のインデックス（0: ファイル名, 1: 撮影日, 2: ファイルサイズ 等）</summary>
        public int SortFieldIndex { get; set; } = 0;

        /// <summary>ソート順のインデックス（0: 昇順, 1: 降順）</summary>
        public int SortDirectionIndex { get; set; } = 1;

        /// <summary>レーティング絞り込みフィルタのインデックス</summary>
        public int RatingFilterIndex { get; set; } = 0;

        /// <summary>前回選択されていた画像のフルパス</summary>
        public string SelectedImageFilePath { get; set; } = string.Empty;

        /// <summary>AI分類機能にOllama（ローカルLLM/VLM）を使用するかどうか</summary>
        public bool UseOllamaForClassification { get; set; } = false;

        /// <summary>OllamaサーバーのエンドポイントURL</summary>
        public string OllamaEndpoint { get; set; } = "http://localhost:11434";

        /// <summary>Ollamaで使用するモデル名（例: llava, llama3.2-vision）</summary>
        public string OllamaModelName { get; set; } = "llava";

        /// <summary>登録済みの外部連携アプリケーション一覧</summary>
        public System.Collections.Generic.List<ExternalApp> ExternalApps { get; set; } = new();

        /// <summary>アプリ終了時に自動でサムネイルキャッシュのクリーンアップを実行するかどうか</summary>
        public bool AutoCleanCacheOnExit { get; set; } = false;

        /// <summary>キャッシュクリーンアップ対象となる経過日数（日）</summary>
        public int CacheCleanPeriodDays { get; set; } = 30;

        /// <summary>キャッシュクリーンアップ時の上限サイズ（バイト単位）</summary>
        public long CacheCleanMaxSizeBytes { get; set; } = 1073741824; // 1 GB
    }
}

