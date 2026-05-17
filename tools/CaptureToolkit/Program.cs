using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

// ViewGrid マニュアル CAPTURE Toolkit
//
// マニュアル本文の `<!-- CAPTURE ... -->` ブロックを起点に、
//   1. placeholder PNG を生成 (画像に仕様を埋め込む)
//   2. _raw/ 配下のフルウィンドウ撮影を crop してターゲットへ書き出し
//   3. 個別エクスポート PNG を 1 枚の比較合成画像に並べる (compose-scaling / compose-photoboard)
//   4. レビューページ (docs/CAPTURE-REVIEW.md) を生成
//   5. 撮影進捗を集計
// する 6 つのサブコマンド。
//
// 使い方:
//   dotnet run --project tools/CaptureToolkit -- generate-placeholders
//   dotnet run --project tools/CaptureToolkit -- crop-raw
//   dotnet run --project tools/CaptureToolkit -- compose-scaling
//   dotnet run --project tools/CaptureToolkit -- compose-photoboard
//   dotnet run --project tools/CaptureToolkit -- generate-review
//   dotnet run --project tools/CaptureToolkit -- status

// --docs=<root> でドキュメントツリーを切り替える (既定 docs)。
// 日本語マニュアルは --docs=docs、 英語マニュアルは --docs=docs/en。
var positional = new List<string>();
foreach (var arg in args)
{
    if (arg.StartsWith("--docs=", StringComparison.Ordinal))
    {
        var value = arg["--docs=".Length..];
        if (string.IsNullOrWhiteSpace(value))
            Console.Error.WriteLine("⚠️  --docs= の値が空です。 既定の docs を使用します。");
        else
            Commands.DocsRoot = value;
    }
    else
    {
        positional.Add(arg);
    }
}

return positional.ToArray() switch
{
    [] => Commands.PrintHelp(),
    ["help"] or ["-h"] or ["--help"] => Commands.PrintHelp(),
    ["generate-placeholders"] => Commands.GeneratePlaceholders(),
    ["crop-raw"] => Commands.CropRaw(),
    ["compose-scaling"] => Commands.ComposeScaling(),
    ["compose-photoboard"] => Commands.ComposePhotoBoard(),
    ["generate-review"] => Commands.GenerateReview(),
    ["status"] => Commands.Status(),
    [var cmd, ..] => Commands.Unknown(cmd),
};

// === 型定義 ===

internal sealed record CropRect(int X, int Y, int Width, int Height);

internal sealed record CaptureBlock(
    string SourceFile,
    int LineNumber,
    string FilePath,
    string SizeText,
    int Width,
    int Height,
    string Samples,
    string Caption,
    string Note,
    IReadOnlyList<string> State,
    CropRect? Crop);

internal enum ImageStatus { Missing, Placeholder, Replaced }

// === コマンド群 ===

internal static class Commands
{
    /// <summary>
    /// ドキュメントツリーのルート。 <c>--docs=&lt;root&gt;</c> で切り替える (既定 <c>docs</c>)。
    /// 日本語マニュアルは <c>docs</c>、 英語マニュアルは <c>docs/en</c>。
    /// 配下の md / images / CAPTURE-REVIEW.md / manifest はすべてこのルート相対で解決する。
    /// </summary>
    public static string DocsRoot { get; set; } = "docs";

    private static string RawRoot => $"{DocsRoot}/images/_raw";
    private static string ManifestPath => $"{DocsRoot}/images/.placeholder-manifest.json";
    private static string ReviewPath => $"{DocsRoot}/CAPTURE-REVIEW.md";

    /// <summary>
    /// docs ルート末尾セグメントから言語を判定する (<c>en</c> なら英語、 それ以外は日本語)。
    /// compose のラベルやレビューのチェックリスト文言の出し分けに使う。
    /// </summary>
    private static string CurrentLanguage =>
        string.Equals(
            new DirectoryInfo(Path.GetFullPath(DocsRoot)).Name, "en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "ja";

    public static int PrintHelp()
    {
        Console.WriteLine("""
            ViewGrid CAPTURE Toolkit

            Usage:
              dotnet run --project tools/CaptureToolkit -- [--docs=<root>] <subcommand>

            Subcommands:
              generate-placeholders   各 <!-- CAPTURE --> から仕様埋め込み placeholder PNG を生成
              crop-raw                docs/images/_raw/ 配下の素材を crop してターゲットへ書き出し
              compose-scaling         スケーリング 6 モードの個別 PNG を 3×2 合成 (um-05-11-scaling-modes)
              compose-photoboard      PhotoBoard 3 スタイルの個別 PNG を 3×1 合成 (um-06-16-photoboard-styles)
              generate-review         docs/CAPTURE-REVIEW.md を再生成 (画像 + 仕様 + レビューチェックリスト)
              status                  撮影進捗を集計表示 (Placeholder のまま / 撮影済 / 未生成)
              help                    このヘルプを表示

            Notes:
              - リポジトリ root から実行すること (`docs/` を相対パスで参照)。
              - --docs=<root>: 対象ドキュメントツリー (既定 docs)。 英語マニュアルは --docs=docs/en。
                images / _raw / CAPTURE-REVIEW.md / manifest はすべてこのルート配下で解決する。
              - 状態判定は docs/images/.placeholder-manifest.json (placeholder SHA 記録) を使う。
                placeholder と SHA 一致 → ❌ Placeholder のまま
                placeholder と SHA 不一致 → ✅ 撮影済 (実画像で上書きされた)
                ファイル無し → ⚠️ 未生成 (generate-placeholders を実行する必要あり)
              - crop-raw: docs/images/_raw/<chapter>/<name>.png を読み、
                CAPTURE ブロックに `crop: x,y,w,h` があれば適用 (無ければ raw 全面コピー) して
                docs/images/<chapter>/<name>.png に書き出します。 raw は再 crop のため残ります。
              - compose-scaling: docs/images/_raw/composites/scaling-modes/ の 6 PNG を
                ファイル名昇順に並べ 3×2 グリッドへ合成。 想定ファイル名:
                  1-None.png / 2-UniformContain.png / 3-ShrinkOnly.png
                  4-EnlargeOnly.png / 5-UniformCover.png / 6-Fill.png
              - compose-photoboard: docs/images/_raw/composites/photoboard-styles/ の 3 PNG を
                ファイル名昇順に並べ 3×1 グリッドへ合成。 想定ファイル名:
                  1-Natural.png / 2-Rough.png / 3-Scattered.png
            """);
        return 0;
    }

    public static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown subcommand: {cmd}\n");
        PrintHelp();
        return 1;
    }

    public static int GeneratePlaceholders()
    {
        var blocks = CaptureParser.ParseAllBlocks(DocsRoot);
        Console.WriteLine($"Found {blocks.Count} CAPTURE blocks. Generating placeholders...\n");

        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in blocks)
        {
            var fullPath = Path.GetFullPath(b.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            PlaceholderRenderer.Render(b, fullPath);
            manifest[NormalizePath(b.FilePath)] = ComputeSha(fullPath);
            Console.WriteLine($"  {b.FilePath} ({b.Width}×{b.Height})");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ManifestPath))!);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"\n✅ Generated {blocks.Count} placeholders");
        Console.WriteLine($"   Manifest: {ManifestPath}");
        return 0;
    }

    public static int GenerateReview()
    {
        var blocks = CaptureParser.ParseAllBlocks(DocsRoot);
        var manifest = LoadManifest();

        int total = blocks.Count;
        int taken = 0, placeholder = 0, missing = 0;
        foreach (var b in blocks)
        {
            switch (GetStatus(b.FilePath, manifest))
            {
                case ImageStatus.Replaced: taken++; break;
                case ImageStatus.Placeholder: placeholder++; break;
                case ImageStatus.Missing: missing++; break;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("# CAPTURE Review (自動生成)");
        sb.AppendLine();
        sb.AppendLine("> このページは `tools/CaptureToolkit -- generate-review` で再生成されます。 手動編集しないこと。");
        sb.AppendLine("> 撮影フェーズ完了後、 placeholder を実画像で上書き → 本ページを再生成 → ✅ 状態を確認してください。");
        sb.AppendLine();
        sb.AppendLine("## 進捗サマリ");
        sb.AppendLine();
        sb.AppendLine($"- 合計: **{total}** 件");
        sb.AppendLine($"- ✅ 撮影済 (placeholder 上書き済): **{taken}**");
        sb.AppendLine($"- ❌ Placeholder のまま: **{placeholder}**");
        if (missing > 0)
        {
            sb.AppendLine($"- ⚠️ 未生成 (placeholder ファイル無し): **{missing}** ← `generate-placeholders` を実行");
        }
        sb.AppendLine();

        var grouped = blocks.GroupBy(b => b.SourceFile).OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in grouped)
        {
            var relPath = Path.GetRelativePath(DocsRoot, group.Key).Replace('\\', '/');
            sb.AppendLine($"## `{relPath}`");
            sb.AppendLine();
            foreach (var b in group.OrderBy(b => b.LineNumber))
            {
                AppendBlock(sb, b, manifest, relPath);
            }
        }

        File.WriteAllText(ReviewPath, sb.ToString());
        Console.WriteLine($"✅ Generated: {ReviewPath}");
        Console.WriteLine($"   Total: {total} | ✅ {taken} | ❌ {placeholder} | ⚠️ {missing}");
        return 0;
    }

    public static int Status()
    {
        var blocks = CaptureParser.ParseAllBlocks(DocsRoot);
        var manifest = LoadManifest();

        Console.WriteLine("CAPTURE Status");
        Console.WriteLine("==============\n");

        var grouped = blocks.GroupBy(b => b.SourceFile).OrderBy(g => g.Key, StringComparer.Ordinal);
        int totalTaken = 0, totalPlaceholder = 0, totalMissing = 0;
        foreach (var group in grouped)
        {
            var relPath = Path.GetRelativePath(".", group.Key).Replace('\\', '/');
            int taken = 0, placeholder = 0, missing = 0;
            foreach (var b in group)
            {
                switch (GetStatus(b.FilePath, manifest))
                {
                    case ImageStatus.Replaced: taken++; break;
                    case ImageStatus.Placeholder: placeholder++; break;
                    case ImageStatus.Missing: missing++; break;
                }
            }
            Console.WriteLine($"  {relPath}  ({group.Count()})");
            Console.WriteLine($"    ✅ {taken,2}    ❌ {placeholder,2}    ⚠️  {missing,2}");
            totalTaken += taken;
            totalPlaceholder += placeholder;
            totalMissing += missing;
        }

        Console.WriteLine();
        Console.WriteLine($"Total: {blocks.Count} captures");
        Console.WriteLine($"  ✅ Screenshots taken    : {totalTaken,3}");
        Console.WriteLine($"  ❌ Placeholder unchanged: {totalPlaceholder,3}");
        Console.WriteLine($"  ⚠️  Missing              : {totalMissing,3}");

        return 0;
    }


    public static int CropRaw()
    {
        var blocks = CaptureParser.ParseAllBlocks(DocsRoot);
        var blockByTarget = blocks.ToDictionary(b => NormalizePath(b.FilePath), b => b, StringComparer.Ordinal);

        var rawRootFull = Path.GetFullPath(RawRoot);
        if (!Directory.Exists(rawRootFull))
        {
            Console.WriteLine($"⚠️  Raw directory not found: {RawRoot}");
            Console.WriteLine($"    Drop full-window screenshots into {RawRoot}/<chapter>/<name>.png");
            return 0;
        }

        int processed = 0, errors = 0, noBlock = 0;
        foreach (var rawPath in Directory.EnumerateFiles(rawRootFull, "*.png", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            // _raw/<chapter>/<name>.png → <docs>/images/<chapter>/<name>.png
            var rel = Path.GetRelativePath(rawRootFull, rawPath).Replace('\\', '/');
            var targetRel = $"{DocsRoot}/images/{rel}";
            var targetKey = NormalizePath(targetRel);

            if (!blockByTarget.TryGetValue(targetKey, out var block))
            {
                Console.WriteLine($"  ⚠️  no CAPTURE for {targetRel}, skipping raw {rel}");
                noBlock++;
                continue;
            }

            try
            {
                var targetFull = Path.GetFullPath(targetRel);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFull)!);
                if (block.Crop is not null)
                {
                    CropAndSave(rawPath, block.Crop, targetFull);
                    Console.WriteLine($"  ✂️  {rel} → {targetRel}  (crop {block.Crop.X},{block.Crop.Y},{block.Crop.Width},{block.Crop.Height})");
                }
                else
                {
                    File.Copy(rawPath, targetFull, overwrite: true);
                    Console.WriteLine($"  📄 {rel} → {targetRel}  (full copy, no crop)");
                }
                processed++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  ❌ {rel}: {ex.Message}");
                errors++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"✅ Processed: {processed} | ⚠️  no CAPTURE: {noBlock} | ❌ errors: {errors}");
        return errors > 0 ? 1 : 0;
    }

    private static void CropAndSave(string rawPath, CropRect crop, string targetPath)
    {
        using var input = SKBitmap.Decode(rawPath)
            ?? throw new InvalidOperationException($"Failed to decode {rawPath}");

        if (crop.X < 0 || crop.Y < 0 ||
            crop.Width <= 0 || crop.Height <= 0 ||
            crop.X + crop.Width > input.Width ||
            crop.Y + crop.Height > input.Height)
        {
            throw new InvalidOperationException(
                $"crop rect ({crop.X},{crop.Y},{crop.Width},{crop.Height}) out of bounds for image {input.Width}x{input.Height}");
        }

        using var cropped = new SKBitmap(crop.Width, crop.Height, input.ColorType, input.AlphaType);
        using (var canvas = new SKCanvas(cropped))
        {
            var src = new SKRect(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height);
            var dst = new SKRect(0, 0, crop.Width, crop.Height);
            canvas.DrawBitmap(input, src, dst);
        }

        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(targetPath);
        data.SaveTo(stream);
    }

    // === compose-* サブコマンド ===

    /// <summary>
    /// スケーリング 6 モード比較画像 (um-05-11-scaling-modes) を生成。
    /// <para>
    /// 入力: <c>docs/images/_raw/composites/scaling-modes/</c> 配下に 6 PNG。
    /// ファイル名はアルファベット順で <c>1-None</c> 〜 <c>6-Fill</c> を期待 (ラベルもこの順)。
    /// </para>
    /// </summary>
    public static int ComposeScaling()
    {
        var labels = CurrentLanguage == "en"
            ? new[]
            {
                "Original (None)",
                "Aspect fit (Contain)",
                "Shrink only (ShrinkOnly)",
                "Enlarge only (EnlargeOnly)",
                "Aspect fill (Cover)",
                "Stretch (Fill)",
            }
            : new[]
            {
                "原寸固定 (None)",
                "アスペクト維持 (Contain)",
                "縮小のみ (ShrinkOnly)",
                "拡大のみ (EnlargeOnly)",
                "アスペクト維持・覆う (Cover)",
                "完全充填 (Fill)",
            };
        return RunCompose(
            recipeName: "scaling-modes",
            targetRel: $"{DocsRoot}/images/um/um-05-11-scaling-modes.png",
            labels: labels,
            cols: 3, rows: 2);
    }

    /// <summary>
    /// PhotoBoard 3 スタイル比較画像 (um-06-16-photoboard-styles) を生成。
    /// <para>
    /// 入力: <c>docs/images/_raw/composites/photoboard-styles/</c> 配下に 3 PNG。
    /// ファイル名はアルファベット順で <c>1-Natural</c> / <c>2-Rough</c> / <c>3-Scattered</c> を期待。
    /// </para>
    /// </summary>
    public static int ComposePhotoBoard()
    {
        var labels = CurrentLanguage == "en"
            ? new[] { "Natural", "Rough", "Scattered" }
            : new[]
            {
                "ナチュラル (Natural)",
                "ラフ (Rough)",
                "バラ撒き (Scattered)",
            };
        return RunCompose(
            recipeName: "photoboard-styles",
            targetRel: $"{DocsRoot}/images/um/um-06-16-photoboard-styles.png",
            labels: labels,
            cols: 3, rows: 1);
    }

    /// <summary>
    /// 共通ランナー。 <paramref name="recipeName"/> の入力ディレクトリ配下 PNG をファイル名昇順に並べ、
    /// <paramref name="cols"/> × <paramref name="rows"/> のグリッドへ合成して <paramref name="targetRel"/>
    /// に書き出す。 出力寸法は CAPTURE ブロックの size から取得する。
    /// </summary>
    private static int RunCompose(string recipeName, string targetRel, string[] labels, int cols, int rows)
    {
        var expectedCount = cols * rows;
        if (labels.Length != expectedCount)
        {
            Console.Error.WriteLine($"❌ internal error: {recipeName} expected {expectedCount} labels, got {labels.Length}");
            return 1;
        }

        var blocks = CaptureParser.ParseAllBlocks(DocsRoot);
        var block = blocks.FirstOrDefault(b => string.Equals(NormalizePath(b.FilePath), NormalizePath(targetRel), StringComparison.Ordinal));
        if (block is null)
        {
            Console.Error.WriteLine($"❌ No CAPTURE block found for {targetRel}.");
            return 1;
        }

        var inputDir = Path.GetFullPath(Path.Combine(RawRoot, "composites", recipeName));
        if (!Directory.Exists(inputDir))
        {
            Console.Error.WriteLine($"❌ Input directory not found: {inputDir}");
            Console.Error.WriteLine($"    Place {expectedCount} PNG files there (alphabetical order = composition order).");
            return 1;
        }

        var pngs = Directory.EnumerateFiles(inputDir, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (pngs.Count != expectedCount)
        {
            Console.Error.WriteLine($"❌ Expected {expectedCount} PNG files in {inputDir}, found {pngs.Count}.");
            foreach (var p in pngs) Console.Error.WriteLine($"    - {Path.GetFileName(p)}");
            return 1;
        }

        Console.WriteLine($"Composing {recipeName} ({block.Width}×{block.Height}, {cols}×{rows}):");
        for (int i = 0; i < pngs.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {Path.GetFileName(pngs[i])}  →  {labels[i]}");
        }

        var targetFull = Path.GetFullPath(targetRel);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFull)!);
        Composer.ComposeGrid(pngs, labels, cols, rows, block.Width, block.Height, targetFull);
        Console.WriteLine($"\n✅ Wrote: {targetRel}");
        return 0;
    }

    // === ヘルパ ===

    private static void AppendBlock(StringBuilder sb, CaptureBlock b, Dictionary<string, string> manifest, string sourceRel)
    {
        var status = GetStatus(b.FilePath, manifest);
        var icon = status switch
        {
            ImageStatus.Replaced => "✅",
            ImageStatus.Placeholder => "❌",
            ImageStatus.Missing => "⚠️",
            _ => "?",
        };

        // 画像の src は docs/CAPTURE-REVIEW.md からの相対パス。 b.FilePath が "docs/images/..." なので
        // docs/ 配下から "images/..." に短縮。
        var imgSrc = Path.GetRelativePath(DocsRoot, b.FilePath).Replace('\\', '/');

        sb.AppendLine($"### {icon} `{Path.GetFileName(b.FilePath)}`");
        sb.AppendLine();
        sb.AppendLine($"![{EscapeMd(b.Caption)}]({imgSrc})");
        sb.AppendLine();
        sb.AppendLine("| 項目 | 内容 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| File | `{b.FilePath}` |");
        sb.AppendLine($"| Size | {b.Width}×{b.Height} |");
        sb.AppendLine($"| Samples | {EscapeMdTable(b.Samples)} |");
        sb.AppendLine($"| Caption | {EscapeMdTable(b.Caption)} |");
        if (!string.IsNullOrEmpty(b.Note))
        {
            sb.AppendLine($"| Note | {EscapeMdTable(b.Note)} |");
        }
        sb.AppendLine($"| Status | {icon} {status} |");
        sb.AppendLine($"| Source | `{sourceRel}:{b.LineNumber}` |");
        sb.AppendLine();

        if (b.State.Count > 0)
        {
            sb.AppendLine("**State**:");
            sb.AppendLine();
            foreach (var item in b.State)
            {
                sb.AppendLine($"- {item}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("**Review checklist**:");
        sb.AppendLine("- [ ] サイズ一致 (placeholder の Size と実画像の寸法)");
        sb.AppendLine("- [ ] サンプル画像一致 (Samples 通りの画像が映っている)");
        sb.AppendLine("- [ ] State 通りの構図");
        sb.AppendLine($"- [ ] 言語設定 {CurrentLanguage}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static string EscapeMd(string s) => s.Replace("[", "\\[").Replace("]", "\\]");

    private static string EscapeMdTable(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    private static Dictionary<string, string> LoadManifest()
    {
        if (!File.Exists(ManifestPath))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var json = File.ReadAllText(ManifestPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static ImageStatus GetStatus(string filePath, Dictionary<string, string> manifest)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) return ImageStatus.Missing;

        var normalized = NormalizePath(filePath);
        if (!manifest.TryGetValue(normalized, out var expectedSha))
        {
            // マニフェストに登録なし → ファイルはあるが placeholder 経由ではない → 撮影済とみなす
            return ImageStatus.Replaced;
        }
        var currentSha = ComputeSha(fullPath);
        return string.Equals(currentSha, expectedSha, StringComparison.Ordinal)
            ? ImageStatus.Placeholder
            : ImageStatus.Replaced;
    }

    private static string ComputeSha(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string NormalizePath(string p) => p.Replace('\\', '/');
}

// === CAPTURE ブロック parser ===

internal static class CaptureParser
{
    private static readonly Regex BlockPattern =
        new(@"<!--\s*CAPTURE\s*\r?\n(.*?)\r?\n\s*-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SizePattern =
        new(@"(\d+)\s*[x×]\s*(\d+)", RegexOptions.Compiled);

    private static readonly Regex CropPattern =
        new(@"^\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*$", RegexOptions.Compiled);

    public static List<CaptureBlock> ParseAllBlocks(string docsRoot)
    {
        var blocks = new List<CaptureBlock>();
        if (!Directory.Exists(docsRoot))
        {
            Console.Error.WriteLine($"⚠️  Docs directory not found: {docsRoot}");
            return blocks;
        }

        foreach (var mdPath in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories))
        {
            // 自動生成ファイル / インベントリ自身は除外
            var fileName = Path.GetFileName(mdPath);
            if (fileName == "CAPTURE-REVIEW.md") continue;
            if (fileName == "CAPTURE-LIST.md") continue;

            // 言語別サブツリー (docs/en/) は、 そのルートを --docs=docs/en で直接
            // 指定したときだけ処理する。 上位ルート (docs) のスキャンには含めない。
            var relFirstSegment = Path.GetRelativePath(docsRoot, mdPath)
                .Replace('\\', '/').Split('/')[0];
            if (relFirstSegment is "en") continue;

            var content = File.ReadAllText(mdPath);
            foreach (Match m in BlockPattern.Matches(content))
            {
                var inner = m.Groups[1].Value;
                var lineNum = content[..m.Index].Count(c => c == '\n') + 1;
                var block = Parse(mdPath, lineNum, inner);
                if (block is not null) blocks.Add(block);
            }
        }
        return blocks;
    }

    private static CaptureBlock? Parse(string sourceFile, int lineNumber, string inner)
    {
        string? filePath = null, sizeText = null, samples = null, caption = null, note = null, cropText = null;
        var state = new List<string>();
        bool inState = false;

        foreach (var rawLine in inner.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) { inState = false; continue; }

            // state リスト項目 ("  - ..." or "  * ...")
            if (inState && (line.StartsWith("  -") || line.StartsWith("  *") || line.StartsWith("\t-")))
            {
                var idx = line.IndexOfAny(new[] { '-', '*' });
                if (idx >= 0 && idx + 1 < line.Length)
                {
                    state.Add(line[(idx + 1)..].TrimStart());
                }
                continue;
            }

            var colonIdx = line.IndexOf(':');
            if (colonIdx < 0) { inState = false; continue; }

            var key = line[..colonIdx].TrimStart();
            var value = colonIdx + 1 < line.Length ? line[(colonIdx + 1)..].Trim() : string.Empty;

            inState = false;
            switch (key.ToLowerInvariant())
            {
                case "file": filePath = value; break;
                case "size": sizeText = value; break;
                case "samples": samples = value; break;
                case "caption": caption = value; break;
                case "note": note = value; break;
                case "crop": cropText = value; break;
                case "state": inState = true; break;
            }
        }

        if (string.IsNullOrEmpty(filePath))
        {
            Console.Error.WriteLine($"  warning: CAPTURE block at {sourceFile}:{lineNumber} has no 'file' key. Skipping.");
            return null;
        }

        int width = 800, height = 600;
        if (!string.IsNullOrEmpty(sizeText))
        {
            var m = SizePattern.Match(sizeText);
            if (m.Success)
            {
                width = int.Parse(m.Groups[1].Value);
                height = int.Parse(m.Groups[2].Value);
            }
        }

        CropRect? crop = null;
        if (!string.IsNullOrEmpty(cropText))
        {
            var cm = CropPattern.Match(cropText);
            if (cm.Success)
            {
                crop = new CropRect(
                    int.Parse(cm.Groups[1].Value),
                    int.Parse(cm.Groups[2].Value),
                    int.Parse(cm.Groups[3].Value),
                    int.Parse(cm.Groups[4].Value));
            }
            else
            {
                Console.Error.WriteLine($"  warning: {sourceFile}:{lineNumber} invalid crop '{cropText}', expected 'x,y,w,h'. Ignoring.");
            }
        }

        return new CaptureBlock(
            sourceFile, lineNumber, filePath,
            sizeText ?? "(unknown)", width, height,
            samples ?? "(none)", caption ?? "(no caption)", note ?? "",
            state, crop);
    }
}

// === Placeholder PNG renderer ===

internal static class PlaceholderRenderer
{
    private static readonly SKColor BgColor = new(0xFF, 0xF5, 0xF5);
    private static readonly SKColor StripeColor = new(0xFF, 0xE0, 0xE0);
    private static readonly SKColor BorderColor = new(0xDC, 0x26, 0x26);
    private static readonly SKColor TextDark = SKColors.Black;
    private static readonly SKColor TextBody = new(0x33, 0x33, 0x33);
    private static readonly SKColor TextDim = new(0x65, 0x65, 0x65);
    private static readonly SKColor Separator = new(0xCB, 0xD5, 0xE1);

    public static void Render(CaptureBlock b, string outputPath)
    {
        var w = b.Width;
        var h = b.Height;

        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) throw new InvalidOperationException("SKSurface creation failed");
        var canvas = surface.Canvas;

        canvas.Clear(BgColor);
        DrawStripes(canvas, w, h);
        DrawBorder(canvas, w, h);

        var typefaceBold = GetTypeface(SKFontStyle.Bold);
        var typefaceRegular = GetTypeface(SKFontStyle.Normal);

        DrawWatermark(canvas, w, h, typefaceBold);

        // 文字レイアウト
        float margin = Math.Max(24, Math.Min(w, h) * 0.04f);
        float y = margin;
        float maxWidth = w - 2 * margin;
        float scale = Math.Min(w, h) / 600f; // 600 基準で font size をスケール
        scale = Math.Clamp(scale, 0.6f, 2.5f);

        // Header
        using (var font = new SKFont(typefaceBold, 32 * scale))
        using (var paint = new SKPaint { Color = BorderColor, IsAntialias = true })
        {
            canvas.DrawText("[ PLACEHOLDER ] TODO", margin, y + font.Size, font, paint);
            y += font.Size + 6;
        }

        // ファイルパス
        using (var font = new SKFont(typefaceRegular, 16 * scale))
        using (var paint = new SKPaint { Color = TextDim, IsAntialias = true })
        {
            canvas.DrawText(b.FilePath, margin, y + font.Size, font, paint);
            y += font.Size + 16;
        }

        DrawSeparator(canvas, margin, y, w - margin);
        y += 12;

        // Key-Value テーブル
        float keySize = 16 * scale;
        float valueSize = 18 * scale;
        y = DrawKeyValueBlock(canvas, "Size", $"{b.Width}×{b.Height}",
            typefaceBold, typefaceRegular, keySize, valueSize, margin, y, maxWidth);
        y = DrawKeyValueBlock(canvas, "Samples", b.Samples,
            typefaceBold, typefaceRegular, keySize, valueSize, margin, y, maxWidth);
        y = DrawKeyValueBlock(canvas, "Caption", b.Caption,
            typefaceBold, typefaceRegular, keySize, valueSize, margin, y, maxWidth);
        if (!string.IsNullOrEmpty(b.Note))
        {
            y = DrawKeyValueBlock(canvas, "Note", b.Note,
                typefaceBold, typefaceRegular, keySize, valueSize, margin, y, maxWidth);
        }

        // State
        if (b.State.Count > 0 && y < h - margin * 2)
        {
            y += 4;
            DrawSeparator(canvas, margin, y, w - margin);
            y += 12;
            using (var font = new SKFont(typefaceBold, keySize))
            using (var paint = new SKPaint { Color = TextDark, IsAntialias = true })
            {
                canvas.DrawText("State:", margin, y + font.Size, font, paint);
                y += font.Size + 6;
            }
            foreach (var item in b.State)
            {
                y = DrawWrappedText(canvas, "  • " + item, typefaceRegular, valueSize * 0.95f,
                    margin, y, maxWidth, TextBody);
                if (y >= h - margin) break;
            }
        }

        SaveImage(surface, outputPath);
        typefaceBold.Dispose();
        typefaceRegular.Dispose();
    }

    private static void DrawStripes(SKCanvas canvas, int w, int h)
    {
        using var paint = new SKPaint
        {
            Color = StripeColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false,
        };
        for (int x = -h; x < w + h; x += 28)
        {
            canvas.DrawLine(x, 0, x + h, h, paint);
        }
    }

    private static void DrawBorder(SKCanvas canvas, int w, int h)
    {
        using var paint = new SKPaint
        {
            Color = BorderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 8f,
            PathEffect = SKPathEffect.CreateDash(new float[] { 16f, 8f }, 0f),
            IsAntialias = true,
        };
        canvas.DrawRect(4, 4, w - 8, h - 8, paint);
    }

    private static void DrawWatermark(SKCanvas canvas, int w, int h, SKTypeface typefaceBold)
    {
        var fontSize = Math.Min(w, h) * 0.20f;
        if (fontSize < 40) return;
        using var font = new SKFont(typefaceBold, fontSize);
        using var paint = new SKPaint { Color = new SKColor(0xDC, 0x26, 0x26, 0x28), IsAntialias = true };
        canvas.Save();
        canvas.RotateDegrees(-25, w / 2f, h / 2f);
        font.MeasureText("PLACEHOLDER", out var bounds);
        canvas.DrawText("PLACEHOLDER", w / 2f - bounds.MidX, h / 2f - bounds.MidY, font, paint);
        canvas.Restore();
    }

    private static void DrawSeparator(SKCanvas canvas, float x1, float y, float x2)
    {
        using var paint = new SKPaint
        {
            Color = Separator,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = false,
        };
        canvas.DrawLine(x1, y, x2, y, paint);
    }

    private static float DrawKeyValueBlock(SKCanvas canvas, string key, string value,
        SKTypeface keyTypeface, SKTypeface valueTypeface, float keySize, float valueSize,
        float x, float y, float maxWidth)
    {
        using var kFont = new SKFont(keyTypeface, keySize);
        using var vFont = new SKFont(valueTypeface, valueSize);
        using var keyPaint = new SKPaint { Color = TextDark, IsAntialias = true };
        using var valuePaint = new SKPaint { Color = TextBody, IsAntialias = true };

        var keyLabel = key + ":";
        canvas.DrawText(keyLabel, x, y + kFont.Size, kFont, keyPaint);
        kFont.MeasureText(keyLabel, out var keyBounds);

        var valueX = x + keyBounds.Width + 12;
        var availableForValue = maxWidth - (valueX - x);
        var lines = WrapText(value, vFont, availableForValue);
        var lineHeight = vFont.Size * 1.35f;

        float curY = y;
        foreach (var line in lines)
        {
            canvas.DrawText(line, valueX, curY + vFont.Size, vFont, valuePaint);
            curY += lineHeight;
        }
        return Math.Max(y + kFont.Size + 8, curY + 4);
    }

    private static float DrawWrappedText(SKCanvas canvas, string text, SKTypeface typeface, float fontSize,
        float x, float y, float maxWidth, SKColor color)
    {
        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var lineHeight = font.Size * 1.4f;
        var lines = WrapText(text, font, maxWidth);
        foreach (var line in lines)
        {
            canvas.DrawText(line, x, y + font.Size, font, paint);
            y += lineHeight;
        }
        return y + 2;
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
        if (maxWidth <= 0) { lines.Add(text); return lines; }

        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            sb.Append(ch);
            font.MeasureText(sb.ToString(), out var bounds);
            if (bounds.Width > maxWidth)
            {
                sb.Length--;
                if (sb.Length > 0) lines.Add(sb.ToString());
                sb.Clear();
                sb.Append(ch);
            }
        }
        if (sb.Length > 0) lines.Add(sb.ToString());
        return lines;
    }

    private static SKTypeface GetTypeface(SKFontStyle style)
    {
        return SKTypeface.FromFamilyName("Yu Gothic UI", style)
            ?? SKTypeface.FromFamilyName("Meiryo", style)
            ?? SKTypeface.FromFamilyName("Segoe UI", style)
            ?? SKTypeface.FromFamilyName("Arial", style)
            ?? SKTypeface.Default;
    }

    private static void SaveImage(SKSurface surface, string path)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }
}

// === 比較合成画像生成 (Composer) ===

/// <summary>
/// 個別エクスポート PNG を <c>cols × rows</c> のグリッドへ並べて 1 枚に合成するユーティリティ。
/// セル上部に日本語ラベル、 中央に画像 (Contain でフィット、 アスペクト維持) を配置する。
/// 背景は白、 セル間にうっすら罫線を引いて区切りを明示する。
/// </summary>
internal static class Composer
{
    private static readonly SKColor BackgroundColor = SKColors.White;
    private static readonly SKColor LabelBackground = new(0xF2, 0xF2, 0xF2);
    private static readonly SKColor LabelTextColor = new(0x1F, 0x1F, 0x1F);
    private static readonly SKColor CellBorderColor = new(0xCC, 0xCC, 0xCC);
    private static readonly SKColor ImageBackground = new(0xFA, 0xFA, 0xFA);

    /// <summary>
    /// 指定パスの PNG 群を <paramref name="cols"/> × <paramref name="rows"/> グリッドに並べて合成。
    /// 入力枚数は <c>cols * rows</c> と一致している必要がある (呼び出し元で検証済み前提)。
    /// </summary>
    public static void ComposeGrid(
        IReadOnlyList<string> imagePaths,
        IReadOnlyList<string> labels,
        int cols,
        int rows,
        int outputWidth,
        int outputHeight,
        string outputPath)
    {
        if (imagePaths.Count != cols * rows)
            throw new ArgumentException($"imagePaths count {imagePaths.Count} != cols*rows {cols * rows}");
        if (labels.Count != cols * rows)
            throw new ArgumentException($"labels count {labels.Count} != cols*rows {cols * rows}");

        using var surface = SKSurface.Create(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) throw new InvalidOperationException("SKSurface creation failed");
        var canvas = surface.Canvas;
        canvas.Clear(BackgroundColor);

        // セルレイアウト計算。 セル幅 / 高さは整数 px に丸めるため、 端数は最終列・行で吸収。
        var cellWidth = outputWidth / cols;
        var cellHeight = outputHeight / rows;

        // ラベルバンドはセル高さ比例 (~12%)、 最小 28px / 最大 64px。
        var labelHeight = Math.Clamp((int)(cellHeight * 0.12f), 28, 64);
        var labelFontSize = labelHeight * 0.55f;

        using var typefaceBold = GetTypeface(SKFontStyle.Bold);
        using var labelFont = new SKFont(typefaceBold, labelFontSize);
        using var labelPaint = new SKPaint { Color = LabelTextColor, IsAntialias = true };
        using var labelBgPaint = new SKPaint { Color = LabelBackground, IsAntialias = false };
        using var borderPaint = new SKPaint
        {
            Color = CellBorderColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = false,
        };
        using var imageBgPaint = new SKPaint { Color = ImageBackground, IsAntialias = false };

        for (int i = 0; i < imagePaths.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var cellX = col * cellWidth;
            var cellY = row * cellHeight;
            // 最終列 / 行は端数を吸収して右端 / 下端まで描く。
            var thisW = (col == cols - 1) ? (outputWidth - cellX) : cellWidth;
            var thisH = (row == rows - 1) ? (outputHeight - cellY) : cellHeight;

            DrawCell(
                canvas,
                imagePaths[i],
                labels[i],
                new SKRect(cellX, cellY, cellX + thisW, cellY + thisH),
                labelHeight,
                labelFont,
                labelPaint,
                labelBgPaint,
                imageBgPaint,
                borderPaint);
        }

        SaveSurface(surface, outputPath);
    }

    private static void DrawCell(
        SKCanvas canvas,
        string imagePath,
        string label,
        SKRect cellRect,
        int labelHeight,
        SKFont labelFont,
        SKPaint labelPaint,
        SKPaint labelBgPaint,
        SKPaint imageBgPaint,
        SKPaint borderPaint)
    {
        // ラベル背景 (セル上部)
        var labelRect = new SKRect(cellRect.Left, cellRect.Top, cellRect.Right, cellRect.Top + labelHeight);
        canvas.DrawRect(labelRect, labelBgPaint);

        // ラベルテキスト (横中央 + 縦中央)
        labelFont.MeasureText(label, out var textBounds);
        var textX = labelRect.MidX - textBounds.MidX;
        var textY = labelRect.MidY - textBounds.MidY;
        canvas.DrawText(label, textX, textY, labelFont, labelPaint);

        // 画像エリア
        var imgRect = new SKRect(cellRect.Left, cellRect.Top + labelHeight, cellRect.Right, cellRect.Bottom);
        canvas.DrawRect(imgRect, imageBgPaint);

        using var bitmap = SKBitmap.Decode(imagePath);
        if (bitmap is not null)
        {
            // Contain (アスペクト維持で内側にフィット)。 8px パディングを取って枠と画像の隙間を作る。
            const float padding = 8f;
            var avail = new SKRect(
                imgRect.Left + padding,
                imgRect.Top + padding,
                imgRect.Right - padding,
                imgRect.Bottom - padding);

            var scale = Math.Min(avail.Width / bitmap.Width, avail.Height / bitmap.Height);
            var drawW = bitmap.Width * scale;
            var drawH = bitmap.Height * scale;
            var dst = new SKRect(
                avail.MidX - drawW / 2f,
                avail.MidY - drawH / 2f,
                avail.MidX + drawW / 2f,
                avail.MidY + drawH / 2f);

            // SkiaSharp 3.x では DrawBitmap(SKBitmap, SKRect, SKSamplingOptions) のオーバーロードが
            // 削除されたため、 SKImage 経由の DrawImage(src, dst, sampling, paint) を使う。
            using var image = SKImage.FromBitmap(bitmap);
            var src = new SKRect(0, 0, bitmap.Width, bitmap.Height);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            canvas.DrawImage(image, src, dst, sampling, paint: null);
        }

        // セル境界 (薄いグレー枠) — ラベル / 画像エリアの両方を含むセル外周のみ
        canvas.DrawRect(cellRect, borderPaint);
        // ラベルと画像の境界 (横線)
        canvas.DrawLine(cellRect.Left, cellRect.Top + labelHeight, cellRect.Right, cellRect.Top + labelHeight, borderPaint);
    }

    private static SKTypeface GetTypeface(SKFontStyle style)
    {
        return SKTypeface.FromFamilyName("Yu Gothic UI", style)
            ?? SKTypeface.FromFamilyName("Meiryo", style)
            ?? SKTypeface.FromFamilyName("Segoe UI", style)
            ?? SKTypeface.FromFamilyName("Arial", style)
            ?? SKTypeface.Default;
    }

    private static void SaveSurface(SKSurface surface, string path)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }
}
