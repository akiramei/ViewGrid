# 06 - Runtime Mapping

## この文書の目的

この文書では、Capability BOM Audit における `Runtime Mapping` の意味と役割を説明します。

Runtime Mapping は、

> 「意味構造」と「実際のコード構造」を接続する観測台帳

です。

Capability BOM Audit において、Runtime Mapping は非常に重要です。

なぜなら、

```text id="rtm001"
Capability
Rule
Decision
```

だけでは、

```text id="rtm002"
実際にどのコードが何をしているか
```

が分からないからです。

---

# Runtime Mapping とは何か

Runtime Mapping は、

> Capability と Runtime component の関係を観測するための台帳

です。

ただし重要なのは、

```text id="rtm003"
Runtime Mapping ≠ dependency graph
```

という点です。

目的は依存解析ではありません。

本当に見たいのは：

```text id="rtm004"
- どのCapabilityに触っているか
- どう関与しているか
- 何を決めているか
```

です。

---

# Runtime Mapping の中心思想

Capability BOM Audit における Runtime Mapping の中心思想はこれです。

```text id="rtm005"
関係があることは問題ではない。
意思決定を所有していることが問題になり得る。
```

つまり Runtime Mapping は、

```text id="rtm006"
依存の地図
```

ではなく、

```text id="rtm007"
意思決定の観測地図
```

です。

---

# Runtime Component とは何か

Runtime Mapping が扱う対象は、通常次のような Runtime component です。

```text id="rtm008"
- ViewModel
- Service
- UseCase
- Domain Model
- Validator
- Renderer
- Repository
- Controller
- Coordinator
```

---

# Runtime Mapping が扱うもの

Runtime Mapping は通常、次を記録します。

| 項目                  | 意味                |
| ------------------- | ----------------- |
| file                | Runtime component |
| mapped_capabilities | 関与Capability      |
| roles               | どう関与するか           |
| decisions           | 何を決めているか          |
| suspected_overreach | 越境疑い              |
| evidence            | 観測根拠              |
| status              | 判定状態              |

---

# Runtime Mapping の目的

Runtime Mapping の目的は、

```text id="rtm009"
クラス分割
```

ではありません。

本当の目的は、

```text id="rtm010"
意味構造と実装構造のズレを観測すること
```

です。

---

# Runtime Mapping の基本構造

Capability BOM Audit v0.1 の基本構造：

```yaml id="rtm011"
files:
  - path:
    mapped_capabilities:
```

---

# 基本例

```yaml id="rtm012"
files:
  - path: src/ViewGrid.Application/ViewModels/GridWorkspaceViewModel.cs

    mapped_capabilities:
      GRID_COMPOSITION:
        roles:
          - coordinates
          - invokes
          - projects

        decisions:
          - type: workflow_decision
            status: acceptable

      PLACEMENT_EDITING:
        roles:
          - coordinates
          - invokes

        suspected_overreach:
          - decision_type:
              - domain_decision
```

---

# mapped_capabilities

## 定義

```text id="rtm013"
そのRuntime componentが関与しているCapability
```

---

## 重要

Capability BOM Audit では、

```text id="rtm014"
複数Capabilityに関与している
```

こと自体は問題ではありません。

---

## 問題なのは

```text id="rtm015"
複数Capabilityの意思決定を所有している
```

ことです。

---

# roles

## 定義

```text id="rtm016"
そのCapabilityにどう関与しているか
```

---

## 例

```yaml id="rtm017"
roles:
  - observes
  - projects
  - invokes
```

---

## 詳細

Role の詳細は：

```text id="rtm018"
03-role-taxonomy.md
```

を参照。

---

# decisions

## 定義

```text id="rtm019"
そのRuntime componentが行っている意味判断
```

---

## 例

```yaml id="rtm020"
decisions:
  - type: workflow_decision
```

---

## 詳細

Decision の詳細は：

```text id="rtm021"
04-decision-taxonomy.md
```

を参照。

---

# Runtime Mapping の重要な視点

Runtime Mapping は、

```text id="rtm022"
「何に触っているか」
```

ではなく、

```text id="rtm023"
「何を決めているか」
```

を見る。

---

# 例

## 自然な例

```text id="rtm024"
ViewModel が ExportGrid を呼ぶ
```

これは：

```yaml id="rtm025"
roles:
  - invokes

decisions:
  - ui_interaction_decision
```

の可能性があります。

---

## 疑わしい例

```text id="rtm026"
ViewModel が export geometry を決める
```

これは：

```yaml id="rtm027"
roles:
  - owns

decisions:
  - rendering_decision
```

の可能性があります。

---

# suspected_overreach

## 定義

```text id="rtm028"
意思決定越境の疑い
```

---

## 重要

Capability BOM Audit は、

```text id="rtm029"
即断罪しない
```

ことを重視します。

そのため：

```yaml id="rtm030"
suspected_overreach:
```

という形で、

```text id="rtm031"
疑い
```

を記録します。

---

# なぜ「疑い」なのか

静的コードだけでは：

```text id="rtm032"
- temporary adaptation
- accidental complexity
- migration code
- UI integration
```

などを完全には判断できないからです。

---

# 例

```yaml id="rtm033"
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

# evidence

## 定義

```text id="rtm034"
観測根拠
```

---

## 重要

Capability BOM Audit は、

```text id="rtm035"
印象レビュー
```

ではありません。

必ず：

```text id="rtm036"
- method
- branch
- call
- condition
```

などの根拠を残します。

---

# 例

```yaml id="rtm037"
evidence:
  - method: MoveSelectedPlacement
    observation:
      placement validity branch が存在する可能性
```

---

# status

## 定義

```text id="rtm038"
観測状態
```

---

# status 一覧

| Status               | 意味     |
| -------------------- | ------ |
| acceptable           | 自然な関与  |
| acceptable_with_note | 注意付き許容 |
| suspicious           | 越境疑い   |
| confirmed_overreach  | 越境確認済み |
| unclear              | 根拠不足   |
| rejected             | 疑い否定   |

---

# acceptable

## 意味

```text id="rtm039"
現在の場所に自然に存在する
```

---

## 例

```text id="rtm040"
ViewModel が button click を UseCase に変換
```

---

# acceptable_with_note

## 意味

```text id="rtm041"
許容可能だが複雑化注意
```

---

## 代表

```text id="rtm042"
workflow_decision
```

---

## 例

```text id="rtm043"
複数UseCaseを順序制御
```

---

# suspicious

## 意味

```text id="rtm044"
Capability境界を越えている可能性
```

---

## 例

```text id="rtm045"
ViewModel が variant fork policy を決定
```

---

# confirmed_overreach

## 定義

```text id="rtm046"
意思決定越境が確認済み
```

---

## 重要

これは：

```text id="rtm047"
「悪」
```

を意味しません。

重要なのは：

```text id="rtm048"
- なぜそこにあるか
- temporaryか
- migrationか
- accidental complexityか
```

です。

---

# unclear

## 定義

```text id="rtm049"
根拠不足
```

---

## 重要

Capability BOM Audit は、

```text id="rtm050"
unknown を残す
```

ことを重視します。

---

# Runtime Mapping と Rule Ledger の違い

これは重要です。

---

# Runtime Mapping

Runtime Mapping は：

```text id="rtm051"
どのコードが
どのCapabilityに
どう関与しているか
```

を見る。

---

# Rule Ledger

Rule Ledger は：

```text id="rtm052"
どのRuleが
どこで保証されているか
```

を見る。

---

# 比較

| Runtime Mapping | Rule Ledger    |
| --------------- | -------------- |
| Capability中心    | Rule中心         |
| Runtime観測       | Constraint観測   |
| Role/Decision   | Guarantee/Test |
| 実装との対応          | 意味保証との対応       |

---

# Runtime Mapping と静的解析の違い

Capability BOM Audit は通常の静的解析とは違います。

---

# 静的解析

通常：

```text id="rtm053"
- 依存
- complexity
- nullability
- style
```

を見る。

---

# Runtime Mapping

Runtime Mapping は：

```text id="rtm054"
- 意味境界
- Decision ownership
- Rule leakage
- Capability overlap
```

を見る。

---

# Runtime Mapping の核心

Runtime Mapping の核心は、

```text id="rtm055"
「どこにコードがあるか」
```

ではありません。

本当に重要なのは：

```text id="rtm056"
「どこに意思決定があるか」
```

です。

---

# ViewModel Audit における Runtime Mapping

ViewModel 監査では通常：

```yaml id="rtm057"
allowed:
  - observes
  - projects
  - invokes

acceptable_with_note:
  - coordinates

suspicious:
  - owns
  - enforces
  - persists
  - renders
```

を基準にします。

---

# 例

## 自然

```yaml id="rtm058"
roles:
  - projects
  - invokes

decisions:
  - ui_interaction_decision
```

---

## 疑わしい

```yaml id="rtm059"
roles:
  - owns

decisions:
  - domain_decision
```

---

# Runtime Mapping と AI監査

Capability BOM Audit では、AIを：

```text id="rtm060"
コード修正者
```

ではなく、

```text id="rtm061"
Runtime観測者
```

として使います。

---

# 良い依頼

```text id="rtm062"
GridWorkspaceViewModel が、
どのCapabilityに対して、
どのRoleとDecisionを持っているか分類してください。
コード修正は禁止です。
```

---

# 悪い依頼

```text id="rtm063"
このViewModelを綺麗にリファクタしてください
```

これはAIが：

```text id="rtm064"
見た目の綺麗さ
```

へ向かいやすい。

---

# Runtime Mapping の核心

Runtime Mapping の目的は、

```text id="rtm065"
依存構造を綺麗にすること
```

ではありません。

本当の目的は、

```text id="rtm066"
意味構造と意思決定の所在を
追跡可能にすること
```

です。

---

# 次に読むべき文書

次は以下を読むと理解が深まります。

```text id="rtm067"
07-overreach-detection.md
08-viewmodel-audit-example.md
09-ai-audit-prompt-guide.md
10-common-misunderstandings.md
```

