# Pattern 1 Micro-Pilot — Candidate Target 選定 (F-P9)

> 最初の再生成 (d-1) で何を対象にするか。評価軸は「空振りリスクの低さ」=
> 失敗時に *方法論の弱さ* と *対象の重さ* を切り分けられること。

## 評価軸
1. **UI/Avalonia を含まない** (描画・XAML の等価判定は困難)
2. **EF migration を含まない** (永続化スキーマ一致は別問題)
3. **precedence/規則が明確** (意味が一意なら生成も判定も容易)
4. **既存テストが等価判定 oracle になる** (合否を即判定できる)
5. **as-built BOM で意味境界が既に明確** (生成入力が揃っている)
6. **観測済みの価値** (drift/盲点に直接効く)

## 候補スコア

| 候補 | UI 無 | EF 無 | 規則明確 | 既存 oracle | BOM 明確 | 価値 | 総評 |
| --- | :-: | :-: | :-: | :-: | :-: | :-: | --- |
| **ImageCropResolver + EffectiveCrop (推奨)** | ✅ | ✅ | ◎ R-08 | ◎ `ImageCropResolverTests` 5 件 | ◎ IMAGE_VARIANT IR-04/IR-05 | ◎ IO-1 drift に直効き | **最有力** |
| PlacementValidator (GRID 純関数) | ✅ | ✅ | ◎ AR-01/02 | ◎ `PlacementValidatorTests` + swap/move anchor | ◎ GRID AR-01..06 | ○ 既に anchor 厚い | 次点 (代替候補) |
| UpdateImageCopyUseCase (統合更新) | ✅ | △ repo 経由 | ○ optional-merge/Clear | ○ Command テスト | ○ IR-08/ID-2 | ○ undo 往復 | 中 (repo 依存で範囲が広がる) |
| ForkPlacementVariantUseCase | ✅ | △ repo 2 本 | ○ | △ Command テスト | ◎ straddle/IO-3 | ◎ Regions 落ち | 中 (跨ぎ + 補償ロジックで重い) |
| GridCanvas/Placement の永続化 | ✅ | ❌ | ○ | △ | ○ | △ | 不可 (EF 込み) |

## 推奨: ImageCropResolver + EffectiveCrop の意味

> 「ImageCropResolver / EffectiveCrop の意味だけを as-built BOM から再生成できるか」を Pattern 1 の最初の問いにする。

理由 (上軸 + GENERATION-GAP-REPORT の 8 次元評価):
- UI/Avalonia・EF を含まず、生成範囲が **Core(VO) + Application(resolver)** に自然に閉じる (Infra の Skia 走査はモック)。
- **precedence (ManualCrop 排他優先→AutoCrop→null、full-manual は短絡) が一意**で、IMAGE_VARIANT BOM の IR-04 + `IImageCropResolver` doc が宣言済。
- **等価判定 oracle が既存**: `tests/ViewGrid.Application.Tests/Services/ImageCropResolverTests.cs` の 5 件が precedence を網羅 (both-off→null / manual-wins + I/O 不発 / manual-full→null / auto-fallback / auto-null→null)。再生成物をこの 5 件に通すだけで precedence の合否が出る。
- **IMAGE_VARIANT の本質に近く、R-08 の 3 重実装 (IO-1) という観測済み drift に直接効く** — 「単一権威 resolver を BOM から正しく再生成できる」ことは、IO-1 を是正する際の参照実装にもなる。

## 代替: PlacementValidator
crop resolver が「IMAGE_VARIANT 寄り」なので、もし「GRID 側で Pattern 1 を試したい」場合の次点。純関数・DB 非依存・GRID BOM が AR-01/AR-02 で厚く規定・既存 `PlacementValidatorTests` + F-P3/F-P7 の anchor が oracle になる。ただし既に anchor が厚いため「新規 drift への効き」は crop resolver より薄い。

## 避けるべき最初の対象 (評価が大きすぎて切り分け不能)
- ViewGrid 全体の再生成
- Avalonia UI (View/axaml) の再生成
- EF Core 永続化込みの再生成
- GRID + IMAGE_VARIANT + RENDERING 横断の再生成

これらは失敗時に「方法論が弱い」のか「対象が重すぎる」のか分離できない。micro-pilot の目的 (方法論の検証) を達成できない。

## 結論
**F-P10 の対象 = ImageCropResolver + 関連 VO の実効クロップ解決の意味** で確定。実行計画は `crop-resolver-micro-pilot-plan.md`。
