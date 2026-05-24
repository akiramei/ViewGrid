# 画像インベントリ (撮影リスト)

ViewGrid マニュアル用のスクリーンショット一覧。 各エントリには **ファイルパス / 章・節 / 撮影内容 / 使用サンプル画像 / 状態** を記録する。 マニュアル本文の HTML コメント (`<!-- CAPTURE ... -->`) と一対一で対応。

## 撮影方針 (共通)

- **解像度**: Windows 11 ディスプレイスケーリング 100%、 ウィンドウサイズは下記指定
- **テーマ**: Light (アクセント色は既定の Blue) を基本。 Dark テーマ固有の説明箇所のみ Dark で撮影
- **言語**: 日本語 (`ja`) で撮影
- **アノテーション**: なし (素のスクリーンショット)。 文章側で操作箇所を指示する
- **ファイル形式**: PNG (lossless)、 半透明背景は不要なので不透明で
- **マスク**: 撮影時の DB / ファイル名にユーザー固有のパス文字列が映り込む箇所は別データに差し替える (例: `D:\Users\<your-name>\...` → `C:\Users\sample\...`)
- **使用サンプル画像**: `docs/sample-images/` 配下の 20 枚を使う。 詳細は [sample-images/README.md](../sample-images/README.md) を参照
- **典型サイズ**:
  - ウィンドウ全体: **1280×800** (16:10)
  - ダイアログ単体: ダイアログの実サイズ + 周辺余白 16px
  - フライアウト / コンテキストメニュー: 実サイズ
  - ペイン拡大: 800×600 程度のクロップ

## 凡例

- ❌ TODO: 未撮影
- 🟡 仮: 仮画像 (撮り直し前提) を入れている
- ✅ 確定: 本番画像

---

## Quick Start (`docs/quickstart.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `qs/qs-01-01-main-window-overview.png` | §1 はじめに | sample-01〜04 (2×2 配置) | アプリ起動後の全体像 (3 ペイン構成)。 1280×800、 2×2 グリッド + 2〜3 件配置済み、 言語 ja | ❌ TODO |
| `qs/qs-03-01-drag-drop-images.png` | §3-1 画像を取り込む | sample-01〜04 (エクスプローラ上の 4 ファイル) | エクスプローラから 4 枚をドラッグ中、 ドロップターゲットのハイライトが見える瞬間。 1280×800 | ❌ TODO |
| `qs/qs-03-02-create-grid-flyout.png` | §3-2 グリッド作成 | (サンプル不要) | 「+ 新規」 押下後の作成フライアウト (名前 「グリッド 1」 / 列 2 / 行 2 / 1200×1200)。 800×600 程度のクロップ | ❌ TODO |
| `qs/qs-03-03-drag-to-cell.png` | §3-3 セルへ配置 | sample-01〜04 (sample-01 配置済 + sample-02 ドラッグ中) | 候補からセルへ D&D 中、 ホバーセルがハイライト、 配置済みセルも見える。 1280×800 | ❌ TODO |
| `qs/qs-03-04-preview-window.png` | §3-4 プレビュー | sample-01〜04 (2×2 配置) | プレビューウィンドウ + 親ウィンドウが両方写る。 拡大率 100%。 1280×800 | ❌ TODO |
| `qs/qs-03-05-export-png-dialog.png` | §3-4 PNG 出力 | (サンプル不要) | OS ネイティブの 「PNG として保存」 ダイアログ。 ダイアログ実サイズ + 余白 16px | ❌ TODO |

---

## User Manual

### §1 基本概念 (`user-manual/01-concepts.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-01-03-main-window-3pane.png` | §1.3 画面構成 | sample-01〜04 (3 件配置) | メインウィンドウの 3 ペイン構成。 グリッド 2 件存在、 1 件目アクティブ、 中央セル選択中 (オレンジ枠)、 右ペイン Inspector 表示。 1280×800 | ❌ TODO |

### §2 アセット管理 (`user-manual/02-assets.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-02-04-add-images-picker.png` | §2.4.2 取り込み方法 | (サンプル不要) | 画像取り込み用ファイルピッカー。 タイトル 「画像を選択」、 フィルタ 「画像 (\*.png; \*.jpg; ...)」。 ピッカー実サイズ + 周辺余白 | ❌ TODO |
| `um/um-02-05-add-variant.png` | §2.5.2 新規バリアント作成 | sample-01 のアセット (バリアント 2 件) | 候補リストでアセット行展開、 「+ バリアント追加」 ボタンが見える状態。 480×600 程度のクロップ | ❌ TODO |

### §3 グリッド (`user-manual/03-grids.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-03-06-create-grid-flyout.png` | §3.6.1 新規グリッド作成 | (サンプル不要) | 新規グリッド作成フライアウト (列 2 / 行 2 / 1200×1200、 既定値)。 800×600 程度のクロップ | ❌ TODO |
| `um/um-03-06-grid-properties.png` | §3.6.2 グリッド名/サイズ編集 | (サンプル不要、 グリッドのみ選択) | 右ペインのグリッド設定 (ドラフト編集中で ● バッジ表示)。 480×800 程度のクロップ | ❌ TODO |
| `um/um-03-07-boundary-drag.png` | §3.7.1 境界ドラッグ | sample-01〜09 (3×3 で 6 件程度配置) | 境界ドラッグで列幅変更中、 境界線が強調表示。 800×600 程度のクロップ | ❌ TODO |

### §4 配置 (`user-manual/04-placements.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-04-09-drop-valid.png` | §4.9.1 配置作成 (D&D) | sample-02 ドラッグ中、 sample-01 配置済 | 候補からドラッグ中、 1×1 セルにホバーで緑ハイライト、 配置済みセルも見える。 1000×800 程度 | ❌ TODO |
| `um/um-04-10-inspector.png` | §4.10 配置固有特性 | sample-01 配置 (選択中) | Inspector 全体 (配置固有 Expander 展開 + 共有特性 Expander 折り畳み + 保存バー + 配置削除)。 480×900 程度 | ❌ TODO |

### §5 共有特性 (`user-manual/05-shared-properties.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-05-11-scaling-modes.png` | §5.11.1 スケーリングモード | aspect-landscape を 1:1 セルに配置、 6 モード並べた合成 | 6 スケーリングモードの比較 (Original / Contain / ShrinkOnly / EnlargeOnly / Cover / Stretch)。 1280×720 合成 | ❌ TODO |
| `um/um-05-11-alignment.png` | §5.11.2 アライメント | (UI 単体撮影) | アライメント 9 アンカーのラジオボタン (中央選択中)。 480×400 程度のクロップ | ❌ TODO |
| `um/um-05-13-autocrop.png` | §5.13.1 AutoCrop | autocrop-white 配置 | AutoCrop 設定 (対象色 「白 #FFFFFF」、 許容色差スライダー値 16、 プレビューサムネに crop 矩形が点線表示)。 480×800 程度 | ❌ TODO |
| `um/um-05-13-manualcrop-editor.png` | §5.13.2 ManualCrop 詳細編集 | sample-01 | ManualCrop 詳細編集ダイアログ (拡大率 400%、 矩形編集中 + 8 ハンドル + 数値入力)。 1024×768 程度 | ❌ TODO |
| `um/um-05-14-region-tab.png` | §5.14.2 保護領域の追加 | region-speech (region 2 件登録済) | 保護領域タブ展開、 1 件目選択中で詳細編集パネル (Offset / Rotation / Flip / FillMode) 表示。 480×900 程度 | ❌ TODO |

### §6 出力 (`user-manual/06-output.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-06-15-output-settings.png` | §6.15 出力モード | sample-01〜04 (2×2 配置) | 出力設定 Expander 展開、 通常モード / 全面切り出し。 480×600 程度 | ❌ TODO |
| `um/um-06-16-photoboard-styles.png` | §6.16.1 スタイルプリセット | photo-01〜04 (2×2 配置) | PhotoBoard 10 スタイルの比較。 強度 0.5 で揃える、 各スタイルラベル付き。 1280×720 合成 | ❌ TODO |
| `um/um-06-18-preview.png` | §6.18 プレビュー | sample-01〜04 (2×2 配置) | プレビューウィンドウ全体 (拡大率 100%、 ズームバー + 倍率表示)。 1280×900 | ❌ TODO |

### §7 ワークスペース (`user-manual/07-workspaces.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-07-21-workspace-switch.png` | §7.21 切替ダイアログ | (サンプル不要) | ワークスペース切替ダイアログ。 3 つのワークスペース (Default/work/hobby) カード表示、 アクティブカードに強調枠、 下部に新規作成 + zip インポートボタン。 800×600 | ❌ TODO |

### §8 操作履歴 (`user-manual/08-history.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-08-25-history-flyout.png` | §8.25 履歴 Flyout | sample-01〜05 (操作履歴 10 件積んでから) | 履歴 Flyout (10 件、 現在位置 5 番目、 残り Redo 候補グレーアウト)。 640×500 程度 | ❌ TODO |

### §9 設定 (`user-manual/09-settings.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| `um/um-09-26-settings-dialog.png` | §9.26 設定ダイアログ | (サンプル不要) | 設定ダイアログ全体 (外観 / 既定値 / 自動保存 / 言語 / 入出力)。 800×900 | ❌ TODO |

### §10 リファレンス (`user-manual/10-reference.md`)

| File | Section | 使用サンプル | Caption / 撮影内容 | 状態 |
|---|---|---|---|---|
| _(画像なし、 全テキスト)_ |  |  |  |  |

---

## 進捗サマリ

```
合計: 25 件 (Quick Start: 6 / User Manual: 19)
✅ 確定:  0
🟡 仮:    0
❌ TODO: 25
```

> 章を増やす / 撮影方針を変える場合は、 マニュアル本文の `<!-- CAPTURE -->` ブロックと本表の両方を同期更新する。

## サンプル画像との対応 (便利表)

各 sample-images がどのスクリーンショットで使われるかの逆引き表:

| サンプル画像 | 使用予定の screenshot |
|---|---|
| `sample-01.png` | qs-01-01, qs-03-01, qs-03-03, qs-03-04, um-01-03, um-02-05, um-04-09, um-04-10, um-05-13-manualcrop-editor, um-06-15, um-06-18, um-08-25 |
| `sample-02.png` | qs-01-01, qs-03-01, qs-03-03, qs-03-04, um-01-03, um-04-09, um-06-15, um-06-18, um-08-25 |
| `sample-03.png` | qs-01-01, qs-03-01, qs-03-03, qs-03-04, um-06-15, um-06-18, um-08-25 |
| `sample-04.png` | qs-03-01, qs-03-03, qs-03-04, um-06-15, um-06-18 |
| `sample-05〜09.png` | um-03-07 (3×3 グリッド用), um-08-25 (履歴用) |
| `aspect-landscape.png` | um-05-11-scaling-modes |
| `autocrop-white.png` | um-05-13-autocrop |
| `region-speech.png` | um-05-14-region-tab |
| `photo-01〜04.png` | um-06-16-photoboard-styles |
| `rotation-demo.png`, `aspect-portrait/square/pano.png`, `autocrop-black/transparent.png`, `region-label.png` | 補助。 必要に応じて追加 screenshot で使用 |
