# 02 - Core Concepts

## この文書の目的

この文書は、Capability BOM Audit を理解するために必要な基本概念を整理するためのものです。

Capability BOM Audit は、既存のソフトウェア設計用語と似た言葉を使いますが、意味が異なる場合があります。

特に重要なのは、

```text id="1tdpfo"
- Capability
- Role
- Decision
- Rule
- Runtime Mapping
- Overreach
```

です。

この文書では、それぞれを明確に区別します。

---

# 全体像

Capability BOM Audit は、ソフトウェアを次の構造として観測します。

```text id="7aef6j"
Capability
  ↓
UseCase
  ↓
Rule
  ↓
Decision
  ↓
Runtime Mapping
  ↓
Overreach Detection
```

重要なのは、

> コード構造ではなく、意味構造を見る

ことです。

---

# Capability

## 定義

Capability は、

> ソフトウェアが持つ意味的能力

です。

Capability は、クラスでもサービスでもありません。

---

## Capability の例

ViewGrid では次のようなものが Capability になります。

```text id="j1pzq4"
- GRID_COMPOSITION
- PLACEMENT_EDITING
- IMAGE_VARIANT_MANAGEMENT
- GRID_LAYOUT_CONTROL
- RENDERING_EXPORT
- HISTORY_MANAGEMENT
```

---

## Capability は Runtime 構造ではない

Capability は意味境界です。

これは非常に重要です。

```text id="5x7t6n"
Capability ≠ Microservice
Capability ≠ Assembly
Capability ≠ Namespace
Capability ≠ Layer
```

Capability は、

> 「そのソフトウェアが何をできるか」

を表します。

---

## Capability の特徴

Capability は通常、次を持ちます。

```text id="g9r6lu"
- purpose
- use_cases
- rules
- entities
- events
```

---

## Capability の例

```yaml id="s2g7zh"
capability:
  id: GRID_COMPOSITION

  purpose:
    画像コピーをグリッド上のセル領域に配置し、
    合成対象として管理する

  use_cases:
    - PlaceImageCopy
    - MovePlacement
    - SwapPlacements

  rules:
    - PlacementMustFitWithinGrid
    - PlacementsMustNotOverlap
```

---

# UseCase

## 定義

UseCase は、

> Capability が提供する操作単位

です。

---

## UseCase は UI 操作ではない

例えば：

```text id="f1rv1y"
「ボタンを押す」
```

は UseCase ではありません。

一方：

```text id="wn2m13"
- MovePlacement
- ExportGrid
- SaveSession
```

は UseCase です。

---

## UseCase の役割

UseCase は通常、

```text id="6e9x2l"
- 入力を受ける
- Ruleを適用する
- 状態変更する
- Eventを発行する
```

を行います。

---

# Rule

## 定義

Rule は、

> ソフトウェアが保証しなければならない意味制約

です。

---

## Rule の例

```text id="9pwl4z"
- PlacementMustFitWithinGrid
- PlacementsMustNotOverlap
- ManualCropOverridesAutoCrop
- UndoMustRestorePreviousObservableState
```

---

## Rule は Validation だけではない

Rule は単なる入力チェックではありません。

Rule には、

```text id="nnkkmf"
- invariant
- validation
- policy
- lifecycle constraint
- consistency rule
```

などがあります。

---

## Rule の重要性

Capability BOM Audit では、

> Rule がどこで保証されているか

を非常に重視します。

例えば：

```text id="kkgqj3"
- ViewModel
- Domain Model
- UseCase
- Validator
- Renderer
```

のどこで Rule が保証されているかを観測します。

---

# Role

## 定義

Role は、

> コードが Capability にどう関与しているか

を表します。

---

## Role の例

```text id="vydc7w"
- observes
- projects
- invokes
- coordinates
- enforces
- owns
- persists
- renders
```

---

## 重要な点

Capability BOM Audit では、

```text id="4znl1n"
複数Capabilityに関与している
```

ことは問題ではありません。

問題なのは、

```text id="3vsmjk"
複数Capabilityの意思決定を所有している
```

ことです。

---

## 例

```yaml id="7stmln"
roles:
  - observes
  - projects
  - invokes
```

これは自然な ViewModel の可能性があります。

一方：

```yaml id="0kgc0q"
roles:
  - owns
  - enforces
```

が複数 Capability にまたがる場合、注意が必要です。

---

# Decision

## 定義

Decision は、

> そのコードが何を決めているか

を表します。

Capability BOM Audit の中心概念です。

---

## なぜ Decision が必要なのか

従来の設計では、

```text id="g3l4gi"
責務
```

という言葉が広く使われていました。

しかし責務だけでは、

```text id="c7u6q0"
- UI射影
- Rule保証
- ワークフロー制御
- 永続化方針
```

を区別しにくい。

そのため Capability BOM Audit では、

> 「何を決めているか」

を別軸で記録します。

---

## Decision の種類

代表的なもの：

```text id="a9g0q1"
- domain_decision
- workflow_decision
- validation_decision
- persistence_decision
- ui_interaction_decision
- rendering_decision
- history_decision
```

> **第三カテゴリ (Step 5 昇格)**: ALLOWED / FORBIDDEN の二分に加え、AI 実装時に「決めてよいが**記録必須**」の
> `MUST_DECIDE_AND_DOCUMENT` がある (生成方向で AI が決めざるを得ない実装決定の追跡)。詳細は `12-must-decide-and-document.md`。

---

## 例

### 許容されやすいもの

```text id="j6q4ql"
ボタン押下で ExportGrid を呼ぶ
```

これは `ui_interaction_decision`。

---

### 疑わしいもの

```text id="9pqdbt"
ViewModel が
- placement validity
- variant fork policy
- undo semantics
- rendering geometry
```

を決める。

これは `domain_decision` や `validation_decision` の可能性があります。

---

# Runtime Mapping

## 定義

Runtime Mapping は、

> Capability と実装コードの関係を観測する台帳

です。

---

## Runtime Mapping は依存表ではない

重要：

```text id="8ybqkq"
Runtime Mapping ≠ dependency graph
```

目的は、

```text id="rr2h3y"
- どのCapabilityに触るか
- どう関与するか
- 何を決めているか
```

を記録することです。

---

## Runtime Mapping の例

```yaml id="ynf0pc"
file:
  path: GridWorkspaceViewModel.cs

  mapped_capabilities:
    GRID_COMPOSITION:
      roles:
        - coordinates
        - invokes
        - projects

      decisions:
        - workflow_decision
```

---

# Overreach

## 定義

Overreach は、

> 本来その場所にあるべきでない意思決定を持っている状態

です。

---

## Overreach の例

例えば：

```text id="snbydr"
ViewModel が
- placement validity
- persistence policy
- rendering geometry
```

を決める。

これは Capability 側へ戻す候補かもしれません。

---

## Overreach は即「悪」ではない

Capability BOM Audit は設計警察ではありません。

そのため、

```text id="e1cbfi"
overreach detected
```

は即バグを意味しません。

重要なのは：

```text id="3jrk9z"
- なぜそこにあるのか
- 本当に必要か
- 一時的か
- accidental complexity か
```

を観測することです。

---

# Responsibility と Decision の違い

これは最重要概念の1つです。

---

## Responsibility

Responsibility は、

> 何を行うか

です。

例：

```text id="jlwmvt"
- ExportGrid を呼ぶ
- 状態を画面表示する
- イベント購読する
```

---

## Decision

Decision は、

> 何を決めるか

です。

例：

```text id="zobvr0"
- export geometry
- variant fork policy
- placement validity
- undo semantics
```

---

## なぜ区別するのか

Capability BOM Audit は、

```text id="d0wwz8"
責務が多い
```

ことより、

```text id="ihc1dg"
意思決定が漏れている
```

ことを重視します。

---

# Allowed / Suspicious / Acceptable With Note

Capability BOM Audit では、すべてを厳格禁止しません。

---

## Allowed

自然な関与。

例：

```text id="q6uhyx"
- observes
- projects
- invokes
```

---

## Acceptable With Note

許容可能だが、複雑化に注意。

例：

```text id="7s7j46"
- coordinates
- workflow_decision
```

---

## Suspicious

Capability 境界を越えている可能性。

例：

```text id="9pjlr0"
- domain_decision
- validation_decision
- persistence_decision
```

---

# Capability BOM Audit の核心

Capability BOM Audit の中心原則はこれです。

```text id="6rly7t"
関与していることは問題ではない。
意思決定を所有していることが問題になり得る。
```

---

# Capability BOM Audit が目指すもの

Capability BOM Audit は、

```text id="vbjlwm"
- 完璧なレイヤ分離
- 巨大クラス撲滅
- マイクロサービス化
```

を目的としません。

本当に目指しているのは、

```text id="xv85kv"
AI時代でも、
人間が意味構造を追跡できる状態
```

です。

---

# 次に読むべき文書

次は以下を読むと理解が進みます。

```text id="dbpr5e"
03-role-taxonomy.md
04-decision-taxonomy.md
05-rule-ledger.md
06-runtime-mapping.md
```

