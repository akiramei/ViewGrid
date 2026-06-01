# Pattern 1 Readiness — Generation-Gap Report (F-P9 / d-0)

> 実施 2026-06-01。対象: GRID_COMPOSITION / IMAGE_VARIANT_MANAGEMENT の as-built BOM。
> 問い: **現在の as-built BOM は「監査地図」として強いが、「コード生成仕様」として何が足りないか**。
> 方針: いきなり再生成 (d) に進まず、Pattern 1 を分解 — (d-0) 本 gap audit → (d-1) 単一対象の micro-generation → (d-2) 既存 anchor/conformance で比較 → (d-3) 不足項目を BOM へフィードバック。

## 前提 — なぜ gap audit が先か
as-built BOM (GRID/IMAGE_VARIANT) が現に答えるのは **監査の問い**:
- 何を壊してはいけないか (rules, fragile)
- どの責任境界を越えてはいけないか (owns/does_not_own, decision_ownership, boundaries)
- どの不変条件が壊れやすいか (fragile + anchor_test)
- 実コードに *現に* 存在する乖離は何か (as_built_divergences)

一方 **生成の問い** には未対応:
- どの public API / 型 / メソッド signature を作るか
- エラーをどう返すか (ErrorOr / null / throw のどれか)
- null / optional の意味は何か
- どの依存 (DI) を持つか
- 生成範囲はどこまで (Core だけ? Application? Persistence? VM? UI?)
- **意味等価性を何で判定するか (test oracle)**

ここを飛ばして再生成すると、失敗時に「方法論が弱い」のか「対象が重すぎる」のか「仕様が曖昧」なのかを切り分けられない。本 report はその切り分けを *先に* 行う。

## 生成仕様の 8 次元と現 BOM の充足度

各次元を、ユーザー提示の判定項目に沿って評価する。判定は **crop resolver 周辺** (最初の micro-pilot 候補) を worked example にしつつ、一般傾向も併記する。

| # | 次元 | 監査地図 (現 BOM) | 生成仕様に必要な追加 | crop resolver での充足 |
| --- | --- | --- | --- | --- |
| 1 | **型定義** | VO/エンティティのフィールドと不変条件は列挙済 | 各 VO の *メソッド意味* (IsFull の tolerance, ToPixelBbox の round+clamp 規則, From 変換) | △ 型と field はあるが method 意味が BOM 外 (コードにのみ) |
| 2 | **メソッド signature** | UseCase/サービスの *責務* は記述、署名は非明示 | `IImageCropResolver.ResolveAsync(ImageCopy, ImageAsset, ct) → Task<CropFraction?>` 等の正確な署名 | △ I/F doc にあるが BOM には署名がない |
| 3 | **エラー表現** | 「失敗理由」は一部記載 (NotFound 等) だが表現規約が混在 | この層は **ErrorOr でなく `CropFraction?` の null で表現** (結果の失敗もクロップ無効も null。ただし null 引数は `ArgumentNullException` で前提ガード)。自己検証 VO は throw、UseCase は ErrorOr。**結果チャネルの規約が層で違う**ことの明文化 | △ null 規約が BOM に未記載 |
| 4 | **precedence 一意性** | IR-04 = ManualCrop 排他優先→AutoCrop→null | full-manual の **短絡** (ManualCrop が full なら AutoCrop に *落ちず* null) が要明示 | ◎ precedence 自体は IR-04 + I/F doc + 既存 5 テストで一意。短絡だけ補足要 |
| 5 | **null / optional 意味** | IR-05 = AutoCrop/ManualCrop は all-or-nothing (getter のみ保証) | 「ImageCopy.AutoCrop/ManualCrop の null = 機能 OFF」「CropFraction? null = クロップ無効 or 解決失敗 (両義)」 | ○ getter 挙動は IR-05 にあり。CropFraction? の両義性のみ補足要 |
| 6 | **依存 (DI)** | boundaries に依存先 Capability は記載 | 具体クラスの注入: `ImageCropResolver(IAutoCropBboxResolver, IImageStorage)`。AutoCrop 経路は I/O + cache | ○ boundaries.depends_on にある。具体 ctor まで落とせば十分 |
| 7 | **生成範囲** | Capability の owns/does_not_own で論理境界は明確 | 「再生成するのは Core(VO)+Application(resolver) のみ。Infra(Skia 走査)はモック、EF/VM/UI は範囲外」 | ◎ crop resolver は UI/EF を含まず範囲が自然に切れる |
| 8 | **test oracle** | anchor_test (fragile 不変条件) はあるが網羅 oracle ではない | 再生成物を *合否判定* する既存テスト集合の指定 + 不足分の追加 | ◎ `ImageCropResolverTests` (5 件) が precedence の強 oracle。VO の derived method のみ補強要 |

凡例: ◎=ほぼ充足 / ○=軽微な補足で充足 / △=BOM に欠落 (コードにのみ存在)。

## 一般的な gap (どの対象でも効く構造的欠落)

1. **エラー表現規約が層で不統一** — UseCase=`ErrorOr<T>`、自己検証 Domain VO (OccupySize/PixelSize/CellPosition)=`throw`、resolver/一部サービス=結果は `null` (前提違反のみ `ArgumentNullException`)。crop 系 VO は throw しない plain data。生成仕様は「この層は結果失敗をどの channel で返すか」を宣言しないと、再生成が誤った channel (例: resolver を ErrorOr 化) を選ぶ。BOM の各 rule に `error_channel` を足すのが最小修正。
2. **VO のメソッド意味が BOM 外** — フィールドと不変条件は BOM にあるが、`IsFull(tolerance=1e-6)` / `ToPixelBbox` の round+clamp といった *振る舞い* はコードにしかない。生成には「型 = フィールド + メソッド契約」が要る。
3. **「null の意味」が文脈依存で多義** — OFF を表す null と「解決失敗」を表す null が同じ型に同居 (CropFraction?)。生成仕様は両義を明示するか、型を分けるかを決める必要がある。
4. **test oracle の所在が未宣言** — どのテストが「意味等価の gate」かを BOM が指していない。anchor_test (fragile) は *一部*。再生成の合否を決めるには「対象ごとの oracle テスト集合」を宣言する欄が要る。
5. **生成範囲の宣言欄がない** — owns は *論理* 境界で、「再生成は Core+Application まで、Infra はモック、UI/EF は範囲外」という *物理* 範囲は別途要る。

## crop resolver を最初の対象にする根拠 (詳細は candidate-targets.md)
- 上表で **◎/○ が支配的**、△ も「コードにある契約を BOM に書き写す」だけで埋まる軽微なもの。
- **oracle が既存** (`ImageCropResolverTests` 5 件 = precedence を網羅、`ManualCropFractionTests` = VO 一部)。再生成物の合否を即判定できる。
- UI(Avalonia)/EF migration/VM を含まず、生成範囲が自然に閉じる。
- R-08 の drift リスク (IO-1: 3 重実装) に直接効く本質領域。

## 結論
- 現 BOM は **監査仕様としては production 級**だが、**生成仕様としては 8 次元中 3 次元 (型メソッド意味 / 署名 / エラー表現規約) が構造的に欠落**。残り 5 次元は crop resolver では既に充足 or 軽微補足で済む。
- したがって Pattern 1 は **「BOM を生成仕様へ全面格上げ」より「対象を絞って欠落次元だけ補い、既存 oracle で検証する micro-pilot」が低リスク**。最初の対象は crop resolver。
- 次工程: `crop-resolver-micro-pilot-plan.md` (F-P10) → 実施後に欠落次元 (型メソッド意味 / エラー規約 / oracle 宣言 / 生成範囲) を BOM スキーマへフィードバック (d-3)。

## スコープ (この audit でやらないこと)
実際の再生成 (F-P10) / BOM スキーマの全面改訂 / C# conformance harness の実装 / RENDERING 側 crop 適用の固め (別タスク)。本 report は *gap の同定* と *最初の対象選定* に限る。
