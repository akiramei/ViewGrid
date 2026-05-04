using SkiaSharp;

// ViewGrid アプリアイコン生成スクリプト。
//
// 出力: src/ViewGrid.Presentation/Assets/
//   icon-256.png  (Avalonia Window.Icon 用、avares:// 参照)
//   icon.ico      (Windows exe ApplicationIcon 用、PNG-in-ICO 形式 256x256)
//
// 設計:
//   - 背景: アクセント色 (sky-500 #0EA5E9) の角丸正方形
//   - 前景: App.axaml の LayoutGeometry (Material Icons grid_view、Apache 2.0) を白で配置
//   - 24x24 viewBox を 192x192 にスケール、上下左右 32px マージン
//
// 使い方:
//   dotnet run --project tools/IconGenerator
//
// 出力ファイルは src/ViewGrid.Presentation/Assets/ に直接書き込む（リポジトリルート起動を想定）。

const int Size = 256;
const int Margin = 32;
const float CornerRadius = 48f;
const string LayoutSvgPath = "M3 3v8h8V3H3zm6 6H5V5h4v4zm-6 4v8h8v-8H3zm6 6H5v-4h4v4zm4-16v8h8V3h-8zm6 6h-4V5h4v4zm-6 4v8h8v-8h-8zm6 6h-4v-4h4v4z";
var accentColor = new SKColor(0x0E, 0xA5, 0xE9); // sky-500

var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
using var surface = SKSurface.Create(info);
if (surface is null)
{
    Console.Error.WriteLine("SKSurface 作成に失敗しました。");
    return 1;
}

var canvas = surface.Canvas;
canvas.Clear(SKColors.Transparent);

// 背景: 角丸矩形 (アクセント色)
using (var bgPaint = new SKPaint())
{
    bgPaint.Color = accentColor;
    bgPaint.IsAntialias = true;
    canvas.DrawRoundRect(new SKRect(0, 0, Size, Size), CornerRadius, CornerRadius, bgPaint);
}

// 前景: LayoutGeometry (24x24 → 192x192、中央配置)
var path = SKPath.ParseSvgPathData(LayoutSvgPath);
if (path is null)
{
    Console.Error.WriteLine("SVG path のパースに失敗しました。");
    return 1;
}

float scale = (Size - Margin * 2) / 24f; // 192 / 24 = 8
var matrix = SKMatrix.CreateScale(scale, scale);
matrix = matrix.PostConcat(SKMatrix.CreateTranslation(Margin, Margin));
path.Transform(matrix);

using (var fgPaint = new SKPaint())
{
    fgPaint.Color = SKColors.White;
    fgPaint.IsAntialias = true;
    canvas.DrawPath(path, fgPaint);
}

// PNG エンコード (256x256)
using var image = surface.Snapshot();
using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
if (pngData is null)
{
    Console.Error.WriteLine("PNG エンコードに失敗しました。");
    return 1;
}

var assetsDir = Path.Combine("src", "ViewGrid.Presentation", "Assets");
Directory.CreateDirectory(assetsDir);

var pngBytes = pngData.ToArray();
var pngPath = Path.Combine(assetsDir, "icon-256.png");
File.WriteAllBytes(pngPath, pngBytes);
Console.WriteLine($"PNG 書き出し: {pngPath} ({pngBytes.Length:N0} bytes)");

// ICO エンコード (PNG-in-ICO 形式、256x256 単一サイズ)
// ICO ヘッダ: ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes) + PNG データ
//   ICONDIR: reserved=0, type=1 (icon), count=1
//   ICONDIRENTRY: width=0 (=256), height=0 (=256), colors=0, reserved=0,
//                 planes=1, bpp=32, size=PNG bytes, offset=22
var icoPath = Path.Combine(assetsDir, "icon.ico");
using (var icoStream = new FileStream(icoPath, FileMode.Create))
using (var bw = new BinaryWriter(icoStream))
{
    // ICONDIR
    bw.Write((ushort)0); // reserved
    bw.Write((ushort)1); // type (1 = icon)
    bw.Write((ushort)1); // count

    // ICONDIRENTRY
    bw.Write((byte)0); // width (256 represented as 0 in 1-byte field)
    bw.Write((byte)0); // height (同上)
    bw.Write((byte)0); // color count (0 = no palette)
    bw.Write((byte)0); // reserved
    bw.Write((ushort)1); // color planes
    bw.Write((ushort)32); // bits per pixel
    bw.Write((uint)pngBytes.Length); // image size
    bw.Write((uint)22); // image offset (6 + 16)

    // PNG payload
    bw.Write(pngBytes);
}
Console.WriteLine($"ICO 書き出し: {icoPath} ({new FileInfo(icoPath).Length:N0} bytes)");
return 0;
