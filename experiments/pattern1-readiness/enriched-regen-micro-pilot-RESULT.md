# Enriched-Spec Re-Generation Micro-Pilot — 結果 (F-P12 / d-3 検証)

> 実施 2026-06-01。Pattern 1 分解 d-3 の **検証工程**。
> 入力: `crop-resolver-spec.v2.md` (= v1 spec + d-3 generation_overlay の欠落次元補填 `rounding: ToEven` + 無効寸法 precondition)。
> 問い: **F-P10 で残った spec gap (ToPixelBbox の丸めモード) を generation_overlay で補填すると、blind 再生成の発散が消えて意味収束するか**。
> = 「BOM + generation_overlay を enrich すれば意味収束する」という d-3 の主張を *反証可能に* 検証する。

## 背景 (F-P10 の残課題)
F-P10 で blind 再生成は成立したが、**in-range (正寸法・oracle が踏む範囲) で観測された唯一の発散**が `CropFraction.ToPixelBbox` の丸めモードだった (無効寸法の第 2 発散は後に d-3/本 RESULT §範囲外で同定):
```
実装  : System.Math.Round(value)            → MidpointRounding.ToEven (銀行家丸め) 既定
v1生成: Math.Round(value, AwayFromZero)     → 中間値 (x.5) で発散
```
この発散は **既存 oracle が正寸法・非中間値しか踏まないため green をすり抜けた** (coverage 盲点)。d-3 はこれを `vo_method_contract.numeric.rounding` の欠落と同定し、`generation_overlay` 欄を提案した。F-P12 はその補填の有効性を実証する。

## 手順 (実施)
1. **enriched spec v2 を凍結** (`crop-resolver-spec.v2.md`): CropFraction 契約の意味差分は §0-1/§0-2 の 2 点のみ — (a) `ToPixelBbox` 丸めモード = `ToEven` 明記、(b) 入力 precondition = 正寸法 (負・0 は未定義)。加えて非意味の **scope 差分** (§0-3: 生成対象を CropFraction のみに絞り resolver を除外。F-P10 で resolver は収束済み)。CropFraction の他契約 (Full/IsFull/From/clamp 上限) は v1 と同一。
2. **oracle 硬化**: `CropFractionTests.cs` に midpoint falsifier `ToPixelBbox_Midpoint_Rounds_ToEven_AsBuilt` を追加 (`0.5×5=2.5 → ToEven=2 / AwayFromZero=3`、`0.5×1=0.5 → ToEven=0 / AwayFromZero=1`)。F-P10 が「丸めモードに頑健」に書いた盲点を *意図的に* 踏む。
3. **blind 再生成**: 独立生成器 (別 subagent) に **v2 spec のみ** を渡し (リポジトリ参照を禁止)、`CropFraction` を生成。生成器は **tool use 0 回 = src/tests を一切読まず** spec だけから生成 (真の blind を確認)。生成物 = `generated/CropFraction.v2.gen.cs`。
4. **3 段 swap 検証** (各々 swap → dotnet test → `git checkout` で revert)。

## 結果 — enriched spec で in-range 発散が消え、全スイート pass
| 段 | src の CropFraction | midpoint oracle | 全スイート | 意味 |
| --- | --- | --- | --- | --- |
| ① | **実装** (ToEven) | **PASS** (X=2) | Core 174 pass | 実装は ToEven。新 oracle が midpoint を被覆 |
| ② | **F-P10 v1 生成物** (AwayFromZero) | **FAIL** (X=3 を返す) | — | ★ 旧発散を新 oracle が決定的に捕捉 = 盲点を閉じた |
| ③ | **F-P12 v2 生成物** (ToEven) | **PASS** | **Core 174 / Application 466 (1 skip) すべて pass** | ★ enriched spec で blind 生成が ToEven に収束。oracle 範囲内 (正寸法 in-range) でドロップイン等価 (範囲外 clamp は別途相違、下記) |

→ **欠落を特定 (F-P10) → spec に `rounding: ToEven` を補填 (d-3) → blind 再生成 (F-P12) → 発散が消え midpoint oracle + 全スイートを通過。** revert 後 src clean。

## v1 生成物 vs v2 生成物 (in-range の唯一の意味差 = 丸めモード)
**正寸法 (precondition 内) では** v1/v2/実装の意味差は丸めモードのみ:
| 項目 (正寸法 in-range) | v1 (F-P10) | v2 (F-P12) | 実装 |
| --- | --- | --- | --- |
| precedence / 短絡 / IO / null / IsFull / From / clamp 上限 (残り) | ✓ | ✓ | ✓ |
| `ToPixelBbox` の丸め | `AwayFromZero` ✗ | **`MidpointRounding.ToEven`** ✓ | `Math.Round` 既定 = ToEven |
- v2 生成器は spec の `★ ToEven 厳守` 一語に従い `RoundToEven(value) => (int)Math.Round(value, MidpointRounding.ToEven)` を生成。**spec の自然語多義 (「四捨五入」) を機械的語彙 (ToEven) に替えただけで in-range の発散が消えた** = d-3 の `vo_method_contract` 提案が予測したとおり。

**範囲外 (precondition 外=無効寸法) では生成 helper の実装も相違する** (Codex review で同定、本 pilot のスコープ外):
- v1 生成物の `Clamp` は `if (max < min) max = min;` で無効区間を正規化、**v2 生成物の `Clamp` はこのガードを持たない**。実装 (`System.Math.Clamp`) は `min>max` で throw。→ 3 者とも範囲外挙動は異なる。これは d-3 §3.4 の「第 2 coverage 盲点」/決定点 D2b と同じ未決定領域で、本 pilot は precondition で範囲外を除外しているため検証していない (下記 caveat)。**つまり「唯一の差は丸め」は *in-range に限った* 主張**。

## 結論
- **d-3 の generation_overlay (rounding 補填) は有効**: F-P10 の発散は偶然でなく **観測可能な spec gap** であり、`vo_method_contract.numeric.rounding: ToEven` を足すだけで blind 再生成が **in-range/oracle 範囲で意味収束**した。「BOM + generation_overlay を enrich すれば (oracle が踏む範囲で) 意味収束する」という主張が **反証可能テスト (midpoint oracle) 付きで強化**された。**ただし範囲外 (無効寸法の clamp=D2b) と property/全域 conformance は未閉 (下記 caveat)**。
- **gap → 補填 → 収束の閉路 (in-range) が完成**: F-P8 (as-built BOM で drift 発見) → F-P9 (生成 gap audit) → F-P10 (blind 生成・gap 顕在化) → F-P11/d-3 (overlay スキーマ提案) → **F-P12 (補填して in-range 収束を実証)**。Pattern 1 の最小実証が一段閉じた (全域 conformance は別工程)。

## caveat (主張の境界 — 誇張しない)
- **収束は in-range (正寸法・丸め) に限る**。これは reproduction mode の収束 (= 現実装 ToEven の忠実再現) であり、ToEven が *意図された* 方針かは未決定 (決定点 D2a。midpoint oracle は「as-built 事実の固定」であって ToEven 方針の是認ではない、とテストに明記済)。
- **無効寸法 (負の width/height) の第 2 発散は本 pilot の対象外**: v2 spec は precondition で「正寸法のみ・範囲外は未定義」と明示し、oracle も範囲外を判定しない。全域 conformance には D2b (throw か正規化かを決める) が別途必要 (d-3 §7 のとおり未決定)。
- 「等価」は依然 *oracle カバレッジ範囲内*。midpoint oracle は丸めの盲点を 1 つ閉じたが、property/全域テストや C# conformance harness は defer 中の既知ギャップ。

## 成果物 / 永続物
- `crop-resolver-spec.v2.md` (凍結 enriched 入力、v1 との差分明示)。
- `generated/CropFraction.v2.gen.cs` (v2 blind 生成物の記録、非コンパイル)。
- `tests/ViewGrid.Core.Tests/Entities/CropFractionTests.cs` の **midpoint falsifier 追加** (永続。丸め盲点を閉じる oracle 硬化。Core.Tests 173→174)。
- 本 RESULT。実 src (`CropFraction.cs`) は変更なし (3 段 swap は各々 revert)。

## next
- **IO-1 是正** (ユーザー優先順位 2): crop 優先 3 重実装を、収束した resolver/CropFraction を唯一源に寄せて解消 → テストで固定。
- 2 例目 (PlacementValidator) で generation_overlay 形を枯らす (より難しい幾何ルールへ適用)。
- d-3 §7 の D2a (ToEven を deliberate 化するか) / D2b (無効寸法挙動を決めるか) は人間決定として保留。
