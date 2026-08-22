# Microsoft Store リリースノート (What's New) - v1.1.3.0

Microsoft Store (Partner Center) のアプリ更新時に「新機能・更新内容 (What's new)」へコピー＆ペーストしてご利用ください。

---

## 🇯🇵 日本語 (ja-JP)

```text
【バージョン 1.1.3.0 の更新内容】

◆ 新機能・利便性向上
1. ナビゲーションタブの並び替え・順序保持
   ・サイドパネルのタブ（ライブラリ、フォルダ、お気に入り、履歴）をドラッグ＆ドロップで自由に並び替えられるようになりました。
   ・タブの並び順および選択状態は次回起動時にも自動的に復元されます。

2. 診断ログ・トラブルシューティング支援
   ・予期せぬエラーやクラッシュの発生原因を特定するための安全なローカルログ収集機構（AppData\Local\ImageManager\Logs）を導入しました。
   ・操作説明（ヘルプ）画面に「ログフォルダを開く」ボタンを追加し、サポートへの問い合わせを容易にしました。

◆ 不具合修正・安定性向上
1. お気に入り・レーティング情報の保存および同期処理の改善
   ・フォルダ切り替え時およびアプリ再起動時にお気に入り（★）やレーティングが確実に保持されるよう、データベースの同期・照合ロジックを修正しました。

2. データベース自己修復機能（整合性自動検証）の追加
   ・異常終了等によるデータベース破損を起動時に自動検知し、安全に退避・自己修復して正常起動を継続するリカバリ機構を実装しました。
```

---

## 🇺🇸 英語 (en-US)

```text
[What's New in Version 1.1.3.0]

◆ New Features & Usability
1. Reorderable & Persistent Navigation Tabs
   - Side panel tabs (Library, Folder, Favorites, History) can now be freely reordered via drag-and-drop.
   - Tab layout and selected tab state are automatically saved and restored on next startup.

2. Diagnostic Logging & Troubleshooting Support
   - Added a safe local diagnostic logging mechanism (AppData\Local\ImageManager\Logs) to help diagnose issues.
   - Added an "Open Logs Folder" button inside the Help dialog for easy access.

◆ Bug Fixes & Stability
1. Reliable Favorites & Rating Persistence
   - Resolved database synchronization and path matching issues, ensuring favorite (★) stars and ratings remain consistently saved across folder navigation and app restarts.

2. Automatic Database Self-Healing & Integrity Checks
   - Implemented an automatic database integrity check on startup that detects and safely recovers from database corruption.
```
