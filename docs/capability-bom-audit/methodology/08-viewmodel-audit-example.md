# 08 - ViewModel Audit Example

## この文書の目的

この文書では、Capability BOM Audit を実際にどのように適用するかを、`GridWorkspaceViewModel` を例に説明します。

重要：

この文書の目的は、

```text id="vma001"
ViewModel を分割すること
```

ではありません。

本当の目的は、

```text id="vma002"
- どのCapabilityに関与しているか
- どのRoleを持っているか
- どのDecisionを持っているか
- どこにOverreachの疑いがあるか
```

を観測することです。

---

# 前提

対象：

```text id="vma003"
GridWorkspaceViewModel.cs
```

想定Capability：

```text id="vma004"
- GRID_COMPOSITION
- PLACEMENT_EDITING
- IMAGE_VARIANT_MANAGEMENT
- GRID_LAYOUT_CONTROL
- RENDERING_EXPORT
- HISTORY_MANAGEMENT
- SESSION_PERSISTENCE
```

---

# Capability BOM Audit の考え方

Capability BOM Audit では、

```text id="vma005"
巨大ViewModel = 悪
```

とは考えません。

重要なのは：

```text id="vma006"
そのViewModelが
何を決めているか
```

です。

---

# 最初に行うこと

最初にコードを修正しません。

まず行うのは：

```text id="vma007"
- Capability inventory
- Role classification
- Decision classification
- Rule observation
- Overreach observation
```

です。

---

# STEP 1 - Capability Inventory

まず、

```text id="vma008"
GridWorkspaceViewModel が
どのCapabilityに触っているか
```

を棚卸しします。

---

# 例

```yaml id="vma009"
file:
  path: src/ViewGrid.Application/ViewModels/GridWorkspaceViewModel.cs

  mapped_capabilities:
    - GRID_COMPOSITION
    - PLACEMENT_EDITING
    - IMAGE_VARIANT_MANAGEMENT
    - GRID_LAYOUT_CONTROL
    - RENDERING_EXPORT
    - HISTORY_MANAGEMENT
    - SESSION_PERSISTENCE
```

---

# 重要

ここでは：

```text id="vma010"
Capability が多い
```

こと自体は問題にしません。

Capability BOM Audit の中心原則：

```text id="vma011"
関与していることは問題ではない。
意思決定を所有していることが問題になり得る。
```

---

# STEP 2 - Role Classification

次に：

```text id="vma012"
そのCapabilityにどう関与しているか
```

を分類します。

---

# 例

```yaml id="vma013"
GRID_COMPOSITION:
  roles:
    - coordinates
    - invokes
    - projects
```

---

# 解釈

これは比較的自然な可能性があります。

例えば：

```text id="vma014"
- Placement編集UseCaseを呼ぶ
- UI表示へ射影する
- 選択状態を調停する
```

など。

---

# 別の例

```yaml id="vma015"
RENDERING_EXPORT:
  roles:
    - invokes
    - coordinates
```

---

# 解釈

```text id="vma016"
Export command を呼ぶ
```

だけなら自然な可能性があります。

---

# STEP 3 - Decision Classification

次に：

```text id="vma017"
何を決めているか
```

を観測します。

---

# 例

```yaml id="vma018"
decisions:
  - type: ui_interaction_decision
    status: acceptable
```

---

# 解釈

例えば：

```text id="vma019"
button click → ExportGrid
```

は通常自然です。

---

# 別の例

```yaml id="vma020"
decisions:
  - type: workflow_decision
    status: acceptable_with_note
```

---

# 解釈

例えば：

```text id="vma021"
Placement変更後に Historyへ積む
```

は workflow coordination の可能性があります。

---

# STEP 4 - Suspicious Decision Observation

ここが重要です。

Capability BOM Audit では：

```text id="vma022"
疑い
```

を記録します。

即断定しません。

---

# 例

```yaml id="vma023"
suspected_overreach:
  - id: SO-001

    decision_type:
      - validation_decision

    suspected_role:
      - enforces

    description:
      placement validity を
      ViewModel が直接判定している可能性

    status: unclear
```

---

# なぜ unclear なのか

静的コードだけでは：

```text id="vma024"
- temporary adaptation
- UI convenience
- migration code
```

などを区別できないからです。

---

# STEP 5 - Evidence Collection

Capability BOM Audit は、

```text id="vma025"
印象レビュー
```

ではありません。

必ず：

```text id="vma026"
- method
- branch
- condition
- call
```

などの根拠を残します。

---

# 例

```yaml id="vma027"
evidence:
  - method: MoveSelectedPlacement

    observation:
      placement validity branch が存在する可能性

    related_rule:
      - PlacementMustFitWithinGrid
```

---

# STEP 6 - Rule Observation

次に：

```text id="vma028"
Rule がどこで観測されるか
```

を確認します。

---

# 例

```yaml id="vma029"
related_rules:
  - PlacementMustFitWithinGrid
  - PlacementsMustNotOverlap
```

---

# 重要

ここで重要なのは：

```text id="vma030"
Rule が存在する
```

ではなく、

```text id="vma031"
どこがRuleを保証しているか
```

です。

---

# 例

## 自然な可能性

```text id="vma032"
Validator が Rule enforcement
```

---

## 疑わしい可能性

```text id="vma033"
ViewModel が validation semantics を保持
```

---

# STEP 7 - Decision Ownership Observation

ここが最重要です。

Capability BOM Audit の核心は：

```text id="vma034"
どこが意思決定 ownership を持つか
```

です。

---

# 例

## 軽い関与

```text id="vma035"
ViewModel が UseCase を呼ぶ
```

これは ownership ではない可能性があります。

---

## 強い ownership

```text id="vma036"
ViewModel が
「fork が必要」
を決める
```

これは：

```text id="vma037"
domain_decision ownership
```

の可能性があります。

---

# ViewModel Audit の典型的観測結果

Capability BOM Audit v0.1 では、ViewModel に対して通常：

---

# allowed

```yaml id="vma038"
roles:
  - observes
  - projects
  - invokes
```

---

# acceptable_with_note

```yaml id="vma039"
roles:
  - coordinates

decisions:
  - workflow_decision
```

---

# suspicious

```yaml id="vma040"
roles:
  - owns
  - enforces
  - persists
  - renders

decisions:
  - domain_decision
  - validation_decision
  - persistence_decision
  - rendering_decision
  - history_decision
```

---

# 実例 - Placement Editing

## 比較的自然

```text id="vma041"
選択中Placementに対して
MovePlacement を呼ぶ
```

---

## 疑わしい

```text id="vma042"
placement validity を
ViewModel が直接判定
```

---

## さらに疑わしい

```text id="vma043"
fork policy を
ViewModel が直接決定
```

---

# 実例 - Rendering Export

## 比較的自然

```text id="vma044"
Export command を呼ぶ
```

---

## 疑わしい

```text id="vma045"
trim semantics を
ViewModel が保持
```

---

# 実例 - History Management

## 比較的自然

```text id="vma046"
操作後に Undo stack へ積む
```

---

## 疑わしい

```text id="vma047"
Undo meaning unit を
ViewModel が直接決定
```

---

# Audit Output Example

Capability BOM Audit の結果は通常：

```text id="vma048"
runtime_mapping.yaml
```

と：

```text id="vma049"
overreach_report.yaml
```

へ出力されます。

---

# Runtime Mapping Example

```yaml id="vma050"
file:
  path: GridWorkspaceViewModel.cs

  mapped_capabilities:

    GRID_COMPOSITION:
      roles:
        - coordinates
        - invokes
        - projects

      decisions:
        - type: workflow_decision
          status: acceptable_with_note

    PLACEMENT_EDITING:
      roles:
        - coordinates
        - invokes

      suspected_overreach:
        - id: SO-001

          decision_type:
            - validation_decision

          suspected_role:
            - enforces

          status: unclear
```

---

# Overreach Report Example

```yaml id="vma051"
findings:
  - id: OVR-001

    capability: PLACEMENT_EDITING

    decision_type:
      - validation_decision

    observed_role:
      - enforces

    expected_role:
      - coordinates
      - invokes

    status: suspected

    evidence:
      - method: MoveSelectedPlacement

        observation:
          placement validity branch が存在する可能性
```

---

# 重要 - この段階ではコード修正しない

Capability BOM Audit の重要原則：

```text id="vma052"
観測を先に行う
```

です。

---

# なぜか

AI時代では：

```text id="vma053"
「綺麗そうな構造」
```

へ飛びつくと危険だからです。

Capability BOM Audit は：

```text id="vma054"
意味構造
```

を先に観測します。

---

# 悪い流れ

```text id="vma055"
巨大VM発見
  ↓
即分割
  ↓
責務不明
  ↓
意味漏れ
```

---

# 良い流れ

```text id="vma056"
Capability inventory
  ↓
Role classification
  ↓
Decision observation
  ↓
Rule observation
  ↓
Overreach observation
  ↓
必要なら refactor proposal
```

---

# なぜ ViewGrid が良い教材なのか

ViewGrid は：

```text id="vma057"
- UI
- ViewModel
- Undo/Redo
- Rendering
- Layout
- Persistence
- Logical copy
```

を持っています。

つまり：

```text id="vma058"
Role
Decision
Rule
Capability
Overreach
```

が全部観測できます。

---

# 特に重要な題材

```text id="vma059"
Logical Copy
```

です。

ImageCopy は：

```text id="vma060"
単なる画像データ
```

ではなく、

```text id="vma061"
意味的派生物
```

だからです。

---

# Capability BOM Audit の核心

ViewModel Audit の目的は、

```text id="vma062"
ViewModel を小さくすること
```

ではありません。

本当に重要なのは：

```text id="vma063"
どこに意思決定 ownership があるか
```

を追跡可能にすることです。

---

# 次に読むべき文書

次は以下を読むと理解が深まります。

```text id="vma064"
09-ai-audit-prompt-guide.md
10-common-misunderstandings.md
```
