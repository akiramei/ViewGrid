// === F-P12 blind generation artifact (NOT compiled in place; under experiments/) ===
// crop-resolver-spec.v2.md (enriched: rounding=ToEven 明示) のみから独立生成器が blind 生成 (0 tool use=リポジトリ未参照)。
// F-P10 の v1 生成物は ToPixelBbox の丸めを AwayFromZero にして実装 (ToEven) と中間値で発散したが、
// v2 spec で丸めモードを補填した結果、本生成物は MidpointRounding.ToEven を採用し実装と収束する。
// 検証: src/ViewGrid.Core/Entities/CropFraction.cs へ一時 swap → 全スイート + midpoint oracle green → revert。
namespace ViewGrid.Core.Entities;

/// <summary>
/// 実効的なクロップ bbox の比率 (0–1)。AutoCrop/ManualCrop 双方からの変換先で、
/// 源を意識せず使える統一型。座標系非依存。
/// </summary>
public readonly record struct CropFraction(double X, double Y, double Width, double Height)
{
    /// <summary>クロップ無効 (全領域) のセンチネル。</summary>
    public static CropFraction Full => new(0, 0, 1, 1);

    /// <summary>
    /// X,Y がともに 0 近傍、かつ Width,Height がともに 1.0 近傍のとき true (= クロップ無効)。
    /// </summary>
    public bool IsFull(double tolerance = 1e-6) =>
        System.Math.Abs(X) < tolerance &&
        System.Math.Abs(Y) < tolerance &&
        System.Math.Abs(Width - 1.0) < tolerance &&
        System.Math.Abs(Height - 1.0) < tolerance;

    /// <summary>
    /// 比率を整数ピクセル bbox へ展開する。丸めは ToEven (銀行家丸め)。
    /// w/h の上限は残り (width-x / height-y) で、画像外へはみ出さない。
    /// precondition: width/height は正の整数。
    /// </summary>
    public (int X, int Y, int Width, int Height) ToPixelBbox(int width, int height)
    {
        int x = Clamp(RoundToEven(X * width), 0, width);
        int y = Clamp(RoundToEven(Y * height), 0, height);
        int w = Clamp(RoundToEven(Width * width), 0, width - x);
        int h = Clamp(RoundToEven(Height * height), 0, height - y);
        return (x, y, w, h);
    }

    /// <summary>AutoCropFraction からの写像。</summary>
    public static CropFraction From(AutoCropFraction f) =>
        new(f.X, f.Y, f.Width, f.Height);

    /// <summary>ManualCropFraction からの写像。</summary>
    public static CropFraction From(ManualCropFraction f) =>
        new(f.X, f.Y, f.Width, f.Height);

    private static int RoundToEven(double value) =>
        (int)System.Math.Round(value, System.MidpointRounding.ToEven);

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
