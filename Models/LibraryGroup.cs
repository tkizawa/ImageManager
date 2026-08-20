using System.Collections.Generic;

namespace ImageManager.Models
{
    /// <summary>
    /// 複数のフォルダをまとめて管理する仮想ライブラリグループのモデルクラス。
    /// プロジェクト別やイベント別などに複数のフォルダパスをグループ化して表示できます。
    /// </summary>
    public class LibraryGroup
    {
        /// <summary>ライブラリグループの一意の識別子（GUID）</summary>
        public string Id { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>ライブラリグループの表示名（例: "2026年 旅行写真"）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>このライブラリグループに含まれるフォルダのフルパス一覧</summary>
        public List<string> FolderPaths { get; set; } = new();
    }
}

