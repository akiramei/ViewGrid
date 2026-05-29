# 10 — 要求仕様 (IMAGE_VARIANT_MANAGEMENT)

> **Version: v0.1**

## このドキュメントの位置づけ

本書は `IMAGE_VARIANT_MANAGEMENT` Capability に対する **要求仕様** である。
「何のために、誰が、何をできる必要があるか」を記述する。実装方針は AI の判断に委ねる。

---

## 1. 背景と目的

### 1.1 解きたい問題

ユーザーは 1 枚の元画像を取り込んだ後、その画像を **異なる設定 (トリミング・回転・スケーリング)
で複数回使い回したい**。例えば:

- 同じ画像を「全体像」と「拡大トリミング」の 2 通りで配置
- 同じ画像を「水平方向に反転した版」と「元の版」の両方で配置
- 同じ画像を「白背景を自動除去した版」と「除去しない版」で並べる

これを実現するには、元画像 (`ImageAsset`) とは別に **「設定違いの使い回し単位」**
を独立した概念として扱う必要がある。本 Capability はこれを **`ImageCopy` (論理コピー / 派生物)** と呼ぶ。

### 1.2 「論理コピー」という意味的核心

`ImageCopy` は **物理コピー (画像データの複製) ではない**。元画像のピクセルデータは
1 か所に保持され、`ImageCopy` は元画像へのポインタ + 設定 (Transform / Scaling / Crop 等) を持つ。

これは ViewGrid プロジェクト固有の **意味的派生物** という概念であり、本 PoC の中核実験対象でもある。

### 1.3 IMAGE_VARIANT_MANAGEMENT が担う範囲

- `ImageAsset` の取り込み・削除・問い合わせ
- `ImageCopy` の生成・削除・問い合わせ
- `ImageCopy` の設定変更 (Transform / ScalingMode / Alignment / AutoCrop / ManualCrop / OccupySize / 名前)
- `ImageCopy` の存在性確認 (他 Capability への提供)

担わないもの:

- 画像ファイルの物理保存形式 (Repository / Infrastructure に委ねる)
- 画像の描画 / 出力 (`RENDERING_EXPORT` に委ねる)
- ワークスペース管理 / DB 切替 (`WORKSPACE_MANAGEMENT` に委ねる)
- グリッドへの配置 (`GRID_COMPOSITION` に委ねる)
- ProtectedRegion (保護領域、PhotoBoard 連動) — v0.2 候補とし、本 v0.1 では対象外

---

## 2. 想定ユーザーとシナリオ

### 2.1 ペルソナ

| ペルソナ | 期待 |
| --- | --- |
| **編集者 (主たる利用者)** | 1 枚の画像から異なる派生物を簡単に作れる。元画像を削除すると派生物がどうなるか予測可能 |
| **大量再利用者** | 同じ画像を 5〜10 通りの設定で派生させ、まとめて管理できる。命名で識別できる |
| **整理志向ユーザー** | 元画像が重複していたら自動で 1 つに統合される (hash 重複除去) |

### 2.2 ユーザーシナリオ

#### S1: 画像取り込みと最初の派生物

ユーザーは PNG ファイルを取り込む → `ImageAsset` が生成される。
続けて派生物 (`ImageCopy`) を 1 つ生成する → ScalingMode = UniformContain、Alignment = 中央、
他は既定値。

#### S2: 同じ元画像から複数派生物

ユーザーは派生物 A (元画像のまま) と派生物 B (左 50% トリミング) を作る。
ストレージ上の画像データは 1 つだけ。

#### S3: 重複取り込みの自動統合

ユーザーが同じ PNG を 2 回取り込む → 2 回目は hash 一致で既存 `ImageAsset` が返る。
新しい `ImageAsset` は生成されない。

#### S4: AutoCrop と ManualCrop の併用 (優先関係)

派生物に AutoCrop (白背景除去) を設定。続けて ManualCrop も設定する。
**描画時には ManualCrop が優先される** が、両設定とも保持される (どちらか OFF にすれば残る方が効く)。

#### S5: 元画像削除のカスケード

ユーザーは元画像を削除しようとする。その元画像から派生した `ImageCopy` が 3 つ存在。
**この時の挙動は本 Capability では決めない** (上位 Coordinator の決定)。ただし、

- 派生物が存在する状態での `DeleteImageAsset` は **失敗を返すことを許容**
- もしくは派生物すべての削除を **呼び出し側に要求** する

これは方法論レベルで意図的に「保留」する設計判断。詳細は 20-capability-bom.md §6.

#### S6: 派生物の改名

ユーザーは派生物に「コーヒー左半分」のような人間可読名を付ける。
名前は省略可能 (省略時は自動生成名)。

---

## 3. ユースケース

### 3.1 一覧

| ID | 名前 | 種別 | 概要 |
| --- | --- | --- | --- |
| UC-01 | `ImportImageAsset` | command | 画像データを取り込み、`ImageAsset` を生成 (hash 重複時は既存を返す) |
| UC-02 | `DeleteImageAsset` | command | `ImageAsset` を削除 (派生物が残っている場合の挙動は明示。§3.2 参照) |
| UC-03 | `ListImageAssets` | query | ワークスペース内の `ImageAsset` 全件を取得 |
| UC-04 | `GetImageAsset` | query | ID から `ImageAsset` を取得 |
| UC-05 | `CreateImageCopy` | command | 既存 `ImageAsset` から新しい `ImageCopy` を生成 |
| UC-06 | `DeleteImageCopy` | command | `ImageCopy` を削除 |
| UC-07 | `ListImageCopies` | query | 全 `ImageCopy` を取得 (オプションで AssetId 絞り込み) |
| UC-08 | `GetImageCopy` | query | ID から `ImageCopy` を取得 |
| UC-09 | `ChangeCopyTransform` | command | 回転 / 反転を変更 |
| UC-10 | `ChangeScalingMode` | command | スケーリングモードを変更 (UniformContain / UniformCover / Fill) |
| UC-11 | `ChangeAlignment` | command | アライメント (アンカー点) を変更 |
| UC-12 | `ChangeAutoCropSettings` | command | AutoCrop を設定 (TargetColor + Threshold) または OFF |
| UC-13 | `ChangeManualCropSettings` | command | ManualCrop を設定 (X, Y, W, H) または OFF |
| UC-14 | `ChangeDefaultOccupySize` | command | 配置時の既定占有サイズを変更 |
| UC-15 | `RenameImageCopy` | command | 派生物の人間可読名を変更 (null も許容 = 自動生成名) |
| UC-16 | `ImageCopyExists` | query | 指定 ID の `ImageCopy` が存在するか (cross-Capability 用) |
| UC-17 | `ImageAssetExists` | query | 指定 ID の `ImageAsset` が存在するか |

### 3.2 主要ユースケースの詳細

#### UC-01: ImportImageAsset

- **入力**: 画像データ (バイト列) / 元ファイル名 (任意) / MIME タイプ
- **事後条件**:
  - 新規 → `ImageAsset` が 1 つ生成される。ピクセルサイズと SHA-256 hash が計算済み
  - hash 既存 → **既存の `ImageAsset` を返す。新規生成しない (R-02 不変条件)**
- **失敗**:
  - `InvalidImageData`: 画像データが decode できない (R-01 違反)
  - `UnsupportedMimeType`: サポート外形式

#### UC-02: DeleteImageAsset

- **入力**: `ImageAsset` ID
- **事後条件**:
  - 関連派生物 0 件 → 元画像が削除される
  - 関連派生物 ≥1 件 → `DependentCopiesExist` 失敗 (派生物の ID リストを payload に)
- **失敗**: `NotFound`, `DependentCopiesExist`

> [!IMPORTANT]
> 関連派生物がある時の自動カスケード削除は **本 Capability では行わない**。
> 呼び出し側 (上位 Coordinator) が派生物の削除を先に行うか、明示的にカスケード要求するか
> を決定する。本 Capability は「依存があれば拒否する」純度を保つ。

#### UC-05: CreateImageCopy

- **入力**:
  - `AssetId`: 元画像 ID
  - `CopyName` (省略可、null も明示的に渡せる)
  - 初期設定 (Transform, ScalingMode, Alignment, OccupySize) — すべて省略可で既定値あり
- **事後条件**: 新規 `ImageCopy` が生成される
- **失敗**:
  - `NotFound`: 元画像が存在しない
  - `InvalidAlignment` / `InvalidScalingMode` / `InvalidOccupySize`: 初期値の妥当性違反

#### UC-12: ChangeAutoCropSettings

- **入力**: `CopyId` / `target_color` (UInt32 ARGB) と `threshold` (0-255) の **両方** または **両方 null** (= OFF)
- **失敗**:
  - `NotFound`
  - `InvalidAutoCropSettings`: 片方だけ null 等 (R-06 違反)

#### UC-13: ChangeManualCropSettings

- **入力**: `CopyId` / `x, y, width, height` の **4 値すべて** (各 0..1 範囲) または **すべて null** (= OFF)
- **事後**: 設定が保存される。**実際の優先関係は描画時に決定される** (R-08 参照)
- **失敗**: `NotFound`, `InvalidManualCropFractions`

#### UC-16: ImageCopyExists

- **入力**: `CopyId`
- **出力**: `bool`
- **失敗**: なし (存在しなければ false を返す)
- **用途**: GRID_COMPOSITION の UC-05 (`PlaceImageCopy`) が `UnknownCopyId` 判定に使う

---

## 4. 非機能要件

### 4.1 整合性

- すべての `ImageCopy` は **必ず** 存在する `ImageAsset` を参照する (R-03 不変条件)
- AutoCrop は両値揃うか両値 null か (R-06 不変条件)
- ManualCrop の値域は [0.0, 1.0] (R-07 不変条件)

### 4.2 取消可能性

- すべての状態変更 UC は取消可能 (`HISTORY_MANAGEMENT` の対象)
- 元画像取込 (UC-01) と削除 (UC-02) も取消対象

### 4.3 重複除去

- 同一バイト列の `ImageAsset` を 2 回取り込んでも **物理ストレージは 1 つ**
- 判定は SHA-256 hash で行う (R-02 不変条件)

### 4.4 永続化

- ストレージ形式 (DB / ファイル) は規定しない
- ただし `FileHash` のユニーク性は本 Capability が保証する責任を持つ (Repository に委ねない)

### 4.5 性能

- ワークスペースあたり `ImageAsset` 数 ≤ 10,000、`ImageCopy` 数 ≤ 50,000
- UC-16 (`ImageCopyExists`) は O(1) 期待 (hash table 等のインデックス)
- UC-01 (`ImportImageAsset`) の hash 計算は許容 (100 MB / 数秒)

---

## 5. Ubiquitous Language

| 用語 | 意味 | 注意 |
| --- | --- | --- |
| **ImageAsset** (元画像) | 取り込まれたオリジナル。ピクセルデータの物理単位 | 本 Capability で定義 |
| **ImageCopy** (論理コピー / 派生物) | `ImageAsset` への参照 + 設定。設定違いの使い回し単位 | **本 Capability が権威**。GRID_COMPOSITION は CopyId のみ参照 |
| **FileHash** | `ImageAsset` の SHA-256 (16 進小文字) | 重複除去の根拠 |
| **Transform** | 派生物の幾何変形 (回転 90 度刻み + 水平反転 + 垂直反転) | 角度は連続値ではない (R-09) |
| **ScalingMode** | スケーリング方式 (`UniformContain` / `UniformCover` / `Fill`) | 列挙値 (R-04) |
| **Alignment** | セル内アンカー点 | 列挙値 (R-05)。詳細は 30-design.md §3.3 |
| **AutoCrop** | 単色余白の自動トリミング設定 | TargetColor + Threshold の両値で意味がある (R-06) |
| **ManualCrop** | 任意矩形トリミング | 0..1 比率の bbox (R-07) |
| **OccupySize** | 配置時の既定占有セル数 | GRID_COMPOSITION の OccupySize と同じ値オブジェクト型を使う (用語整合) |
| **CopyName** | 派生物の人間可読名 | 省略可 (null) で自動生成名 |

> [!IMPORTANT]
> `OccupySize` と `PixelSize` は GRID_COMPOSITION の用語集と **同じ意味で使う**。
> 本 Capability で再定義しない。詳細は親ディレクトリの `../10-requirements.md` §5。

---

## 6. 受け入れ基準

- [ ] すべての UseCase に事前/事後/失敗条件が記述されている
- [ ] 用語集が GRID_COMPOSITION 側と矛盾しない (特に OccupySize / PixelSize)
- [ ] **AutoCrop と ManualCrop の優先関係** が明示されている (R-08)
- [ ] **UC-02 のカスケード方針** が明示されている (本 Capability では拒否、上位で決定)

---

## 7. 関連ドキュメント

- `20-capability-bom.md` — Capability の意味境界と Decision ownership
- `21-image-variant-management.yaml` — 機械可読版
- `30-design.md` — Rule ledger / Entity 意味 / テスト規範
- `40-ai-implementation-prompt.md` — AI 実装プロンプト
- `../README.md` — 親ディレクトリ (GRID_COMPOSITION サンプル)
