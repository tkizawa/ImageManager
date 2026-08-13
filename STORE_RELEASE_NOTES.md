# Microsoft Store リリースノート (What's New) - v1.0.10.0

Microsoft Store (Partner Center) のアプリ更新時に「新機能・更新内容 (What's new)」へコピー＆ペーストしてご利用ください。

---

## 🇯🇵 日本語 (ja-JP)

```text
【バージョン 1.0.10.0 の更新内容】

1. 大量RAW画像（4,000枚超）表示および並べ替えの安定化・パフォーマンス向上
   ・数千枚のCanon CR3等のRAW画像が存在するフォルダにおいて、並べ替え（日付順・更新日時順）実行時のメモリ急上昇やクラッシュ問題を根本解決しました。
   ・Windows WICエラーダイアログの発生を完全に抑止し、快適でスムーズな動作を実現しました。

2. ビューアー表示の右クリック表示切替＆オーバーレイ情報追加
   ・大画面ビューアーの左上にファイル名、撮影日、EXIF情報（カメラ、レンズ、F値、SS、ISO等）のオーバーレイを表示しました。
   ・右クリックメニューから情報の表示・非表示を切り替え可能にし、切り替え状態を保存できるようにしました。

3. C:ドライブ等のボリュームラベル表示名の修正・改善
   ・Windows標準のC:ドライブなど、ボリュームラベルが明示的に設定されていないドライブにおいて、Windows Shell API (SHGetFileInfo) 連携を導入し、エクスプローラーと同等の「ローカル ディスク (C:)」等の正確な「ボリュームラベル（ドライブ文字）」表示名を取得・表示するように改善しました。
```

---

## 🇺🇸 英語 (en-US)

```text
[What's New in Version 1.0.10.0]

1. Massive RAW Folder Performance & Sorting Stability
   - Resolved memory spikes and crash issues when sorting large folders containing thousands of camera RAW files (such as Canon CR3).
   - Fixed Windows WIC system error dialogs for smooth, fast navigation across thousands of images.

2. Viewer Information Overlay & Toggle Persistence
   - Added a top-left overlay panel in the image viewer displaying photo details (filename, date taken, camera, lens, exposure specs, and file size).
   - Added context menu right-click toggle for overlay visibility with persistent settings.

3. Fixed Drive List Label Display (e.g., C: Drive)
   - Integrated Windows Shell API (SHGetFileInfo) to resolve missing volume labels for unlabeled drives (like default C: drive), accurately displaying Windows Explorer names such as "Local Disk (C:)".
```
