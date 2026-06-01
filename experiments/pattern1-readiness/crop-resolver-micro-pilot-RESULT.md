# Crop Resolver Micro-Pilot — 結果 (F-P10、実施・実証済)

> 実施 2026-06-01。Pattern 1 (BOM からの再生成) の最初の micro-pilot。
> 計画: `crop-resolver-micro-pilot-plan.md` / 入力 spec: `crop-resolver-spec.md`。
> 問い: **as-built BOM (+ 最小の gap 補填) から、意味等価な crop resolver を blind 再生成できるか**。

## 手順 (実施)
1. `crop-resolver-spec.md` を凍結 (BOM の IR-04/IR-05/IR-06 + GENERATION-GAP の欠落次元補填。実装コード片なし)。
2. **独立生成器** (別 agent) に **spec ファイルのみ** を渡し、`src/`・`tests/` を読ませず `CropFraction` + `ImageCropResolver` を blind 生成 (生成物 = `generated/{CropFraction,ImageCropResolver}.gen.cs`)。
3. oracle 補強として `tests/ViewGrid.Core.Tests/Entities/CropFractionTests.cs` を新規追加 (8 Fact、実 CropFraction で全 green 確認。VO method 契約を固定)。
4. **swap 検証**: 生成物を実 src (`CropFraction.cs` / `ImageCropResolver.cs`) へ一時上書き → 全テスト実行 → `git checkout` で復元。

## 結果 — 生成物は全スイートを通過 (drop-in 等価)
| 検証 | 結果 |
| --- | --- |
| ビルド (生成物を src に swap) | 成功 (public 表面・署名が一致 = 呼び出し側 Renderer/View/UseCase がそのままコンパイル) |
| ViewGrid.Core.Tests | **173 pass / 0 fail** (新 CropFractionTests が *生成* CropFraction に対して green を含む) |
| ViewGrid.Application.Tests | **466 pass / 1 skip / 0 fail** (`ImageCropResolverTests` 5 件 + renderer + VM が *生成* 物に対して green) |

→ **spec のみから blind 生成したコードが、既存スイート全 639 件 (+1 skip) の意味等価判定を通過。** 復元後も green。src clean。

## 生成物 vs 実装の意味比較
| 項目 | 実装 | 生成 | 等価性 |
| --- | --- | --- | --- |
| resolver precedence (ManualCrop 排他優先→AutoCrop→null) | ✓ | ✓ | 完全一致 |
| ManualCrop full の **短絡** (AutoCrop に落ちない) | ✓ | ✓ | 完全一致 |
| AutoCrop 経路の I/O 条件 (ManualCrop/both-off では走査しない) | ✓ | ✓ | 完全一致 |
| 結果チャネル = `CropFraction?` の null (例外化しない) | ✓ | ✓ | 完全一致 |
| 前提ガード `ResolveAsync` の `ArgumentNullException.ThrowIfNull` | ✓ | ✓ | 完全一致 |
| ctor 引数 null チェック | なし (primary ctor) | **あり** (防御的) | 軽微差 (テスト非依存) |
| CropFraction `IsFull` / `From` / `Full` | ✓ | ✓ | 完全一致 |
| CropFraction `ToPixelBbox` の **丸めモード** | `Math.Round` 既定 = **ToEven (銀行家丸め)** | **AwayFromZero** | ★ 中間値 (x.5) で発散。既存テストは踏まない |

## d-3 フィードバック — BOM へ戻すべき欠落次元 (生成器が誤った/曖昧と申告した点)
F-P9 が「型のメソッド意味」を欠落次元と予測した通り、**数値メソッドの精密仕様**が spec gap として顕在化:

1. **[最重要] `ToPixelBbox` の丸めモード未指定** — spec は「round (四捨五入)」とだけ書いた。実装は `Math.Round` 既定 (ToEven)、生成器は「四捨五入」を `AwayFromZero` と解釈。**中間値で結果が変わる**。既存テストが中間値を踏まないため swap は green だったが、これは *カバレッジ盲点* かつ *spec 不足* の二重の穴。→ BOM の `vo_method_contract` に丸めモード (ToEven/AwayFromZero) を明記すべき。
2. **ctor 引数ガード方針が未指定** — 生成器は ctor でも `ArgumentNullException` を防御的に追加 (実装は primary ctor で ctor チェックなし、`ResolveAsync` の ThrowIfNull のみ)。→ 「前提ガードは結果メソッド入口のみか、ctor も含むか」を規約化。
3. **入力ドメインの正規化方針が未指定** — 負の比率や >1 の Width/Height に対する正規化要否が未規定。生成器は clamp に委ねた。→ 「入力比率は呼出側が [0,1] を保証する前提か」を明記。

## 8 次元の判定 (F-P9 の gap audit に照らして)
| 次元 | 結果 |
| --- | --- |
| precedence 一意性 | ◎ spec で一意。生成完全一致 |
| null / optional 意味 | ◎ 一致 |
| DI 依存 | ◎ 一致 (ctor 署名どおり) |
| 生成範囲 | ◎ Core(VO)+Application(resolver) に閉じ、Infra/EF/VM/UI を巻き込まず |
| メソッド署名 | ◎ 補填で十分 (生成物がドロップインでコンパイル) |
| test oracle | ◎ 既存 `ImageCropResolverTests` が precedence を網羅。CropFractionTests を追加して VO も固定 |
| エラー表現 | ○ 補填で十分 (null チャネル + 前提ガードを明示したら一致) |
| **型のメソッド意味** | △ **補填してもなお不足** — 丸めモードのような数値精密仕様が抜けた。生成は *振る舞いは合うが厳密には発散* |

## 結論
- **Pattern 1 micro-generation は実現可能**: 適切に境界された oracle-backed な対象 (crop resolver) なら、as-built BOM + 最小 gap 補填から **意味等価 (既存スイート全通過) なコードを blind 再生成できた**。
- **残る本質ギャップは「数値メソッドの精密仕様」**: 丸めモードのように *テストで踏まれない細部* は、BOM にも spec にも乗りにくく、生成で発散する。Pattern 1 を広げるには BOM スキーマに `vo_method_contract` (丸め/精度/境界) と `error_channel` / `ctor_guard` の欄を足すのが次の投資。
- **caveat**: 「等価」は *既存テストカバレッジの範囲内*。丸めの発散が示す通り、カバレッジは全挙動を固定しない。フル conformance には property/midpoint テスト (または C# conformance harness) が要る — これは defer 中の既知ギャップと整合。

## 成果物 / 永続物
- `generated/{CropFraction,ImageCropResolver}.gen.cs` (生成物の記録、非コンパイル)。
- `tests/ViewGrid.Core.Tests/Entities/CropFractionTests.cs` (**永続追加**、8 Fact — CropFraction VO の oracle。実コードの coverage 改善でもある)。
- 本 RESULT + `crop-resolver-spec.md` (凍結入力)。
- 実 src は変更なし (swap は検証後 revert)。

## next
- d-3 をスキーマ提案に具体化 (BOM に `vo_method_contract`/`error_channel`/`ctor_guard`/`oracle_tests`/`generation_scope` 欄)。
- 別対象での 2 例目 (例: PlacementValidator) で「生成可能性」の再現性を見る。
- IO-1 (crop 優先の 3 重実装) の是正に、本 micro-pilot で再生成した単一 resolver を参照実装として使う案。
