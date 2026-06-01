# Crop Resolver — 生成仕様 v2 (F-P12 enriched / d-3 overlay 適用後の凍結入力)

> これは **F-P12 の blind 再生成に渡す唯一の入力**。`crop-resolver-spec.md` (v1, F-P10) に
> **d-3 generation_overlay の欠落次元補填**を加えたもの。**CropFraction 契約の意味差分** = この §0 の 1-2 に
> 列挙した補填点のみ (= 実験操作)。加えて §0-3 に **実験スコープの差分** (生成対象を CropFraction のみへ絞り
> resolver を除外) を明示。**実装コード片は含まない** (意味記述のみ)。生成器はこの spec だけから C# を書く。
> 既存実装/テスト/リポジトリのファイルは一切参照しない。

## §0. v1 → v2 の差分 (d-3 で同定した欠落次元の補填。これが実験操作)
F-P10 で blind 再生成は成立したが、`ToPixelBbox` の **丸めモードが v1 spec に無く**、生成器が
`AwayFromZero` を選び、実装の `Math.Round` 既定 (`ToEven` / 銀行家丸め) と **中間値 (x.5) で発散**した。
v2 はその欠落次元 (`vo_method_contract.numeric.rounding`) を明示する:

1. **[追加] `ToPixelBbox` の丸めモード = `ToEven` (銀行家丸め)** を明記。
   - 比率×軸サイズが x.5 のちょうど中間値のとき、**偶数側へ丸める** (例: 2.5 → 2、3.5 → 4、0.5 → 0)。
   - これは現実装 (`System.Math.Round(value)` の既定 MidpointRounding) の **as-built 再現** であり、
     「ToEven が望ましい設計か」(AwayFromZero との優劣) は別途人間が決める決定点 (本 spec では再現を目的とする)。
2. **[追加] `ToPixelBbox` の入力 precondition = `width`/`height` は正の整数** (画像実寸。呼出側が保証)。
   - **負値・0 の挙動は未定義** (呼出側責任)。生成器はこの範囲外を正規化してもしなくてもよい (spec 非規定)。
   - = oracle はこの範囲外を判定しない。in-range (正寸法) の意味等価のみが検証対象。
3. **[scope 差分・非意味] 生成対象を CropFraction のみに絞る** — v1 は CropFraction + ImageCropResolver の 2 つを生成対象としたが、resolver は F-P10 で完全収束済みのため本実験では除外。これに伴い「所与の型」から resolver 関連 (ImageCopy/ImageAsset/IImageCropResolver/IAutoCropBboxResolver/IImageStorage) を外し、振る舞い例も CropFraction のものへ差し替えた。**これは CropFraction の意味契約の変更ではなく、検証対象を絞る実験スコープの差分**。

CropFraction の契約 (Full / IsFull / From×2 / ToPixelBbox の clamp 上限・写像) は §0-1/§0-2 の補填点を除き v1 と完全に同一。

---

## 生成対象 (この実験では CropFraction のみ生成する)
**`CropFraction`** — `namespace ViewGrid.Core.Entities` の `readonly record struct`。
(v1 では ImageCropResolver も生成したが F-P10 で完全収束済み。F-P12 は発散の残った CropFraction を再生成し、
 enriched spec で発散が消えるかを検証する。)

## 所与の型 (生成しない。これらの署名に対してコンパイルできるように書く)
```
// namespace ViewGrid.Core.Entities
public readonly record struct AutoCropFraction(double X, double Y, double Width, double Height); // 0–1 比率
public readonly record struct ManualCropFraction(double X, double Y, double Width, double Height); // 0–1 比率
```

## 生成対象: CropFraction の契約
「実効的なクロップ bbox」の比率 (0–1)。AutoCrop/ManualCrop 双方からの変換先で、源を意識せず使える統一型。座標系非依存。

- フィールド: `double X, Y, Width, Height` (record struct の位置パラメータ)。
- `static CropFraction Full` = `(0, 0, 1, 1)` — クロップ無効 (全領域) のセンチネル。
- `bool IsFull(double tolerance = 1e-6)` — X,Y がともに 0 近傍 (絶対値 < tolerance) かつ Width,Height がともに 1.0 近傍 (|値-1| < tolerance) のとき true (= クロップ無効)。
- `(int X, int Y, int Width, int Height) ToPixelBbox(int width, int height)` — 比率を整数ピクセル bbox へ展開:
  - `x = clamp(round(X*width), 0, width)`、`y = clamp(round(Y*height), 0, height)`。
  - `w = clamp(round(Width*width), 0, width - x)`、`h = clamp(round(Height*height), 0, height - y)`。
  - **★ round の丸めモード = `ToEven` (銀行家丸め)** — x.5 の中間値は偶数側へ (§0-1)。C# では `Math.Round(value)` 既定、
    または `Math.Round(value, MidpointRounding.ToEven)`。**`AwayFromZero` ではない**。
  - clamp は範囲内へ丸める。w/h の上限が **残り (width-x / height-y)** である点に注意 (はみ出さない)。
  - **★ precondition**: `width`/`height` は正の整数 (§0-2)。負値・0 は未定義 (範囲外挙動は自由)。
- `static CropFraction From(AutoCropFraction f)` — `f.X,f.Y,f.Width,f.Height` をそのまま写像。
- `static CropFraction From(ManualCropFraction f)` — 同上。

## 観測可能な振る舞い例 (spec の意味の明確化。網羅 oracle ではない)
- `CropFraction(0.05, 0.05, 0.9, 0.9).ToPixelBbox(800, 600)` → `(40, 30, 720, 540)` (整数、丸め非依存)。
- `CropFraction(0.5, 0, 1, 1).ToPixelBbox(5, 5)` → `X = 2` (0.5×5 = 2.5 → ToEven で 2。AwayFromZero なら 3)。 ★ 丸めモードの差が出る例。
- `CropFraction.Full.ToPixelBbox(640, 480)` → `(0, 0, 640, 480)`。
- `CropFraction(0.5, 0, 0.9, 1).ToPixelBbox(100, 100)` → `(50, 0, 50, 100)` (w が残り 50 に clamp)。

## 制約
- `using` / namespace を正しく付け、所与の型の署名に合わせる。
- public 表面は呼び出し側 (Renderer/View/UseCase) が依存するため、上記の名前・署名どおりに作る (Full / IsFull / ToPixelBbox / From×2)。
- 実装の中身は spec の意味を満たせば書き方は自由。**ただし丸めは `ToEven` を厳守** (§0-1、本実験の検証点)。
