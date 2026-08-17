# Microsoft Store リリースノート (What's New) - v1.1.1.0

Microsoft Store (Partner Center) のアプリ更新時に「新機能・更新内容 (What's new)」へコピー＆ペーストしてご利用ください。

---

## 🇯🇵 日本語 (ja-JP)

```text
【バージョン 1.1.1.0 の更新内容】

◆ 新機能・機能改善
1. 写真のレート機能（★1～5）およびレートフィルターの追加
   ・写真に 5 段階の星レート（★1～★5）または「レートなし (0)」を設定・管理できるようになりました。
   ・プロパティ欄、右クリックメニュー、キーボードショートカット（「0」～「5」キー / テンキー）から素早く設定できます。
   ・ツールバーに指定レート抽出用のフィルターを追加し、「お気に入りのみ」との組み合わせ絞り込み（AND条件）やレート順並び替えに対応しました。

2. サムネイルキャッシュ機能およびキャッシュ管理ダイアログの追加
   ・RAW画像に加え一般画像（JPEG / PNG / WebP / BMP / GIF 等）のローカルディスクキャッシュ生成・再利用に対応し、大量の写真一覧表示を大幅に高速化しました。
   ・「ツール」メニューにキャッシュ管理ダイアログを追加し、現在のキャッシュ容量・ファイル数の確認およびワンクリックでの一括削除が可能になりました。
   ・アプリ終了時の自動キャッシュクリーンアップ（保持期間：7/14/30/60日、容量上限：1GB/5GB/10GB指定）を設定できるようになりました。

3. 設定のエクスポート・インポートにデータベース（SQLite）バックアップを追加
   ・ZIPアーカイブ形式（*.zip）によるアプリ設定とメタデータデータベース（imagemanager.db）の一括バックアップ・復元に対応しました。
   ・従来のJSON単体ファイル（*.json）のエクスポート・インポート互換性も保持しています。

4. 全画面画像ビュアーでのお気に入り切り替え対応
   ・拡大ビュアー表示中に「F」キー押下または右クリックメニューから、閲覧中画像のお気に入り状態を即座に切り替えられるようになりました。

5. RAW画像（CR3 / ORF / NEF / ARW）の表示画質向上と自動回転
   ・RAW画像のサムネイルおよびプレビュー表示の高画質化・安定化を行いました。
   ・RAW画像のExif Orientation（回転向き）メタデータを解析し、正しい向きへ自動回転して表示するよう改善しました。

◆ 不具合修正・安定性向上
1. お気に入り・メタデータが再起動後に失われる不具合の修正
   ・アプリ再起動時にお気に入りフラグ等のメタデータが反映されない問題を修正しました（フォルダ識別子のSHA-256決定論的生成およびパス照合強化）。
   ・フォルダ再スキャン時に既存のメタデータ（お気に入り・レート等）が意図せず初期化・上書きされるのを防ぐ保護処理を強化しました。

2. サムネイル表示でRAW画像の一部が表示されない不具合の修正
   ・一部のRAW画像フォーマット（CR3 / ORF / NEF / ARW 等）においてサムネイルが正常に読み込めない・表示されない問題を修正しました。
```

---

## 🇺🇸 英語 (en-US)

```text
[What's New in Version 1.1.1.0]

◆ New Features & Enhancements
1. 5-Star Photo Rating System & Rating Filter
   - Added support for rating photos from 1 to 5 stars (★1 to ★5) or clearing ratings (0).
   - Easily set ratings via the properties pane, thumbnail context menu, or keyboard shortcuts ('0' to '5' keys / numpad).
   - Added toolbar rating filter dropdown supporting combined filtering with Favorites (AND condition) and sorting by rating (Ascending / Descending).

2. Thumbnail Disk Caching & Cache Management Dialog
   - Added high-speed local disk thumbnail caching for all standard image formats (JPEG, PNG, WebP, BMP, GIF) in addition to RAW files, significantly accelerating grid rendering.
   - Added a Cache Management dialog to view current cache size and file count, with one-click bulk cache purging.
   - Added automatic cache cleanup on application exit (configurable retention periods: 7/14/30/60 days, size limits: 1GB/5GB/10GB).

3. SQLite Database Backup with Settings Export/Import
   - Added full backup and restore support for app settings and the SQLite metadata database (imagemanager.db) using ZIP archive format (*.zip).
   - Maintained backward compatibility for importing and exporting legacy JSON configuration files (*.json).

4. Toggle Favorites in Fullscreen Image Viewer
   - Easily toggle favorite status while viewing images in fullscreen mode using the 'F' key or right-click context menu.

5. High-Quality RAW Previews & Automatic Exif Orientation Rotation
   - Improved thumbnail and preview rendering quality and stability for RAW formats (CR3, ORF, NEF, ARW).
   - Added automatic rotation based on Exif Orientation metadata for RAW photos.

◆ Bug Fixes & Stability
1. Fixed Metadata & Favorites Persistence Issue Across Restarts
   - Resolved an issue where favorite flags were not properly restored after restarting the application (improved deterministic SHA-256 folder ID generation and path matching).
   - Enhanced metadata protection to prevent existing ratings and favorites from being overwritten during folder re-scans.

2. Fixed RAW Image Thumbnail Display Issue
   - Fixed an issue where thumbnails for certain RAW image formats (CR3, ORF, NEF, ARW) failed to load or display properly in the thumbnail grid.
```
