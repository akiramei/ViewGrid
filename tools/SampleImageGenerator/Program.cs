using SkiaSharp;

// ViewGrid マニュアル用サンプル画像生成スクリプト。
//
// 出力: docs/sample-images/
//   Set A (1200×1200, 8 枚): sample-01.png 〜 sample-08.png — 識別画像
//   Set B (アスペクト比違い, 4 枚): aspect-landscape/portrait/square/pano.png
//   Set C (1600×1600, 3 枚): autocrop-white/black/transparent.png — AutoCrop 対象
//   Set D (1200×1200, 1 枚): rotation-demo.png — 回転 / 反転 demo
//   Set E (2 枚): region-speech.png, region-label.png — ProtectedRegion demo
//   Set F (1600×1200, 4 枚): photo-01.png 〜 photo-04.png — PhotoBoard 用
//
// 共通テンプレ (Set A〜E):
//   - パステル背景 + 太い外枠 (12px、 アクセント色)
//   - 100px 間隔の grid 線 (薄いアクセント色、 PixelOffset / Scaling 観察用)
//   - 4 隅マーカー (TL/TR/BL/BR 100×100、 Alignment / Rotation / Flip 観察用)
//   - 中央: 大きな番号 / ラベル / 寸法テキスト
//
// Set F は写真風 (フレーム + 影が映えるよう grid 線なし、 グラデ + ノイズ)。
//
// 使い方: dotnet run --project tools/SampleImageGenerator (リポジトリ root から)

var outputDir = Path.GetFullPath("docs/sample-images");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output: {outputDir}");

// === 共通テンプレ用カラーパレット (背景パステル + アクセント原色) ===
var palette = new (SKColor Bg, SKColor Accent, string Hue)[]
{
    (new(0xFE, 0xE2, 0xE2), new(0xDC, 0x26, 0x26), "Red"),
    (new(0xFC, 0xE7, 0xF3), new(0xDB, 0x27, 0x77), "Pink"),
    (new(0xDB, 0xEA, 0xFE), new(0x25, 0x63, 0xEB), "Blue"),
    (new(0xCF, 0xFA, 0xFE), new(0x06, 0xB6, 0xD4), "Cyan"),
    (new(0xD1, 0xFA, 0xE5), new(0x05, 0x96, 0x69), "Green"),
    (new(0xEC, 0xFC, 0xCB), new(0x65, 0xA3, 0x0D), "Lime"),
    (new(0xFE, 0xD7, 0xAA), new(0xEA, 0x58, 0x0C), "Orange"),
    (new(0xFE, 0xF3, 0xC7), new(0xCA, 0x8A, 0x04), "Amber"),
};

// === Set A: 番号入り識別画像 (8 枚、 1200×1200) ===
Console.WriteLine("\n[Set A] 識別画像 (8 枚)");
for (int i = 0; i < 8; i++)
{
    var (bg, accent, _) = palette[i];
    var path = Path.Combine(outputDir, $"sample-{i + 1:D2}.png");
    GenerateTemplate(path, 1200, 1200, bg, accent,
        centerText: $"{i + 1:D2}",
        labelText: $"sample-{i + 1:D2}",
        dimText: "1200 × 1200");
    Console.WriteLine($"  {Path.GetFileName(path)}");
}

// === Set B: アスペクト比違い (4 枚) ===
Console.WriteLine("\n[Set B] アスペクト比違い (4 枚)");
var aspectSet = new (string Name, int W, int H, string Ratio)[]
{
    ("landscape", 1920, 1080, "16:9"),
    ("portrait", 1080, 1920, "9:16"),
    ("square", 1200, 1200, "1:1"),
    ("pano", 2400, 800, "3:1"),
};
var (bgB, accentB, _) = palette[2]; // Blue
foreach (var (name, w, h, ratio) in aspectSet)
{
    var path = Path.Combine(outputDir, $"aspect-{name}.png");
    GenerateTemplate(path, w, h, bgB, accentB,
        centerText: ratio,
        labelText: $"aspect-{name}",
        dimText: $"{w} × {h}");
    Console.WriteLine($"  {Path.GetFileName(path)}");
}

// === Set C: AutoCrop 対象 (3 枚、 1600×1600) ===
Console.WriteLine("\n[Set C] AutoCrop 対象 (3 枚)");
GenerateAutoCropTarget(Path.Combine(outputDir, "autocrop-white.png"),
    borderColor: SKColors.White, borderName: "white", paletteIndex: 4); // Green inner
GenerateAutoCropTarget(Path.Combine(outputDir, "autocrop-black.png"),
    borderColor: SKColors.Black, borderName: "black", paletteIndex: 2); // Blue inner
GenerateAutoCropTarget(Path.Combine(outputDir, "autocrop-transparent.png"),
    borderColor: SKColors.Transparent, borderName: "transparent", paletteIndex: 6); // Orange inner

// === Set D: 回転 / 反転 demo (1 枚、 1200×1200) ===
Console.WriteLine("\n[Set D] 回転 / 反転 demo");
GenerateRotationDemo(Path.Combine(outputDir, "rotation-demo.png"));

// === Set E: ProtectedRegion demo (2 枚) ===
Console.WriteLine("\n[Set E] 保護領域 demo (2 枚)");
GenerateRegionSpeech(Path.Combine(outputDir, "region-speech.png"));
GenerateRegionLabel(Path.Combine(outputDir, "region-label.png"));

// === Set F: PhotoBoard 用 (4 枚、 1600×1200) ===
Console.WriteLine("\n[Set F] PhotoBoard 用 (4 枚)");
GeneratePhotoLike(Path.Combine(outputDir, "photo-01.png"),
    top: new SKColor(0xFB, 0x72, 0x40), middle: new SKColor(0xE1, 0x1D, 0x48), bottom: new SKColor(0x5B, 0x21, 0x86),
    label: "sunset");
GeneratePhotoLike(Path.Combine(outputDir, "photo-02.png"),
    top: new SKColor(0x1E, 0x40, 0xAF), middle: new SKColor(0x06, 0xB6, 0xD4), bottom: new SKColor(0xBA, 0xE6, 0xFD),
    label: "ocean");
GeneratePhotoLike(Path.Combine(outputDir, "photo-03.png"),
    top: new SKColor(0x14, 0x53, 0x2D), middle: new SKColor(0x16, 0xA3, 0x4A), bottom: new SKColor(0xD9, 0xF9, 0x9D),
    label: "forest");
GeneratePhotoLike(Path.Combine(outputDir, "photo-04.png"),
    top: new SKColor(0x1E, 0x29, 0x3B), middle: new SKColor(0x64, 0x74, 0x8B), bottom: new SKColor(0xFC, 0xA5, 0xA5),
    label: "city");

Console.WriteLine($"\n✅ Done. Generated 20 sample images.");
return 0;

// ───────────────────────── helper methods ─────────────────────────

// 共通テンプレ: 背景 + 外枠 + grid 線 + 4 隅マーカー + 中央テキスト
static void GenerateTemplate(string path, int w, int h, SKColor bg, SKColor accent,
    string centerText, string labelText, string dimText)
{
    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
    if (surface is null) throw new InvalidOperationException("SKSurface 作成失敗");
    var canvas = surface.Canvas;
    canvas.Clear(bg);

    DrawTemplateChrome(canvas, w, h, accent);
    DrawCenterLabels(canvas, w, h, accent, centerText, labelText, dimText);

    SaveImage(surface, path);
}

// 外枠 + grid 線 + 4 隅マーカーをまとめて描く
static void DrawTemplateChrome(SKCanvas canvas, int w, int h, SKColor accent)
{
    // 外枠 (12px 太線、 矩形ストローク)
    using (var paint = new SKPaint { Color = accent, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12f })
    {
        canvas.DrawRect(6, 6, w - 12, h - 12, paint);
    }

    // grid 線 (100px 間隔、 薄いアクセント色)
    var gridColor = accent.WithAlpha(50);
    using (var paint = new SKPaint { Color = gridColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = false })
    {
        for (int x = 100; x < w; x += 100)
            canvas.DrawLine(x, 12, x, h - 12, paint);
        for (int y = 100; y < h; y += 100)
            canvas.DrawLine(12, y, w - 12, y, paint);
    }

    // 50px 補助線 (さらに薄く、 ピクセル微調整時の視認性を上げる)
    var subGridColor = accent.WithAlpha(20);
    using (var paint = new SKPaint { Color = subGridColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = false })
    {
        for (int x = 50; x < w; x += 100)
            canvas.DrawLine(x, 12, x, h - 12, paint);
        for (int y = 50; y < h; y += 100)
            canvas.DrawLine(12, y, w - 12, y, paint);
    }

    // 4 隅マーカー
    const float markerSize = 100f;
    const float markerMargin = 30f;
    DrawCornerMarker(canvas, markerMargin, markerMargin, markerSize, accent, "TL");
    DrawCornerMarker(canvas, w - markerMargin - markerSize, markerMargin, markerSize, accent, "TR");
    DrawCornerMarker(canvas, markerMargin, h - markerMargin - markerSize, markerSize, accent, "BL");
    DrawCornerMarker(canvas, w - markerMargin - markerSize, h - markerMargin - markerSize, markerSize, accent, "BR");
}

// 中央: 大きな番号 + ラベル + 寸法テキスト
static void DrawCenterLabels(SKCanvas canvas, int w, int h, SKColor accent,
    string centerText, string labelText, string dimText)
{
    var typefaceBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    var typefaceRegular = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal) ?? SKTypeface.Default;

    // 中央番号 (画像短辺の 22%)
    float centerSize = Math.Min(w, h) * 0.22f;
    using (var font = new SKFont(typefaceBold, centerSize))
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        DrawCenteredText(canvas, centerText, w / 2f, h / 2f, font, paint);
    }

    // ラベル (中央番号の下、 短辺の 4%)
    using (var font = new SKFont(typefaceRegular, Math.Min(w, h) * 0.04f))
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        DrawCenteredText(canvas, labelText, w / 2f, h / 2f + centerSize * 0.55f, font, paint);
    }

    // 寸法テキスト (画像下部、 32px)
    using (var font = new SKFont(typefaceRegular, 32f))
    using (var paint = new SKPaint { Color = accent.WithAlpha(180), IsAntialias = true })
    {
        DrawCenteredText(canvas, dimText, w / 2f, h - 70, font, paint);
    }

    typefaceBold.Dispose();
    typefaceRegular.Dispose();
}

// 4 隅マーカー: アクセント色矩形 + 白文字 (TL/TR/BL/BR)
static void DrawCornerMarker(SKCanvas canvas, float x, float y, float size, SKColor accent, string label)
{
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        canvas.DrawRect(x, y, size, size, paint);
    }

    var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    using (var font = new SKFont(typeface, size * 0.38f))
    using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        DrawCenteredText(canvas, label, x + size / 2f, y + size / 2f, font, paint);
    }
    typeface.Dispose();
}

// テキストを (cx, cy) 中心に描画
static void DrawCenteredText(SKCanvas canvas, string text, float cx, float cy, SKFont font, SKPaint paint)
{
    font.MeasureText(text, out var bounds);
    float baseline = cy - bounds.MidY;
    canvas.DrawText(text, cx, baseline, SKTextAlign.Center, font, paint);
}

// === Set C: AutoCrop 対象 (中央に Set A 風テンプレ + 外周 200px の単色余白) ===
static void GenerateAutoCropTarget(string path, SKColor borderColor, string borderName, int paletteIndex)
{
    const int outer = 1600;
    const int border = 200;
    const int inner = outer - 2 * border;

    using var surface = SKSurface.Create(new SKImageInfo(outer, outer, SKColorType.Rgba8888, SKAlphaType.Premul));
    if (surface is null) throw new InvalidOperationException("SKSurface 作成失敗");
    var canvas = surface.Canvas;

    // 外周: 余白色で塗りつぶし (Transparent はそのままクリアして α=0)
    if (borderColor == SKColors.Transparent)
        canvas.Clear(SKColors.Transparent);
    else
        canvas.Clear(borderColor);

    // 内側矩形: パステル背景
    var palette = new (SKColor Bg, SKColor Accent)[]
    {
        (new(0xFE, 0xE2, 0xE2), new(0xDC, 0x26, 0x26)),
        (new(0xFC, 0xE7, 0xF3), new(0xDB, 0x27, 0x77)),
        (new(0xDB, 0xEA, 0xFE), new(0x25, 0x63, 0xEB)),
        (new(0xCF, 0xFA, 0xFE), new(0x06, 0xB6, 0xD4)),
        (new(0xD1, 0xFA, 0xE5), new(0x05, 0x96, 0x69)),
        (new(0xEC, 0xFC, 0xCB), new(0x65, 0xA3, 0x0D)),
        (new(0xFE, 0xD7, 0xAA), new(0xEA, 0x58, 0x0C)),
        (new(0xFE, 0xF3, 0xC7), new(0xCA, 0x8A, 0x04)),
    };
    var (bg, accent) = palette[paletteIndex];

    using (var paint = new SKPaint { Color = bg, IsAntialias = false })
    {
        canvas.DrawRect(border, border, inner, inner, paint);
    }

    // 内側矩形領域に template chrome を描画 (オフセット付き)
    canvas.Save();
    canvas.Translate(border, border);
    DrawTemplateChromeInner(canvas, inner, inner, accent);

    var typefaceBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    var typefaceRegular = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal) ?? SKTypeface.Default;

    using (var font = new SKFont(typefaceBold, inner * 0.18f))
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        DrawCenteredText(canvas, "CROP", inner / 2f, inner / 2f, font, paint);
    }

    using (var font = new SKFont(typefaceRegular, inner * 0.04f))
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        DrawCenteredText(canvas, $"autocrop-{borderName}", inner / 2f, inner / 2f + inner * 0.13f, font, paint);
        DrawCenteredText(canvas, $"outer border: {borderName}", inner / 2f, inner / 2f + inner * 0.18f, font, paint);
    }

    typefaceBold.Dispose();
    typefaceRegular.Dispose();
    canvas.Restore();

    SaveImage(surface, path);
    Console.WriteLine($"  {Path.GetFileName(path)}");
}

// AutoCrop 用の内側 chrome (外枠不要 = AutoCrop で検出される 「subject の外周」 が画像の外枠を持ってはいけないため)
static void DrawTemplateChromeInner(SKCanvas canvas, int w, int h, SKColor accent)
{
    // grid 線 (100px 間隔)
    var gridColor = accent.WithAlpha(50);
    using (var paint = new SKPaint { Color = gridColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = false })
    {
        for (int x = 100; x < w; x += 100)
            canvas.DrawLine(x, 0, x, h, paint);
        for (int y = 100; y < h; y += 100)
            canvas.DrawLine(0, y, w, y, paint);
    }

    // 50px 補助線
    var subGridColor = accent.WithAlpha(20);
    using (var paint = new SKPaint { Color = subGridColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = false })
    {
        for (int x = 50; x < w; x += 100)
            canvas.DrawLine(x, 0, x, h, paint);
        for (int y = 50; y < h; y += 100)
            canvas.DrawLine(0, y, w, y, paint);
    }

    // 4 隅マーカー (内側矩形の隅に配置)
    const float markerSize = 90f;
    const float markerMargin = 24f;
    DrawCornerMarker(canvas, markerMargin, markerMargin, markerSize, accent, "TL");
    DrawCornerMarker(canvas, w - markerMargin - markerSize, markerMargin, markerSize, accent, "TR");
    DrawCornerMarker(canvas, markerMargin, h - markerMargin - markerSize, markerSize, accent, "BL");
    DrawCornerMarker(canvas, w - markerMargin - markerSize, h - markerMargin - markerSize, markerSize, accent, "BR");
}

// === Set D: 回転 / 反転 demo (4 象限を強い原色 + 中央に大きな ↑ + 「TOP」) ===
static void GenerateRotationDemo(string path)
{
    const int size = 1200;
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    if (surface is null) throw new InvalidOperationException("SKSurface 作成失敗");
    var canvas = surface.Canvas;

    var quadrants = new (SKColor Color, string Label, float X, float Y)[]
    {
        (new(0xDC, 0x26, 0x26), "TL", 0, 0),                  // 赤
        (new(0x25, 0x63, 0xEB), "TR", size / 2f, 0),          // 青
        (new(0x05, 0x96, 0x69), "BL", 0, size / 2f),          // 緑
        (new(0xCA, 0x8A, 0x04), "BR", size / 2f, size / 2f),  // 黄
    };

    // 4 象限の塗りつぶし
    foreach (var (color, _, x, y) in quadrants)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = false };
        canvas.DrawRect(x, y, size / 2f, size / 2f, paint);
    }

    // 象限ラベル (各象限の中央近く)
    var typefaceBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    foreach (var (_, label, x, y) in quadrants)
    {
        using var font = new SKFont(typefaceBold, 130f);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true };
        // ラベルは各象限の外側寄り (TL なら左上寄り) に配置
        float cx = x + size / 4f;
        float cy = y + size / 4f;
        if (label == "TL") { cx = x + size * 0.13f; cy = y + size * 0.13f; }
        if (label == "TR") { cx = x + size * 0.37f; cy = y + size * 0.13f; }
        if (label == "BL") { cx = x + size * 0.13f; cy = y + size * 0.37f; }
        if (label == "BR") { cx = x + size * 0.37f; cy = y + size * 0.37f; }
        DrawCenteredText(canvas, label, cx, cy, font, paint);
    }

    // grid 線 (薄い白、 PixelOffset 視認用)
    using (var paint = new SKPaint { Color = SKColors.White.WithAlpha(30), Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = false })
    {
        for (int x = 100; x < size; x += 100)
            canvas.DrawLine(x, 0, x, size, paint);
        for (int y = 100; y < size; y += 100)
            canvas.DrawLine(0, y, size, y, paint);
    }

    // 中央に白円 + 上向き矢印 + 「TOP」 テキスト
    float cx0 = size / 2f, cy0 = size / 2f;
    using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        canvas.DrawCircle(cx0, cy0, 220, paint);
    }
    using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f })
    {
        canvas.DrawCircle(cx0, cy0, 220, paint);
    }

    // 上向き矢印 ↑
    using (var path2 = new SKPath())
    using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
    {
        path2.MoveTo(cx0, cy0 - 130);             // 上の頂点
        path2.LineTo(cx0 + 60, cy0 - 50);         // 右下
        path2.LineTo(cx0 + 25, cy0 - 50);         // 右内側
        path2.LineTo(cx0 + 25, cy0 + 110);        // 右脚下
        path2.LineTo(cx0 - 25, cy0 + 110);        // 左脚下
        path2.LineTo(cx0 - 25, cy0 - 50);         // 左内側
        path2.LineTo(cx0 - 60, cy0 - 50);         // 左下
        path2.Close();
        canvas.DrawPath(path2, paint);
    }

    // 「TOP」 テキスト (画像上端中央付近)
    using (var font = new SKFont(typefaceBold, 64f))
    using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        DrawCenteredText(canvas, "TOP", size / 2f, 80, font, paint);
    }

    // 「rotation-demo」 ラベル (画像下部)
    using (var font = new SKFont(typefaceBold, 40f))
    using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        DrawCenteredText(canvas, "rotation-demo", size / 2f, size - 60, font, paint);
    }

    typefaceBold.Dispose();
    SaveImage(surface, path);
    Console.WriteLine($"  {Path.GetFileName(path)}");
}

// === Set E-1: 吹き出し風 region (右上に矩形 + 「セリフ」 テキスト) ===
static void GenerateRegionSpeech(string path)
{
    const int w = 1600, h = 900;
    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
    if (surface is null) throw new InvalidOperationException("SKSurface 作成失敗");
    var canvas = surface.Canvas;
    var accent = new SKColor(0x25, 0x63, 0xEB); // Blue accent

    canvas.Clear(new SKColor(0xDB, 0xEA, 0xFE));
    DrawTemplateChrome(canvas, w, h, accent);

    // 中央右上: 吹き出し矩形領域 (青背景)
    var bubbleRect = new SKRect(w * 0.55f, h * 0.12f, w - 80, h * 0.42f);
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        canvas.DrawRoundRect(bubbleRect, 20f, 20f, paint);
    }
    var typefaceBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    using (var font = new SKFont(typefaceBold, 90f))
    using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        DrawCenteredText(canvas, "REGION", bubbleRect.MidX, bubbleRect.MidY - 25, font, paint);
    }
    using (var font = new SKFont(typefaceBold, 36f))
    using (var paint = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true })
    {
        DrawCenteredText(canvas, "Speech / Caption", bubbleRect.MidX, bubbleRect.MidY + 45, font, paint);
    }

    // 中央: ラベル
    using (var font = new SKFont(typefaceBold, 90f))
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        DrawCenteredText(canvas, "region-speech", w / 2f, h * 0.7f, font, paint);
    }

    typefaceBold.Dispose();
    SaveImage(surface, path);
    Console.WriteLine($"  {Path.GetFileName(path)}");
}

// === Set E-2: ロゴ風 region (左下に矩形 + 「LABEL」 テキスト) ===
static void GenerateRegionLabel(string path)
{
    const int size = 1200;
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    if (surface is null) throw new InvalidOperationException("SKSurface 作成失敗");
    var canvas = surface.Canvas;
    var accent = new SKColor(0xEA, 0x58, 0x0C); // Orange accent

    canvas.Clear(new SKColor(0xFE, 0xD7, 0xAA));
    DrawTemplateChrome(canvas, size, size, accent);

    // 左下: ロゴ矩形領域 (オレンジ背景)
    var labelRect = new SKRect(80, size * 0.6f, size * 0.45f, size - 80);
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        canvas.DrawRoundRect(labelRect, 16f, 16f, paint);
    }
    var typefaceBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    using (var font = new SKFont(typefaceBold, 110f))
    using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
    {
        DrawCenteredText(canvas, "LABEL", labelRect.MidX, labelRect.MidY - 25, font, paint);
    }
    using (var font = new SKFont(typefaceBold, 36f))
    using (var paint = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true })
    {
        DrawCenteredText(canvas, "Logo / Badge", labelRect.MidX, labelRect.MidY + 55, font, paint);
    }

    // 中央: ラベル
    using (var font = new SKFont(typefaceBold, 90f))
    using (var paint = new SKPaint { Color = accent, IsAntialias = true })
    {
        DrawCenteredText(canvas, "region-label", size / 2f, size * 0.38f, font, paint);
    }

    typefaceBold.Dispose();
    SaveImage(surface, path);
    Console.WriteLine($"  {Path.GetFileName(path)}");
}

// === Set F: 写真風画像 (グラデーション + Perlin ノイズ + 小さなラベル) ===
static void GeneratePhotoLike(string path, SKColor top, SKColor middle, SKColor bottom, string label)
{
    const int w = 1600, h = 1200;
    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
    if (surface is null) throw new InvalidOperationException("SKSurface 作成失敗");
    var canvas = surface.Canvas;

    // 縦方向のリニアグラデーション (top → middle → bottom)
    using (var paint = new SKPaint { IsAntialias = true })
    using (var shader = SKShader.CreateLinearGradient(
        new SKPoint(w / 2f, 0),
        new SKPoint(w / 2f, h),
        new[] { top, middle, bottom },
        new[] { 0f, 0.55f, 1f },
        SKShaderTileMode.Clamp))
    {
        paint.Shader = shader;
        canvas.DrawRect(0, 0, w, h, paint);
    }

    // 中央付近に放射状のソフトな光を追加
    using (var paint = new SKPaint { IsAntialias = true })
    using (var shader = SKShader.CreateRadialGradient(
        new SKPoint(w / 2f, h * 0.4f),
        h * 0.8f,
        new[] { SKColors.White.WithAlpha(80), SKColors.Transparent },
        SKShaderTileMode.Clamp))
    {
        paint.Shader = shader;
        canvas.DrawRect(0, 0, w, h, paint);
    }

    // Perlin ノイズオーバーレイ (実写感)
    using (var paint = new SKPaint { IsAntialias = true })
    using (var noiseShader = SKShader.CreatePerlinNoiseFractalNoise(0.012f, 0.012f, 3, 0))
    {
        paint.Shader = noiseShader;
        paint.BlendMode = SKBlendMode.Multiply;
        // ノイズを薄く乗せる
        paint.Color = SKColors.White.WithAlpha(60);
        canvas.DrawRect(0, 0, w, h, paint);
    }

    // 右下にラベル (小さく、 半透明白)
    var typefaceBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    using (var font = new SKFont(typefaceBold, 42f))
    using (var paint = new SKPaint { Color = SKColors.White.WithAlpha(200), IsAntialias = true })
    {
        canvas.DrawText($"photo-{label}", w - 40, h - 40, SKTextAlign.Right, font, paint);
    }

    typefaceBold.Dispose();
    SaveImage(surface, path);
    Console.WriteLine($"  {Path.GetFileName(path)}");
}

// 共通: PNG 保存
static void SaveImage(SKSurface surface, string path)
{
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(path);
    data.SaveTo(stream);
}
