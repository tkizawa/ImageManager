# Microsoft Store リリースノート (What's New) - v1.1.2.0

Microsoft Store (Partner Center) のアプリ更新時に「新機能・更新内容 (What's new)」へコピー＆ペーストしてご利用ください。

---

## 🇯🇵 日本語 (ja-JP)

```text
【バージョン 1.1.2.0 の更新内容】

◆ 不具合修正・安定性向上
1. JPEG画像でサムネイルおよびプレビューが歪んで表示される不具合の修正
   ・BitmapDecoder 使用時、Exif の回転情報（Orientation）による縦横サイズ・ストライド計算の不一致を修正しました。
   ・一部の JPEG 写真でプレビューやサムネイルが斜めに歪んだり崩れて表示される問題を解消しました。

2. 一般画像（JPEG/PNG/WebP等）のネイティブ描画最適化
   ・標準画像の不要なサムネイル再エンコード処理を排除し、WinUI 3 のハードウェアアクセラレーションによる直接デコード描画へ最適化しました。
   ・画像一覧表示のレンダリング速度向上およびメモリ使用効率の改善を行いました。

3. ディスクキャッシュ機能の最適化
   ・ディスクキャッシュ対象をデコード負荷の高い RAW 画像に限定（raw_v10_）し、標準画像における不要なディスク I/O およびストレージ消費を削減しました。

◆ 機能改善・操作ガイド
1. 操作説明（ヘルプ）ダイアログおよびドキュメントの更新
   ・アプリ内の操作説明ダイアログを最新の機能仕様に合わせて見直し、分かりやすさを向上しました。
```

---

## 🇺🇸 英語 (en-US)

```text
[What's New in Version 1.1.2.0]

◆ Bug Fixes & Stability
1. Fixed JPEG Thumbnail & Preview Distortion Issue
   - Resolved a pixel stride and orientation calculation mismatch in BitmapDecoder that caused certain JPEG photos with EXIF rotation tags to appear distorted or corrupted.

2. Native Hardware-Accelerated Rendering for Standard Images
   - Eliminated redundant thumbnail re-encoding for standard formats (JPEG, PNG, WebP, etc.), leveraging direct GPU-accelerated decoding for faster rendering and lower memory overhead.

3. Disk Cache Optimization
   - Streamlined disk caching specifically for computationally heavy RAW files (raw_v10_), reducing unnecessary disk I/O and storage consumption for standard images.

◆ Enhancements & Documentation
1. Updated In-App User Guide
   - Refined and updated the built-in Help dialog with clear instructions and latest feature guides.
```
