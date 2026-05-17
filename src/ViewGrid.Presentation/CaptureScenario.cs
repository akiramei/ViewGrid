using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;

namespace ViewGrid.Presentation;

/// <summary>キャプチャシナリオ内の 1 配置 (どのサンプル画像をどのセルに置くか)。</summary>
/// <param name="SampleFileName">配置するサンプル画像のファイル名 (例: <c>sample-01.png</c>)。</param>
/// <param name="Column">配置先セルの列 (0 始まり)。</param>
/// <param name="Row">配置先セルの行 (0 始まり)。</param>
internal sealed record CapturePlacement(string SampleFileName, int Column, int Row);

/// <summary>シナリオが既定バリアントに加えて作成する追加バリアント。</summary>
/// <param name="SampleFileName">対象アセットのサンプル画像ファイル名。</param>
/// <param name="Name">追加バリアントの表示名。</param>
internal sealed record CaptureExtraVariant(string SampleFileName, string Name);

/// <summary>保護領域の矩形 (元画像座標系の 0–1 比率) と塗りつぶし方法。</summary>
internal sealed record CaptureRegion(
    double X, double Y, double Width, double Height, ProtectedRegionFillMode FillMode);

/// <summary>
/// サンプルの既定バリアントへ適用する特性。 AutoCrop と保護領域に対応する。
/// </summary>
/// <param name="SampleFileName">対象サンプル画像のファイル名。</param>
/// <param name="AutoCrop">適用する自動トリミング設定。 null なら適用しない。</param>
/// <param name="Regions">登録する保護領域。 null なら登録しない。</param>
internal sealed record CaptureVariantProperty(
    string SampleFileName,
    AutoCropSettings? AutoCrop = null,
    IReadOnlyList<CaptureRegion>? Regions = null);

/// <summary>キャプチャシナリオが用意する 1 グリッド。</summary>
internal sealed record CaptureGrid(
    string Name,
    int Columns,
    int Rows,
    int CanvasWidth,
    int CanvasHeight,
    IReadOnlyList<CapturePlacement> Placements);

/// <summary>
/// 1 つの CAPTURE プレースホルダに対応する撮影シーンの宣言的定義。
/// <c>--capture-scenario=&lt;Id&gt;</c> で起動すると、 この定義どおりに隔離ワークスペースへ
/// グリッド・配置を組み立て、 撮影者が「最後の一手」 (選択・ドラッグ・ダイアログ操作等) だけで
/// 撮れる状態にする。
/// </summary>
/// <param name="Id">シナリオ ID。 対応する画像ファイル名 (拡張子なし) に揃える。</param>
/// <param name="Description">撮影者向けの状態説明 (ログに出力)。</param>
/// <param name="Samples">取り込むサンプル画像 (候補リストに並ぶ。 配置に使う分を含む)。</param>
/// <param name="Grids">用意するグリッドと配置。 先頭グリッドが起動時のアクティブになる。</param>
/// <param name="ExtraVariants">既定バリアントに加えて作成する追加バリアント。</param>
/// <param name="VariantProperties">既定バリアントへ適用する特性 (トリミング / 保護領域)。</param>
internal sealed record CaptureScenario(
    string Id,
    string Description,
    IReadOnlyList<string> Samples,
    IReadOnlyList<CaptureGrid> Grids,
    IReadOnlyList<CaptureExtraVariant>? ExtraVariants = null,
    IReadOnlyList<CaptureVariantProperty>? VariantProperties = null);

/// <summary>
/// キャプチャシナリオの定義レジストリと、 隔離ワークスペースへシーンを組み立てるビルダー。
/// </summary>
internal static class CaptureScenarios
{
    private const int Canvas = 1200;

    // よく使うサンプルセット。
    private static readonly string[] Samples1To4 =
        ["sample-01.png", "sample-02.png", "sample-03.png", "sample-04.png"];

    private static readonly string[] Samples1To6 =
        ["sample-01.png", "sample-02.png", "sample-03.png",
         "sample-04.png", "sample-05.png", "sample-06.png"];

    /// <summary>
    /// データ staging で撮影者の手数を減らせるシナリオ群。
    /// グリッド・配置までを用意し、 選択 / ペイン展開 / ダイアログ起動など
    /// 「最後の一手」は撮影者が行う (それ自体が撮影対象の操作であることが多い)。
    /// </summary>
    private static readonly IReadOnlyList<CaptureScenario> All =
    [
        new CaptureScenario(
            Id: "qs-01-01-main-window-overview",
            Description: "アプリ全体像。 2×2 グリッドに sample-01〜03 を配置済み。 撮影者はそのまま撮る。",
            Samples: Samples1To4,
            Grids:
            [
                Grid("グリッド 1", 2, 2,
                    Place("sample-01.png", 0, 0),
                    Place("sample-02.png", 1, 0),
                    Place("sample-03.png", 1, 1)),
            ]),

        new CaptureScenario(
            Id: "um-01-03-main-window-3pane",
            Description: "3 ペイン構成。 グリッド 2 件 (1 件目 3×3 アクティブ・配置 3 件、 2 件目は空)。"
                + " 撮影者は中央の配置 (sample-02) をクリック選択して Inspector を出す。",
            Samples: ["sample-01.png", "sample-02.png", "sample-03.png"],
            Grids:
            [
                Grid("グリッド 1", 3, 3,
                    Place("sample-01.png", 0, 0),
                    Place("sample-02.png", 1, 1),
                    Place("sample-03.png", 2, 2)),
                Grid("グリッド 2", 3, 3),
            ]),

        new CaptureScenario(
            Id: "um-03-06-grid-properties",
            Description: "グリッド設定 (右ペイン)。 配置なしのグリッドがアクティブ。"
                + " 撮影者は名前欄を編集してドラフト状態 (● バッジ) にして撮る。",
            Samples: [],
            Grids: [Grid("グリッド 1", 2, 2)]),

        new CaptureScenario(
            Id: "um-03-07-boundary-drag",
            Description: "3×3 グリッドに sample-01〜06 を上 2 行へ配置済み。"
                + " 撮影者は中央列の境界線をドラッグして強調表示を撮る。",
            Samples: Samples1To6,
            Grids:
            [
                Grid("グリッド 1", 3, 3,
                    Place("sample-01.png", 0, 0),
                    Place("sample-02.png", 1, 0),
                    Place("sample-03.png", 2, 0),
                    Place("sample-04.png", 0, 1),
                    Place("sample-05.png", 1, 1),
                    Place("sample-06.png", 2, 1)),
            ]),

        new CaptureScenario(
            Id: "um-04-09-drop-valid",
            Description: "候補→セルの D&D (有効ホバー)。 sample-01 を左上に配置済み、"
                + " sample-02 は候補のまま。 撮影者は sample-02 を右上セルへドラッグして緑ハイライトを撮る。"
                + " (qs-03-03-drag-to-cell も同一シーン)",
            Samples: ["sample-01.png", "sample-02.png"],
            Grids: [Grid("グリッド 1", 2, 2, Place("sample-01.png", 0, 0))]),

        new CaptureScenario(
            Id: "qs-03-03-drag-to-cell",
            Description: "候補→セルの D&D。 um-04-09-drop-valid と同一シーン。",
            Samples: ["sample-01.png", "sample-02.png"],
            Grids: [Grid("グリッド 1", 2, 2, Place("sample-01.png", 0, 0))]),

        new CaptureScenario(
            Id: "um-04-10-inspector",
            Description: "Inspector の構造。 2×2 グリッドに sample-01 を 1 件配置済み。"
                + " 撮影者は配置をクリック選択して右ペインの Inspector を出す。",
            Samples: ["sample-01.png"],
            Grids: [Grid("グリッド 1", 2, 2, Place("sample-01.png", 0, 0))]),

        new CaptureScenario(
            Id: "um-06-15-output-settings",
            Description: "出力設定。 2×2 グリッドに sample-01〜04 を配置済み。"
                + " 撮影者は右ペインの出力設定 Expander を展開して撮る。",
            Samples: Samples1To4,
            Grids: [Grid("グリッド 1", 2, 2, PlaceFour())]),

        new CaptureScenario(
            Id: "qs-03-04-preview-window",
            Description: "プレビューウィンドウ。 2×2 グリッドに sample-01〜04 を配置済み。"
                + " 撮影者はプレビューボタンを押してプレビューを開く。 (um-06-18-preview も同一シーン)",
            Samples: Samples1To4,
            Grids: [Grid("グリッド 1", 2, 2, PlaceFour())]),

        new CaptureScenario(
            Id: "um-06-18-preview",
            Description: "プレビューウィンドウ。 qs-03-04-preview-window と同一シーン。",
            Samples: Samples1To4,
            Grids: [Grid("グリッド 1", 2, 2, PlaceFour())]),

        new CaptureScenario(
            Id: "um-02-05-add-variant",
            Description: "候補リストからのバリアント追加。 sample-01 にバリアント 2 件"
                + " (既定「(無名)」+「派生 1」) を用意済み。 撮影者は候補ペインを撮る。",
            Samples: ["sample-01.png"],
            Grids: [],
            ExtraVariants: [new CaptureExtraVariant("sample-01.png", "派生 1")]),

        new CaptureScenario(
            Id: "um-05-11-alignment",
            Description: "共有特性編集タブ。 sample-01 を 1 件取り込み済み。"
                + " 撮影者は候補のバリアントを選択して右ペインの特性タブを撮る。",
            Samples: ["sample-01.png"],
            Grids: []),

        new CaptureScenario(
            Id: "um-05-13-autocrop",
            Description: "AutoCrop 設定。 autocrop-white のバリアントに自動トリミング (白) を適用済み。"
                + " 撮影者はそのバリアントを選択してトリミングタブを撮る。",
            Samples: ["autocrop-white.png"],
            Grids: [],
            VariantProperties:
            [
                new CaptureVariantProperty("autocrop-white.png", AutoCrop: AutoCropSettings.White),
            ]),

        new CaptureScenario(
            Id: "um-05-14-region-tab",
            Description: "保護領域タブ。 region-speech のバリアントに保護領域 2 件を登録済み。"
                + " 撮影者はそのバリアントを選択して保護領域タブを撮る。",
            Samples: ["region-speech.png"],
            Grids: [],
            VariantProperties:
            [
                new CaptureVariantProperty("region-speech.png", Regions:
                [
                    new CaptureRegion(0.10, 0.08, 0.42, 0.30, ProtectedRegionFillMode.White),
                    new CaptureRegion(0.46, 0.52, 0.34, 0.30, ProtectedRegionFillMode.White),
                ]),
            ]),
    ];

    /// <summary>2×2 グリッドへ sample-01〜04 を 4 隅に配置する定義を返す。</summary>
    private static CapturePlacement[] PlaceFour() =>
    [
        Place("sample-01.png", 0, 0),
        Place("sample-02.png", 1, 0),
        Place("sample-03.png", 0, 1),
        Place("sample-04.png", 1, 1),
    ];

    private static CapturePlacement Place(string sample, int column, int row) =>
        new(sample, column, row);

    private static CaptureGrid Grid(string name, int columns, int rows, params CapturePlacement[] placements) =>
        new(name, columns, rows, Canvas, Canvas, placements);

    /// <summary>シナリオ ID で定義を引く (大文字小文字無視)。 未定義なら null。</summary>
    public static CaptureScenario? Find(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>定義済みシナリオ ID の一覧 (未定義 ID 指定時の案内に使う)。</summary>
    public static IEnumerable<string> Ids => All.Select(s => s.Id);

    /// <summary>
    /// 指定シナリオのシーンを隔離ワークスペースへ組み立てる。
    /// サンプル取り込み → グリッド作成 → 配置 → 起動時アクティブグリッド設定 (先頭グリッド)、 の順。
    /// 各ステップの失敗はログに残して続行する (キャプチャモード起動自体は妨げない)。
    /// </summary>
    public static async Task BuildAsync(IServiceProvider services, string scenarioId, string[] args)
    {
        var scenario = Find(scenarioId);
        if (scenario is null)
        {
            Log.Warning("キャプチャモード: シナリオ '{Id}' は未定義です。 利用可能: {Ids}",
                scenarioId, string.Join(", ", Ids));
            return;
        }

        var samplesDir = CaptureMode.ResolveSamplesDirectory(args);
        if (samplesDir is null && scenario.Samples.Count > 0)
        {
            Log.Warning("キャプチャモード: サンプル画像ディレクトリが見つからず、 シナリオ '{Id}' を構築できません。"
                + " --capture-samples=<dir> で明示指定できます。", scenarioId);
            return;
        }

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var import = sp.GetRequiredService<ImportImageUseCase>();
        var createCopy = sp.GetRequiredService<CreateLogicalCopyUseCase>();
        var updateCopy = sp.GetRequiredService<UpdateImageCopyUseCase>();
        var createGrid = sp.GetRequiredService<CreateGridCanvasUseCase>();
        var place = sp.GetRequiredService<PlaceImageCopyUseCase>();
        var settings = sp.GetRequiredService<IAppSettingsService>();

        // 1. サンプル取り込み (ファイル名 → CopyId / AssetId のマップを作る)。
        var copyIdByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var assetIdByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in scenario.Samples)
        {
            var path = Path.Combine(samplesDir!, sample);
            if (!File.Exists(path))
            {
                Log.Warning("キャプチャモード: サンプル {Sample} が見つかりません ({Dir})。", sample, samplesDir);
                continue;
            }

            var result = await import.ExecuteAsync(
                new ImportImageRequest { SourcePath = path, SourceType = ImageSource.File });
            if (result.IsError)
            {
                Log.Warning("キャプチャモード: サンプル取り込み失敗 {Sample}: {Errors}",
                    sample, string.Join(", ", result.Errors.Select(e => e.Description)));
                continue;
            }

            copyIdByName[sample] = result.Value.DefaultCopy.Id;
            assetIdByName[sample] = result.Value.Asset.Id;
        }

        // 1b. 追加バリアントの作成 (既定バリアントに加えて)。
        foreach (var ev in scenario.ExtraVariants ?? [])
        {
            if (!assetIdByName.TryGetValue(ev.SampleFileName, out var assetId))
            {
                Log.Warning("キャプチャモード: 追加バリアントスキップ — {Sample} 未取り込み。", ev.SampleFileName);
                continue;
            }

            var variantResult = await createCopy.ExecuteAsync(assetId, ev.Name);
            if (variantResult.IsError)
            {
                Log.Warning("キャプチャモード: 追加バリアント作成失敗 {Sample}/{Name}: {Errors}",
                    ev.SampleFileName, ev.Name,
                    string.Join(", ", variantResult.Errors.Select(e => e.Description)));
            }
        }

        // 1c. 既定バリアントへの特性適用 (AutoCrop / 保護領域)。
        foreach (var vp in scenario.VariantProperties ?? [])
        {
            if (!copyIdByName.TryGetValue(vp.SampleFileName, out var copyId))
            {
                Log.Warning("キャプチャモード: 特性適用スキップ — {Sample} 未取り込み。", vp.SampleFileName);
                continue;
            }

            var regions = vp.Regions is null
                ? (ImmutableArray<ProtectedRegion>?)null
                : [.. vp.Regions.Select((r, index) => new ProtectedRegion
                {
                    Id = Guid.NewGuid(),
                    ImageCopyId = copyId,
                    Rect = new RegionRectFraction(r.X, r.Y, r.Width, r.Height),
                    FillMode = r.FillMode,
                    SortOrder = index,
                })];

            var updateResult = await updateCopy.ExecuteAsync(copyId,
                new UpdateImageCopyChanges { AutoCrop = vp.AutoCrop, Regions = regions });
            if (updateResult.IsError)
            {
                Log.Warning("キャプチャモード: 特性適用失敗 {Sample}: {Errors}",
                    vp.SampleFileName, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
            }
        }

        // 2. グリッド作成 + 配置。 先頭グリッドを起動時アクティブにする。
        Guid? activeGridId = null;
        var placed = 0;
        var totalPlacements = 0;
        foreach (var g in scenario.Grids)
        {
            var gridResult = await createGrid.ExecuteAsync(new CreateGridCanvasRequest
            {
                Name = g.Name,
                Rows = g.Rows,
                Cols = g.Columns,
                CanvasWidth = g.CanvasWidth,
                CanvasHeight = g.CanvasHeight,
            });
            if (gridResult.IsError)
            {
                Log.Warning("キャプチャモード: グリッド '{Grid}' 作成失敗: {Errors}",
                    g.Name, string.Join(", ", gridResult.Errors.Select(e => e.Description)));
                continue;
            }

            var gridId = gridResult.Value.Id;
            activeGridId ??= gridId;

            foreach (var p in g.Placements)
            {
                totalPlacements++;
                if (!copyIdByName.TryGetValue(p.SampleFileName, out var copyId))
                {
                    Log.Warning("キャプチャモード: 配置スキップ — {Sample} の取り込みに失敗。", p.SampleFileName);
                    continue;
                }

                var placeResult = await place.ExecuteAsync(gridId, copyId, new CellPosition(p.Column, p.Row));
                if (placeResult.IsError)
                {
                    Log.Warning("キャプチャモード: 配置失敗 {Sample}@({X},{Y}): {Errors}",
                        p.SampleFileName, p.Column, p.Row,
                        string.Join(", ", placeResult.Errors.Select(e => e.Description)));
                }
                else
                {
                    placed++;
                }
            }
        }

        // 3. 先頭グリッドを起動時のアクティブグリッドにする (LastOpenedGridId 復元経路)。
        if (activeGridId is { } id)
            await settings.UpdateAsync(s => s with { LastOpenedGridId = id.ToString() });

        Log.Information(
            "キャプチャモード: シナリオ '{Id}' を構築しました (グリッド {Grids} 件、 配置 {Placed}/{Total})。 {Desc}",
            scenario.Id, scenario.Grids.Count, placed, totalPlacements, scenario.Description);
    }
}
