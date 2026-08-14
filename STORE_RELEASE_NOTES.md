# Microsoft Store リリースノート (What's New) - v1.1.0.0

Microsoft Store (Partner Center) のアプリ更新時に「新機能・更新内容 (What's new)」へコピー＆ペーストしてご利用ください。

---

## 🇯🇵 日本語 (ja-JP)

```text
【バージョン 1.1.0.0 の更新内容】

1. SQLite データベース管理およびライブラリの自動メタデータ同期機能の導入
   ・画像のメタデータ（EXIF情報、AI分類カテゴリー、評価、お気に入り設定等）を高速なローカルSQLiteデータベース（imagemanager.db）で一元管理するようにメジャーアップデートを行いました。

2. ライブラリフォルダ移動・名前変更への柔軟な追従および再選択機能
   ・ライブラリに登録されたフォルダが見つからない（名前変更・移動された）際、再選択ダイアログを表示して即座に新フォルダへ接続できるように修正しました。
   ・ライブラリツリーの右クリックメニューに「フォルダの場所を変更...」を追加し、いつでもメタデータを維持したまま新しい参照パスへ再紐付け可能です。

3. 画像ビュアーの操作性向上およびツールチップ表示の改善
   ・大画面画像ビュアー背景での不要なツールチップ表示を抑制し、「右クリックで画像情報の表示・非表示の切り替え」ガイドツールチップを表示するように改善しました。

4. 写真の「お気に入り」機能の追加
   ・サムネイル右上の星ボタン、右クリックメニュー「お気に入りの切り替え」、または拡大ビュアーでの「F」キーからワンクリックでお気に入り登録・解除ができるようになりました。
   ・ツールバーの「お気に入りのみ」ボタンで、お気に入りに指定した写真のみを瞬時に抽出表示できます。

5. 写真の「レート（★1～5）」およびレートフィルター機能の追加
   ・写真に 5 段階の星レート（★1～★5）および「レートなし (0)」を設定・管理できるようになりました。右ペインの星ボタン、右クリックメニュー、キーボードの「0」～「5」キー（テンキー対応）から素早く設定できます。
   ・ツールバーにレートフィルター用ドロップダウンを追加し、指定レートの写真のみを絞り込み表示できます。「お気に入りのみ」ボタンとの併用時には両方の条件を満たす写真のみを抽出（AND条件）可能です。
   ・並び替えに「レート順（昇順・降順）」を追加しました。
```

---

## 🇺🇸 英語 (en-US)

```text
[What's New in Version 1.1.0.0]

1. SQLite Database Integration & Automatic Metadata Sync
   - Major update adding a fast, local SQLite database (imagemanager.db) to manage image metadata, EXIF properties, AI classifications, ratings, and favorites.

2. Flexible Library Folder Relocation & Re-selection Dialog
   - Added missing folder detection with an interactive re-selection dialog when a library folder is renamed or moved.
   - Added 'Change Folder Location...' to the library tree context menu to re-bind folders while preserving existing metadata.

3. Image Viewer UX & Tooltip Enhancements
   - Suppressed auto-generated background tooltips and added helpful guidance tooltips for toggling photo information overlay.

4. Photo Favorites Feature
   - Easily bookmark or unbookmark photos via the top-right star icon on thumbnails, context menu, or by pressing 'F' in the image viewer.
   - Use the 'Favorites Only' toolbar toggle to quickly view only your bookmarked photos.

5. 5-Star Rating System & Rating Filter
   - Rate photos with 1 to 5 stars (★1 to ★5) or clear ratings from the properties pane, context menu, or keyboard shortcuts ('0' to '5' keys / numpad).
   - Filter photos by star rating from the toolbar dropdown, supporting seamless combination with Favorites (AND condition).
   - Added 'Sort by Rating' (Ascending / Descending).
```
