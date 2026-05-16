# ViewGrid マニュアル用サンプル画像

マニュアルのスクリーンショットを撮影する際にアプリへドラッグ&ドロップで取り込む素材画像。 機能ごとに以下の 6 セット (合計 20 枚) を用意。

> ⚠️ これらの画像は手描き / 写真ではなく `tools/SampleImageGenerator` で自動生成されたもの。 配色や寸法を変えたい場合は `Program.cs` を編集して再生成 (`dotnet run --project tools/SampleImageGenerator`)。

## 共通テンプレ (Set A〜E)

すべての画像 (Set F を除く) に以下の要素を含めており、 ViewGrid のあらゆる操作の結果が screenshot で視認できます。

- **外枠 (12px、 アクセント色)** — 画像本体の境界。 Crop / 配置で残った範囲を明示
- **grid 線 (100px 間隔 + 50px 補助線)** — PixelOffset 1〜10px や Scaling の伸縮を読み取る
- **4 隅マーカー (TL/TR/BL/BR)** — Alignment / Rotation / Flip の結果を一目で識別
- **中央番号 + ラベル** — 配置時の画像識別と D&D 経路の追跡

## セット一覧

### Set A: 識別画像 (8 枚、 1200×1200)

| ファイル | 配色 | 用途 |
|---|---|---|
| `sample-01.png` | Red | 配置 / D&D の主役 (グリッドの 1 番目に置く想定) |
| `sample-02.png` | Pink | 同上 (2 番目) |
| `sample-03.png` | Blue | 同上 (3 番目) |
| `sample-04.png` | Cyan | 同上 (4 番目) |
| `sample-05.png` | Green | 5 番目以降 (3×3 グリッド用) |
| `sample-06.png` | Lime | 同上 |
| `sample-07.png` | Orange | 同上 |
| `sample-08.png` | Amber | 同上 |

**使う章**: Quick Start §3、 §1 基本概念、 §2 アセット管理、 §3 グリッド、 §4 配置 (D&D / Inspector / PixelOffset)、 §6 出力、 §8 履歴

### Set B: アスペクト比違い (4 枚)

| ファイル | サイズ | 用途 |
|---|---|---|
| `aspect-landscape.png` | 1920×1080 (16:9) | §5.11 Scaling demo (横長を 1:1 セルに置く) |
| `aspect-portrait.png` | 1080×1920 (9:16) | §5.11 Scaling demo (縦長を 1:1 セルに置く) |
| `aspect-square.png` | 1200×1200 (1:1) | §5.11 Scaling demo (基準ケース) |
| `aspect-pano.png` | 2400×800 (3:1) | §5.11 Scaling demo (極端なアスペクト) |

**使う章**: §5.11 スケーリングとアライメント。 同じ素材を 6 モード (Original / Contain / Shrink / Enlarge / Cover / Stretch) で並べる比較 screenshot 用。

### Set C: AutoCrop 対象 (3 枚、 1600×1600)

| ファイル | 余白色 | 用途 |
|---|---|---|
| `autocrop-white.png` | 白 #FFFFFF | AutoCrop プリセット 「白」 demo |
| `autocrop-black.png` | 黒 #000000 | AutoCrop プリセット 「黒」 demo |
| `autocrop-transparent.png` | α=0 | AutoCrop プリセット 「透明」 demo |

外周 200px が単色余白、 内側 1200×1200 が共通テンプレ。 AutoCrop で余白が検出されると内側 1200×1200 が残る形。

**使う章**: §5.13.1 AutoCrop

### Set D: 回転 / 反転 demo (1 枚、 1200×1200)

| ファイル | 用途 |
|---|---|
| `rotation-demo.png` | §5.12 Rotation / Flip demo (90°/180°/270°/FlipH/FlipV それぞれの結果が一瞬で分かる) |

4 象限を強い原色 (TL=赤 / TR=青 / BL=緑 / BR=黄) + 中央に大きな ↑ 矢印 + 「TOP」 テキスト。 回転や反転がかかると 4 象限の位置と矢印の向きが変わるので結果が確実に視認できます。

### Set E: 保護領域 demo (2 枚)

| ファイル | サイズ | 用途 |
|---|---|---|
| `region-speech.png` | 1600×900 | §5.14 ProtectedRegion demo (吹き出し風領域を浮かせる) |
| `region-label.png` | 1200×1200 | §5.14 ProtectedRegion demo (ロゴ風領域を浮かせる) |

「画像の一部を独立アセットとして分離する」 ユースケースを再現するため、 元画像内に **明らかな矩形領域 (REGION / LABEL)** が含まれています。 ViewGrid の保護領域機能でこの矩形を Region として設定し、 PhotoBoard 等で 「親が回転しても領域だけ水平」 「親側塗りで元位置を白く塗る」 等を demo。

### Set F: PhotoBoard 用 (4 枚、 1600×1200)

| ファイル | 配色テーマ | 用途 |
|---|---|---|
| `photo-01.png` | 夕焼け (橙 → 桃 → 紫) | PhotoBoard demo (写真風の画像で frame + 影 + ジッターが映える) |
| `photo-02.png` | 海 (紺 → 青緑 → 水色) | 同上 |
| `photo-03.png` | 森 (深緑 → 緑 → 黄緑) | 同上 |
| `photo-04.png` | 街夜景 (紺 → 灰 → 桃) | 同上 |

**Set F だけ共通テンプレを使わず** リニアグラデーション + Perlin ノイズで実写感を出しています (PhotoBoard の演出効果が grid 線で打ち消されないため)。

**使う章**: §6.15 出力モード、 §6.16 PhotoBoard 詳細

## 再生成方法

```bash
# リポジトリ root から
dotnet run --project tools/SampleImageGenerator
```

`docs/sample-images/` に上記 20 枚が再生成されます (上書き)。

配色 / サイズ / レイアウトを変えたい場合は `tools/SampleImageGenerator/Program.cs` を編集。

## 撮影時の使い分け早見表

| 章 | 推奨サンプル |
|---|---|
| Quick Start §3 5 分チュートリアル | Set A の `sample-01〜04` (2×2 グリッド) |
| §1 基本概念 (画面構成 screenshot) | Set A の `sample-01〜04` 配置済み |
| §3 グリッド (列幅変更 demo) | Set A の `sample-01〜04` |
| §4 配置 (PixelOffset demo) | Set A の `sample-01` (赤の grid 線が動くのが分かる) |
| §5.11 Scaling 比較 | Set B 4 枚 |
| §5.12 Rotation/Flip | Set D `rotation-demo` |
| §5.13.1 AutoCrop | Set C 3 枚 (プリセット切替の前後 screenshot) |
| §5.13.2 ManualCrop | Set A `sample-01` (grid 線で矩形位置が読める) |
| §5.14 保護領域 | Set E 2 枚 |
| §6.16 PhotoBoard | Set F 4 枚 (2×2 配置で 「写真ボード」 感を演出) |
| §6.17 Trim Mode 比較 | Set A の `sample-01,03,05,07` を sparse 配置 (空セルあり) |
