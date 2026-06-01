# Crop Resolver — 生成仕様 (F-P10 blind generation の凍結入力)

> これは Pattern 1 micro-pilot で **独立生成器に渡す唯一の入力**。as-built BOM の IR-04/IR-05/IR-06 +
> `GENERATION-GAP-REPORT.md` の欠落次元補填から構成。**実装コード片は含まない** (意味記述のみ)。
> 生成器はこの spec だけから C# を書く。既存実装/テストは参照しない。

## 生成対象 (2 つ)
1. **`CropFraction`** — `namespace ViewGrid.Core.Entities` の `readonly record struct`。
2. **`ImageCropResolver`** — `namespace ViewGrid.Application.Services` の `sealed class`。既存インターフェイス `IImageCropResolver` を実装する。

## 所与の型 (生成しない。これらの署名に対してコンパイルできるように書く)
```
// namespace ViewGrid.Core.Entities
public readonly record struct AutoCropSettings(uint TargetColorArgb, byte Threshold);
public readonly record struct AutoCropFraction(double X, double Y, double Width, double Height); // 0–1 比率
public readonly record struct ManualCropFraction(double X, double Y, double Width, double Height); // 0–1 比率

public sealed class ImageCopy {
    public AutoCropSettings? AutoCrop { get; init; }     // null = AutoCrop OFF
    public ManualCropFraction? ManualCrop { get; init; } // null = ManualCrop OFF
    // (他フィールドは resolver に無関係)
}
public sealed class ImageAsset {
    public Guid Id { get; init; }
    public string StoredRelativePath { get; init; }
    // (他フィールドは resolver に無関係)
}

// namespace ViewGrid.Core.Services
public interface IImageCropResolver {
    Task<CropFraction?> ResolveAsync(ImageCopy copy, ImageAsset asset, CancellationToken ct = default);
}
public interface IAutoCropBboxResolver {
    // Cache miss なら原画像を走査。読込失敗は null。比率 (0–1) を返す。
    Task<AutoCropFraction?> ResolveAsync(Guid assetId, string sourceImageAbsolutePath, AutoCropSettings settings, CancellationToken ct = default);
}
// namespace ViewGrid.Core.Services
public interface IImageStorage {
    string ResolveAbsolutePath(string relativePath);
    // (他メンバは無関係)
}
```

## 生成対象1: CropFraction の契約
「実効的なクロップ bbox」の比率 (0–1)。AutoCrop/ManualCrop 双方からの変換先で、源を意識せず使える統一型。座標系非依存。

- フィールド: `double X, Y, Width, Height` (record struct の位置パラメータ)。
- `static CropFraction Full` = `(0, 0, 1, 1)` — クロップ無効 (全領域) のセンチネル。
- `bool IsFull(double tolerance = 1e-6)` — X,Y がともに 0 近傍 (絶対値 < tolerance) かつ Width,Height がともに 1.0 近傍 (|値-1| < tolerance) のとき true (= クロップ無効)。
- `(int X, int Y, int Width, int Height) ToPixelBbox(int width, int height)` — 比率を整数ピクセル bbox へ展開:
  - `x = clamp(round(X*width), 0, width)`、`y = clamp(round(Y*height), 0, height)`。
  - `w = clamp(round(Width*width), 0, width - x)`、`h = clamp(round(Height*height), 0, height - y)`。
  - round は四捨五入、clamp は範囲内へ丸める。w/h の上限が **残り (width-x / height-y)** である点に注意 (はみ出さない)。
- `static CropFraction From(AutoCropFraction f)` — `f.X,f.Y,f.Width,f.Height` をそのまま写像。
- `static CropFraction From(ManualCropFraction f)` — 同上。

## 生成対象2: ImageCropResolver の契約
`IImageCropResolver` を実装。ctor 依存: `(IAutoCropBboxResolver autoCropResolver, IImageStorage imageStorage)`。

`ResolveAsync(ImageCopy copy, ImageAsset asset, CancellationToken ct)` の意味:
1. **前提ガード**: `copy` / `asset` が null なら `ArgumentNullException` を投げる (結果チャネルとは別。前提違反は例外)。
2. **precedence = ManualCrop 排他優先 → AutoCrop → null** (IR-04):
   - `copy.ManualCrop` が非 null なら: それを `CropFraction.From` で変換し、**`IsFull()` なら `null` を返す。そうでなければその CropFraction を返す**。
     - ★ **短絡**: ManualCrop が設定されている時点で AutoCrop は **一切参照しない** (full で null になる場合も AutoCrop に *落ちない*)。
   - そうでなく `copy.AutoCrop` が非 null なら: `imageStorage.ResolveAbsolutePath(asset.StoredRelativePath)` で絶対パスを得て、`autoCropResolver.ResolveAsync(asset.Id, path, settings, ct)` を await。結果が非 null ならそれを `CropFraction.From` で変換して返す。null なら `null` を返す。
   - どちらも null なら `null`。
3. **エラー表現 (結果チャネル)**: 戻り値は `CropFraction?`。`null` は **「クロップ無効」または「AutoCrop 走査が解決できなかった (resolver が null)」の両義**。結果の失敗を `ErrorOr` や例外で表さない (前提ガードの ArgumentNullException を除く)。
4. **I/O 条件**: AutoCrop 経路に入った時のみ `imageStorage` / `autoCropResolver` を呼ぶ。ManualCrop 経路・both-off 経路では **AutoCrop の I/O を一切起こさない**。

## 観測可能な振る舞い例 (spec の意味の明確化。網羅 oracle ではない)
- ManualCrop=null, AutoCrop=null → `null` (AutoCrop I/O なし)。
- ManualCrop=(0.1,0.2,0.3,0.4), AutoCrop=White → `CropFraction(0.1,0.2,0.3,0.4)` (AutoCrop I/O なし)。
- ManualCrop=Full(0,0,1,1), AutoCrop=White → `null` (AutoCrop I/O なし = 短絡)。
- ManualCrop=null, AutoCrop=White, resolver→(0.05,0.05,0.9,0.9) → `CropFraction(0.05,0.05,0.9,0.9)`。
- ManualCrop=null, AutoCrop=Black, resolver→null → `null`。

## 制約
- `using` / namespace を正しく付け、所与の型の署名に合わせる。
- CropFraction と ImageCropResolver の **public 表面は呼び出し側 (Renderer/View/UseCase) が依存するため、上記の名前・署名どおりに** 作る (Full / From×2 / IsFull / ToPixelBbox / ResolveAsync)。
- 実装の中身 (分岐) は spec の意味を満たせば書き方は自由。
