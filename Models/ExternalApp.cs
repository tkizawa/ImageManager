using System;

namespace ImageManager.Models
{
    /// <summary>
    /// 外部画像編集・閲覧アプリケーションの連携設定を表すモデルクラス。
    /// アプリケーション名、実行ファイルパス、引数テンプレートを保持します。
    /// </summary>
    public class ExternalApp
    {
        /// <summary>外部アプリ設定の一意の識別子（GUID）</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>UI上に表示されるアプリケーション名（例: "Photoshop", "Lightroom"）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>実行可能ファイル（.exe等）の絶対パス</summary>
        public string ExecutablePath { get; set; } = string.Empty;

        /// <summary>
        /// 起動時のコマンドライン引数テンプレート。
        /// "{path}" の文字列は選択中画像のフルパスに置換されます。
        /// </summary>
        public string Arguments { get; set; } = string.Empty; // e.g. "{path}"
    }
}

