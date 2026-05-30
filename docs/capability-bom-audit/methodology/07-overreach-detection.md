# 07 - Overreach Detection

## この文書の目的

この文書では、Capability BOM Audit における `Overreach Detection` の意味と役割を説明します。

Overreach Detection は、

> 「本来その場所にあるべきではない意思決定」

を観測するための監査です。

Capability BOM Audit において、Overreach Detection は中心的な概念です。

---

# なぜ Overreach Detection が必要なのか

従来の設計レビューでは、次のような指標がよく使われていました。

```text id="ovr001"
- クラスサイズ
- メソッド数
- レイヤ違反
- 依存方向
- 循環参照
```

もちろん重要です。

しかしAI時代では、次の問題が増えます。

```text id="ovr002"
- UI層へ意味判断が漏れる
- validation が複数箇所へ分散
- workflow と domain が混ざる
- rendering semantics がUIへ入る
- persistence policy が ViewModel に漏れる
- Undo意味がUIイベントへ埋まる
```

つまり、

> 「何を決めているか」

が重要になります。

---

# Overreach とは何か

Overreach とは、

> 本来別Capabilityや別層に属する意思決定を持っている状態

です。

重要：

```text id="ovr003"
Overreach ≠ 大きいクラス
```

です。

---

# Capability BOM Audit の核心

Capability BOM Audit の中心原則：

```text id="ovr004"
関与していることは問題ではない。
意思決定を所有していることが問題になり得る。
```

Overreach Detection は、

```text id="ovr005"
意思決定 ownership の越境
```

を観測します。

---

# Overreach と Responsibility の違い

従来の設計では、

```text id="ovr006"
責務過多
```

という言葉がよく使われました。

しかし Capability BOM Audit では、より重要なのは：

```text id="ovr007"
Decision ownership
```

です。

---

# 例

## 自然な関与

```text id="ovr008"
ViewModel が ExportGrid を呼ぶ
```

これは：

```yaml id="ovr009"
roles:
  - invokes

decisions:
  - ui_interaction_decision
```

の可能性があります。

---

## Overreach の可能性

```text id="ovr010"
ViewModel が
- export geometry
- trim semantics
- placement validity
```

を決める。

これは：

```yaml id="ovr011"
roles:
  - owns

decisions:
  - rendering_decision
  - validation_decision
```

の可能性があります。

---

# Overreach Detection の目的

Overreach Detection の目的は、

```text id="ovr012"
コードを綺麗にすること
```

ではありません。

本当の目的は、

```text id="ovr013"
意味境界の崩れを観測すること
```

です。

> **事前防御との補完 (Step 5 昇格)**: 本章は overreach の **事後検出**。実装前の **事前防御** は
> 三層構造 (`11-three-layer-disambiguation.md`) が担う。両者は補完関係 (事前に塞ぎ、事後に観測する)。

---

# Capability BOM Audit は設計警察ではない

これは非常に重要です。

Overreach Detection は、

```text id="ovr014"
即バグ判定
```

ではありません。

Capability BOM Audit は、

```text id="ovr015"
測量器
```

であって、

```text id="ovr016"
断罪器
```

ではありません。

---

# なぜ即断罪しないのか

現実のソフトウェアには：

```text id="ovr017"
- migration code
- temporary adaptation
- UI integration
- legacy compatibility
- experimental flow
```

などがあります。

そのため：

```text id="ovr018"
見た瞬間に「悪」と断定しない
```

ことが重要です。

---

# Overreach Detection の中心視点

Overreach Detection では、次を観測します。

| 観測対象                   | 内容              |
| ---------------------- | --------------- |
| Decision ownership     | 何を決めているか        |
| Rule enforcement       | Ruleをどこで保証しているか |
| Capability leakage     | Capability境界漏れ  |
| Decision concentration | 意思決定集中          |
| Runtime mismatch       | 意味構造とのズレ        |

---

# Decision Ownership

## 定義

```text id="ovr019"
最終的な意味判断を誰が持つか
```

---

## 例

### 軽い関与

```text id="ovr020"
ViewModel が MovePlacement を呼ぶ
```

これは ownership ではない可能性があります。

---

### 強い ownership

```text id="ovr021"
ViewModel が
「fork が必要」
を決める
```

これは：

```text id="ovr022"
domain_decision ownership
```

の可能性があります。

---

# Rule Leakage

## 定義

```text id="ovr023"
Rule保証が本来の場所から漏れている状態
```

---

## 例

```text id="ovr024"
ViewModel が placement validity を保証
```

---

## なぜ危険か

Rule leakage が起きると：

```text id="ovr025"
- Rule重複
- Rule不一致
- AI局所修正
- hidden validation
```

が発生しやすい。

---

# Capability Leakage

## 定義

```text id="ovr026"
本来別Capabilityに属する意味判断が混入する状態
```

---

## 例

```text id="ovr027"
GRID_COMPOSITION ViewModel に
SESSION_PERSISTENCE semantics が入る
```

---

# Decision Concentration

## 定義

```text id="ovr028"
1つのRuntime componentに
大量の意思決定が集中する状態
```

---

## 例

```text id="ovr029"
GridWorkspaceViewModel が
- workflow
- validation
- rendering
- persistence
- history
```

を全部決める。

---

# 注意

Decision concentration は即悪ではありません。

重要なのは：

```text id="ovr030"
- accidental complexity か
- temporary adaptation か
- 本当に必要か
```

です。

---

# Runtime Mismatch

## 定義

```text id="ovr031"
意味構造と実装構造が一致していない状態
```

---

## 例

```text id="ovr032"
Capability:
  GRID_COMPOSITION

しかし実際:
  UI layer が geometry semantics を保持
```

---

# Overreach の分類

Capability BOM Audit v0.1 では、Overreach を次のように扱います。

| Status               | 意味     |
| -------------------- | ------ |
| acceptable           | 自然な配置  |
| acceptable_with_note | 注意付き許容 |
| suspicious           | 越境疑い   |
| confirmed_overreach  | 越境確認済み |
| unclear              | 根拠不足   |
| rejected             | 疑い否定   |

---

# acceptable

## 意味

```text id="ovr033"
その場所に自然に存在する
```

---

## 例

```text id="ovr034"
ViewModel が button click を UseCase に変換
```

---

# acceptable_with_note

## 意味

```text id="ovr035"
許容可能だが複雑化注意
```

---

## 代表

```text id="ovr036"
workflow_decision
```

---

## 例

```text id="ovr037"
複数UseCaseの調停
```

---

# suspicious

## 意味

```text id="ovr038"
意味 ownership が漏れている可能性
```

---

## 例

```text id="ovr039"
ViewModel が validation semantics を保持
```

---

# confirmed_overreach

## 定義

```text id="ovr040"
意思決定越境が確認済み
```

---

## 重要

これは：

```text id="ovr041"
直ちに修正すべき
```

を意味しません。

Capability BOM Audit は、

```text id="ovr042"
観測
```

を優先します。

---

# unclear

## 定義

```text id="ovr043"
証拠不足
```

---

## 重要

Capability BOM Audit は、

```text id="ovr044"
unknown を残す
```

ことを重視します。

---

# rejected

## 定義

```text id="ovr045"
疑いが否定された
```

---

## 例

```text id="ovr046"
workflow coordination だっただけで、
domain ownership は存在しなかった
```

---

# Overreach と Layer Violation の違い

これは重要です。

---

# Layer Violation

通常：

```text id="ovr047"
依存方向
```

を見る。

---

# Overreach

Overreach は：

```text id="ovr048"
意味 ownership
```

を見る。

---

# 例

## Layer violation ではない

```text id="ovr049"
ViewModel → Service
```

---

## しかし Overreach の可能性

```text id="ovr050"
ViewModel が domain semantics を保持
```

---

# 大きいクラス ≠ Overreach

Capability BOM Audit は：

```text id="ovr051"
行数
```

では判断しません。

---

# 重要なのは

```text id="ovr052"
どのDecisionを所有しているか
```

です。

---

# 例

## 大きいが自然

```yaml id="ovr053"
roles:
  - observes
  - projects
  - invokes
  - coordinates
```

---

## 小さいが危険

```yaml id="ovr054"
roles:
  - owns

decisions:
  - domain_decision
```

---

# ViewModel Audit における典型的 Overreach

Capability BOM Audit v0.1 では、ViewModel に対して通常次を疑います。

---

# suspicious decision types

```text id="ovr055"
- domain_decision
- validation_decision
- persistence_decision
- rendering_decision
- history_decision
```

---

# suspicious roles

```text id="ovr056"
- owns
- enforces
- persists
- renders
```

---

# 例

## 疑わしい

```text id="ovr057"
ViewModel が
「この編集は fork 必須」
を決める
```

---

## 比較的自然

```text id="ovr058"
ViewModel が
ForkPlacementVariant を呼ぶ
```

---

# Overreach Report

Overreach Detection の結果は通常：

```text id="ovr059"
overreach report
```

として出力されます。

---

# 例

```yaml id="ovr060"
findings:
  - id: OVR-001

    capability: PLACEMENT_EDITING

    decision_type:
      - domain_decision

    observed_role:
      - owns

    expected_role:
      - coordinates
      - invokes

    status: suspected

    evidence:
      - method: MoveSelectedPlacement
        observation:
          fork requirement branch が存在する可能性
```

---

# Overreach Detection と AI監査

Capability BOM Audit では、AIを：

```text id="ovr061"
コード修正者
```

ではなく、

```text id="ovr062"
意味構造監査者
```

として使います。

---

# 良い依頼

```text id="ovr063"
このViewModelが、
どのDecisionを所有しているか分類してください。
コード修正は禁止です。
```

---

# 悪い依頼

```text id="ovr064"
この巨大ViewModelを綺麗にしてください
```

これはAIが：

```text id="ovr065"
見た目
```

を優先しやすい。

---

# Overreach Detection の核心

Overreach Detection の目的は、

```text id="ovr066"
完璧な分離
```

ではありません。

本当の目的は、

```text id="ovr067"
意味境界と意思決定の所在を
観測可能にすること
```

です。

---

# 次に読むべき文書

次は以下を読むと理解が深まります。

```text id="ovr068"
08-viewmodel-audit-example.md
09-ai-audit-prompt-guide.md
10-common-misunderstandings.md
```

