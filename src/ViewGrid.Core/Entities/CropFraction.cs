namespace ViewGrid.Core.Entities;

/// <summary>
/// 「実効的なクロップ bbox」の比率（0–1）。<see cref="AutoCropFraction"/>（自動）と
/// <see cref="ManualCropFraction"/>（手動）の両方からの変換先として、Renderer / View / Use case が
/// クロップの源（自動/手動）を意識せず使える統一型。
/// <para>
/// すべて 0.0–1.0。<c>(0, 0, 1, 1)</c> はクロップ無効（全領域）。
/// 適用順序は <c>元画像 → CropFraction → 回転・反転 → ScalingMode/Alignment</c>（元画像座標系で完結）。
/// </para>
/// </summary>
public readonly record struct CropFraction(double X, double Y, double Width, double Height)
{
    /// <summary>クロップ無効（全領域）のセンチネル値。</summary>
    public static CropFraction Full { get; } = new(0.0, 0.0, 1.0, 1.0);

    /// <summary>fraction を整数ピクセル bbox に展開する（軸サイズで掛けて round）。</summary>
    public (int X, int Y, int Width, int Height) ToPixelBbox(int width, int height)
    {
        var x = (int)System.Math.Clamp(System.Math.Round(X * width), 0, width);
        var y = (int)System.Math.Clamp(System.Math.Round(Y * height), 0, height);
        var w = (int)System.Math.Clamp(System.Math.Round(Width * width), 0, width - x);
        var h = (int)System.Math.Clamp(System.Math.Round(Height * height), 0, height - y);
        return (x, y, w, h);
    }

    /// <summary>クロップ無効（fullness 1.0 に近い）か。</summary>
    public bool IsFull(double tolerance = 1e-6) =>
        System.Math.Abs(X) < tolerance && System.Math.Abs(Y) < tolerance
        && System.Math.Abs(Width - 1.0) < tolerance && System.Math.Abs(Height - 1.0) < tolerance;

    /// <summary>AutoCrop 走査結果から CropFraction を作る。</summary>
    public static CropFraction From(AutoCropFraction f) => new(f.X, f.Y, f.Width, f.Height);

    /// <summary>ManualCrop 設定から CropFraction を作る。</summary>
    public static CropFraction From(ManualCropFraction f) => new(f.X, f.Y, f.Width, f.Height);

    /// <summary>
    /// 実効クロップの優先規則 (R-08) の<b>唯一の源</b>。<c>ManualCrop を排他優先 → AutoCrop → null</c>。
    /// いずれも <see cref="IsFull"/> (クロップ無効 = 全領域) なら <c>null</c> を返す。
    /// <para>
    /// AutoCrop の I/O・ピクセル走査は呼出側が済ませた <paramref name="auto"/> を渡す（本メソッドは純粋: I/O を持たない）。
    /// ManualCrop が非 null の時点で <paramref name="auto"/> は<b>一切参照しない</b>（排他・短絡）。
    /// <see cref="ViewGrid.Core.Services.IImageCropResolver"/> の標準実装 / CopyPropertiesViewModel の
    /// 編集プレビュー / SkiaGridImageRenderer の出力が、この 1 関数に優先判定を委譲することで drift を防ぐ。
    /// </para>
    /// </summary>
    /// <param name="manual">ManualCrop 比率（OFF / 未設定なら null）。</param>
    /// <param name="auto">AutoCrop 走査結果の比率（OFF / 未走査なら null）。ManualCrop 優先時は無視される。</param>
    /// <returns>実効クロップ比率。クロップ無効（OFF / full / どちらも null）なら null。</returns>
    public static CropFraction? ResolveEffective(ManualCropFraction? manual, AutoCropFraction? auto)
    {
        if (manual is { } m)
        {
            var cf = From(m);
            return cf.IsFull() ? null : cf;
        }
        if (auto is { } a)
        {
            var cf = From(a);
            return cf.IsFull() ? null : cf;
        }
        return null;
    }
}
