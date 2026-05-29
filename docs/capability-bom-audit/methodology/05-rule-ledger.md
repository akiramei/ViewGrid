# 05 - Rule Ledger

## この文書の目的

この文書では、Capability BOM Audit における `Rule Ledger` の役割を説明します。

Rule Ledger は、

> 「どの Rule が、どこで、どのように保証されているか」

を追跡するための台帳です。

Capability BOM Audit において、Rule Ledger は非常に重要です。

なぜなら、

```text id="jlwmrl"
コード構造
```

だけでは、

```text id="jlwmrm"
意味保証
```

を追跡できないからです。

---

# なぜ Rule Ledger が必要なのか

AI時代では、コード生成が非常に高速になります。

すると、次の問題が起きます。

```text id="jlwmrn"
- Validation が複数箇所へ複製される
- 同じRuleが微妙に異なる形で実装される
- UI層に意味制約が漏れる
- Rule保証場所が不明になる
- AIが局所修正を繰り返す
```

結果として、

```text id="jlwmro"
「このRuleは誰が保証しているのか」
```

が分からなくなります。

Rule Ledger は、この問題を解決するためのものです。

---

# Rule Ledger とは何か

Rule Ledger は、

> Rule の存在・保証場所・検証状態を追跡する台帳

です。

重要なのは、

```text id="jlwmrp"
Rule = Validation一覧
```

ではないことです。

Rule Ledger は、

```text id="jlwmrq"
意味制約の観測台帳
```

です。

---

# Rule とは何か

Capability BOM Audit における Rule は、

> ソフトウェアが保証しなければならない意味制約

です。

---

## Rule の例

```text id="jlwmrr"
- PlacementMustFitWithinGrid
- PlacementsMustNotOverlap
- ManualCropOverridesAutoCrop
- UndoMustRestorePreviousObservableState
```

---

# Rule Ledger が扱うもの

Rule Ledger は通常、次を記録します。

| 項目                   | 意味                              |
| -------------------- | ------------------------------- |
| Rule ID              | Rule識別子                         |
| Capability           | 所属Capability                    |
| Rule Type            | invariant / validation / policy |
| Description          | Rule説明                          |
| Decision Type        | 関連Decision                      |
| Expected Enforcement | 本来の保証場所                         |
| Enforced By          | 実際の保証場所                         |
| Tested By            | テスト場所                           |
| Observed In          | 観測場所                            |
| Status               | 検証状態                            |

---

# Rule Ledger の中心思想

Capability BOM Audit における Rule Ledger の中心思想はこれです。

```text id="jlwmrs"
Rule が存在すること
```

ではなく、

```text id="rlwmm1"
どこがRuleを保証しているか
```

を追跡することです。

---

# Rule Type

Capability BOM Audit v0.1 では、次の Rule Type を扱います。

| Type                 | 意味          |
| -------------------- | ----------- |
| invariant            | 常に成立すべき意味制約 |
| validation           | 入力・状態妥当性    |
| policy               | 業務方針        |
| lifecycle_constraint | 状態遷移制約      |
| consistency_rule     | 整合性維持       |

---

# invariant

## 定義

```text id="rlwmm2"
常に成立しなければならない意味制約
```

---

## 例

```text id="rlwmm3"
- PlacementMustFitWithinGrid
- PlacementsMustNotOverlap
```

---

## 特徴

invariant は通常、

```text id="rlwmm4"
domain meaning
```

に近い。

---

# validation

## 定義

```text id="rlwmm5"
入力や状態の妥当性制約
```

---

## 例

```text id="rlwmm6"
- offset range validation
- invalid selection prevention
```

---

## 注意

validation は UI validation と混同されやすいですが、

Capability BOM Audit では、

```text id="rlwmm7"
意味 validation
```

を重視します。

---

# policy

## 定義

```text id="rlwmm8"
業務的・設計的方針
```

---

## 例

```text id="rlwmm9"
- ManualCropOverridesAutoCrop
- CopyPropertiesMayAffectMultiplePlacements
```

---

## 特徴

policy は単なる validation ではありません。

```text id="rlwmma"
どう振る舞うべきか
```

を定義します。

---

# lifecycle_constraint

## 定義

```text id="rlwmmb"
状態遷移に関する制約
```

---

## 例

```text id="rlwmmc"
- DeletedPlacementCannotBeEdited
- ExportedSessionCannotBeModified
```

---

# consistency_rule

## 定義

```text id="rlwmmd"
複数状態間の整合性制約
```

---

## 例

```text id="rlwmme"
- RenderedOutputMustMatchCanvasGeometry
- UndoStateMustMatchObservableState
```

---

# Rule Ledger の基本構造

Capability BOM Audit v0.1 の基本構造：

```yaml id="rlwmmf"
rules:
  - id:
    capability:
    type:
    description:
    decision_type:
    expected_enforcement:
    enforced_by:
    tested_by:
    observed_in:
    status:
```

---

# Rule ID

## 定義

Rule を識別する一意ID。

---

## 推奨

```text id="rlwmmg"
意味ベース命名
```

を推奨します。

---

## 良い例

```text id="rlwmmh"
PlacementMustFitWithinGrid
ManualCropOverridesAutoCrop
```

---

## 悪い例

```text id="rlwmmi"
CheckPlacement1
ValidateOffset2
```

---

# Capability

## 定義

Rule が所属する意味能力。

---

## 例

```yaml id="rlwmmj"
capability: GRID_COMPOSITION
```

---

# Decision Type

## 定義

その Rule がどの種類の意味判断に関係するか。

---

## 例

```yaml id="rlwmmk"
decision_type:
  - validation_decision
  - domain_decision
```

---

# Expected Enforcement

## 定義

```text id="rlwmml"
本来どこで保証されるべきか
```

---

## 重要

Capability BOM Audit は、

```text id="rlwmmm"
現在どこにあるか
```

だけでなく、

```text id="rlwmmn"
本来どこにあるべきか
```

も記録します。

---

## 例

```yaml id="rlwmmo"
expected_enforcement:
  - Placement validator
  - GridComposition use case
```

---

# Enforced By

## 定義

```text id="rlwmmp"
実際にRuleを保証している場所
```

---

## 例

```yaml id="rlwmmq"
enforced_by:
  - GridPlacementValidator
```

---

## 未確認の場合

```yaml id="rlwmmr"
enforced_by:
  - unknown
```

---

## 重要

`unknown` は悪ではありません。

むしろ：

```text id="rlwmms"
Rule保証場所が未確認
```

という重要情報です。

---

# Tested By

## 定義

```text id="rlwmmt"
Ruleを検証しているテスト
```

---

## 例

```yaml id="rlwmmu"
tested_by:
  - GridCompositionTests
  - PlacementValidationTests
```

---

## 未確認

```yaml id="rlwmmv"
tested_by:
  - unknown
```

---

# Observed In

## 定義

```text id="rlwmmw"
Ruleが観測されたRuntime component
```

---

## 重要

`observed_in` は、

```text id="rlwmmx"
Rule保証
```

とは違います。

例えば：

```text id="rlwmmy"
ViewModel に validation branch が存在する
```

ことを記録できます。

---

## 例

```yaml id="rlwmmz"
observed_in:
  - GridWorkspaceViewModel
```

---

# Status

## 定義

```text id="rlwmn0"
Rule保証状態
```

---

# Status 一覧

| Status             | 意味         |
| ------------------ | ---------- |
| unverified         | 保証場所未確認    |
| partially_verified | 一部確認済み     |
| verified           | 保証とテスト確認済み |
| duplicated         | 複数箇所重複     |
| misplaced          | 不適切な場所で保証  |
| obsolete           | 現在不要       |

---

# unverified

## 意味

```text id="rlwmn1"
どこが保証しているか未確認
```

---

## 重要

これは非常に重要な状態です。

Capability BOM Audit は、

```text id="rlwmn2"
unknown を可視化する
```

ための方法論でもあります。

---

# duplicated

## 意味

```text id="rlwmn3"
同じRuleが複数箇所に実装されている
```

---

## 例

```text id="rlwmn4"
- ViewModel validation
- Domain validation
- Renderer validation
```

---

## 危険性

AI時代では duplicated が増えやすい。

理由：

```text id="rlwmn5"
AIが局所修正を繰り返すから
```

---

# misplaced

## 意味

```text id="rlwmn6"
Rule保証場所が不自然
```

---

## 例

```text id="rlwmn7"
ViewModel が placement validity を保証
```

---

## 重要

これは Capability BOM Audit の重要検出対象です。

---

# Rule Leakage

## 定義

```text id="rlwmn8"
本来別Capabilityに属するRuleが、
別Runtime componentへ漏れている状態
```

---

## 例

```text id="rlwmn9"
UI層へ domain validation が漏れる
```

---

# Rule Concentration

## 定義

```text id="rlwmna"
1つのRuntime componentに
大量のRule enforcementが集中する状態
```

---

## 例

```text id="rlwmnb"
GridWorkspaceViewModel が
多数のvalidationを持つ
```

---

## 注意

Rule concentration は即悪ではありません。

重要なのは：

```text id="rlwmnc"
- accidental complexity か
- temporary adaptation か
- domain meaning leakage か
```

です。

---

# Rule と Decision の関係

Rule は、

```text id="rlwmnd"
保証されるべき制約
```

です。

Decision は、

```text id="rlwmne"
そのRuleを誰が扱うか
```

です。

---

## 例

```text id="rlwmnf"
Rule:
  PlacementMustFitWithinGrid

Decision:
  誰がそれを保証するか
```

---

# Rule と Runtime Mapping の関係

Runtime Mapping は、

```text id="rlwmng"
どのコードが何に関与しているか
```

を記録します。

Rule Ledger は、

```text id="rlwmnh"
どのRuleがどこで保証されるか
```

を記録します。

---

## 違い

| Runtime Mapping | Rule Ledger |
| --------------- | ----------- |
| Capabilityとの関係  | Ruleとの関係    |
| Role中心          | Guarantee中心 |
| 関与観測            | 制約観測        |

---

# Rule Ledger の重要性

Capability BOM Audit において Rule Ledger は、

```text id="rlwmni"
意味保証の地図
```

です。

AI時代では、

```text id="rlwmnj"
コードそのもの
```

より、

```text id="rlwmnk"
Rule guarantee structure
```

の方が重要になる場合があります。

---

# Rule Ledger と AI監査

Capability BOM Audit では、AIに対して：

```text id="rlwmnl"
このRuleはどこで保証されているか
```

を監査させます。

---

## 良い依頼

```text id="rlwmnm"
unverified の Rule を抽出し、
保証候補・テスト候補・未確認理由を分類してください。
コード修正は禁止です。
```

---

## 悪い依頼

```text id="rlwmnn"
validation を整理してください
```

これはAIが勝手に実装変更を始めやすい。

---

# Rule Ledger の核心

Rule Ledger の目的は、

```text id="rlwmno"
Validation一覧を作ること
```

ではありません。

本当の目的は、

```text id="rlwmnp"
意味保証の所在を追跡可能にすること
```

です。

---

# 次に読むべき文書

次は以下を読むと理解が深まります。

```text id="rlwmnq"
06-runtime-mapping.md
07-overreach-detection.md
08-viewmodel-audit-example.md
09-ai-audit-prompt-guide.md
```

