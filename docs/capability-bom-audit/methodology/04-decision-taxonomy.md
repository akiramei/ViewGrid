# 04 - Decision Taxonomy

## この文書の目的

この文書では、Capability BOM Audit における `Decision` の意味を定義します。

Decision は、

> 「そのコードが何を決めているか」

を表します。

Capability BOM Audit において、Decision は最重要概念の1つです。

---

# なぜ Decision が必要なのか

従来のソフトウェア設計では、

```text id="k5n1d5"
責務
```

という言葉が広く使われてきました。

しかし責務だけでは、次を区別しにくい場合があります。

```text id="3sv9if"
- UI状態の変換
- ワークフロー調停
- Rule保証
- ドメイン判断
- 永続化方針
- 出力意味
```

例えば：

```text id="mjlwm1"
ViewModel が ExportGrid を呼ぶ
```

のは自然かもしれません。

しかし：

```text id="jlwmm2"
ViewModel が
- export geometry
- trim semantics
- persistence boundary
```

を決めていた場合、それは別問題です。

Capability BOM Audit は、

> 「何を行っているか」

だけでなく、

> 「何を決めているか」

を観測します。

---

# Decision の基本原則

Decision taxonomy の中心原則は次です。

```text id="jlwmm3"
関与していることは問題ではない。
意思決定を所有していることが問題になり得る。
```

Decision taxonomy は、

```text id="jlwmm4"
- どの判断が
- どこで
- なぜ
- 誰によって
```

行われているかを追跡するために使います。

---

# Decision と Role の違い

これは非常に重要です。

---

## Role

Role は、

```text id="jlwmm5"
そのCapabilityにどう関与しているか
```

です。

例：

```text id="jlwmm6"
- invokes
- projects
- coordinates
```

---

## Decision

Decision は、

```text id="jlwmm7"
何を決めているか
```

です。

例：

```text id="jlwmm8"
- validation_decision
- workflow_decision
- rendering_decision
```

---

## 例

```yaml id="jlwmm9"
roles:
  - coordinates

decisions:
  - workflow_decision
```

これは自然な可能性があります。

---

## 別の例

```yaml id="jlwmma"
roles:
  - owns

decisions:
  - domain_decision
```

これは強い意味判断です。

---

# Decision 一覧

Capability BOM Audit v0.1 では、次の Decision を定義します。

| Decision                | 意味                |
| ----------------------- | ----------------- |
| domain_decision         | ドメイン意味を決める        |
| workflow_decision       | UseCase順序や分岐を決める  |
| validation_decision     | 入力や状態の妥当性を決める     |
| persistence_decision    | 保存単位や復元方針を決める     |
| ui_interaction_decision | UI操作をUseCaseへ変換する |
| rendering_decision      | 出力・描画意味を決める       |
| history_decision        | Undo/Redo意味を決める   |

---

# domain_decision

## 定義

```text id="jlwmmb"
ドメイン上の意味、所有、状態、関係を決める
```

---

## 例

```text id="jlwmmc"
- placement validity
- variant sharing policy
- logical copy ownership
- lifecycle transition
- fork requirement
```

---

## 重要

`domain_decision` は最も重要な Decision の1つです。

なぜなら：

```text id="jlwmmd"
ソフトウェアの意味
```

そのものに関係するからです。

---

## 期待される場所

通常：

```text id="jlwmme"
- Capability
- UseCase
- Domain Service
- Policy
- Domain Model
```

などに存在することが期待されます。

---

## 疑わしい例

```text id="jlwmmf"
ViewModel が
「fork が必要」
を直接決める
```

---

## より自然な例

```text id="jlwmmg"
PlacementEditingCapability が
fork policy を持つ
```

---

# workflow_decision

## 定義

```text id="jlwmmh"
UseCase の順序、分岐、組み合わせを決める
```

---

## 例

```text id="jlwmmi"
- Validation後にSaveする
- Placement変更後にHistoryへ積む
- Export前にRender更新する
```

---

## 特徴

`workflow_decision` は比較的自然です。

特に：

```text id="jlwmmj"
- ViewModel
- Coordinator
- Application Service
```

では普通に存在します。

---

## 注意点

ただし、

```text id="jlwmmk"
workflow_decision
```

が増えすぎると、

```text id="jlwmml"
domain_decision
```

へ変質する場合があります。

---

## 許容される例

```text id="jlwmmm"
Save前にValidationを呼ぶ
```

---

## 疑わしい例

```text id="jlwmmn"
variant fork 条件を UI flow 内で決める
```

---

# validation_decision

## 定義

```text id="jlwmmo"
入力や状態が許容されるかを決める
```

---

## 例

```text id="jspx1d"
- placement overlap check
- offset range validation
- trim mode constraint
- session compatibility
```

---

## 重要

Capability BOM Audit では、

```text id="jlwmmp"
どこが Rule を保証しているか
```

を非常に重視します。

---

## 期待される場所

通常：

```text id="jlwmmq"
- Validator
- UseCase
- Domain Model
- Rule Engine
```

などです。

---

## 疑わしい例

```text id="jlwmmr"
ViewModel が placement validity を直接判定
```

---

# persistence_decision

## 定義

```text id="jlwmms"
何を、どの粒度で、いつ保存・復元するかを決める
```

---

## 例

```text id="jlwmmt"
- session save boundary
- serialization unit
- migration policy
- restore semantics
```

---

## 注意

`persistence_decision` は単なる I/O ではありません。

重要なのは、

```text id="jlwmmu"
保存意味
```

を持つ点です。

---

## 疑わしい例

```text id="jlwmmv"
ViewModel が
保存対象の意味単位を決める
```

---

# ui_interaction_decision

## 定義

```text id="jlwmmw"
UI操作をどのUseCaseへ変換するかを決める
```

---

## 例

```text id="jlwmmx"
- button click → ExportGrid
- drag operation → MovePlacement
- selection state → command enablement
```

---

## 特徴

これは比較的自然な Decision です。

通常：

```text id="jlwmmy"
- ViewModel
- UI Controller
```

などに存在します。

---

## 重要

Capability BOM Audit は、

```text id="jlwmmz"
UI interaction
```

自体を問題視しません。

問題なのは、

```text id="jlwmn0"
UI interaction
```

の中に、

```text id="jlwmn1"
domain_decision
```

が混ざることです。

---

# rendering_decision

## 定義

```text id="jlwmn2"
表示・画像・出力意味を決める
```

---

## 例

```text id="jlwmn3"
- export geometry
- canvas size
- trim behavior
- transparent region handling
```

---

## 注意

`rendering_decision` は単なる描画ではありません。

重要なのは、

```text id="jlwmn4"
出力意味
```

を決めていることです。

---

## 疑わしい例

```text id="jlwmn5"
ViewModel が
trim semantics を直接決める
```

---

# history_decision

## 定義

```text id="jlwmn6"
Undo/Redo の意味単位や復元方針を決める
```

---

## 例

```text id="jlwmn7"
- undo unit
- redo replay semantics
- transient state handling
- command grouping
```

---

## 重要

Undo/Redo は単なる履歴ではありません。

```text id="jlwmn8"
「何を元に戻すか」
```

という意味判断を含みます。

---

## 疑わしい例

```text id="jlwmn9"
ViewModel が
Undo意味単位を直接決める
```

---

# Allowed / Acceptable / Suspicious

Decision taxonomy では、Decision を3段階で扱います。

---

# Allowed

通常自然な Decision。

---

## 代表

```text id="jlwmna"
- ui_interaction_decision
```

---

## 理由

これは通常：

```text id="jlwmnb"
UI adaptation
```

だからです。

---

# Acceptable With Note

許容可能だが複雑化に注意。

---

## 代表

```text id="jlwmnc"
- workflow_decision
```

---

## 理由

workflow coordination は自然ですが、

```text id="jlwmnd"
domain meaning
```

が混ざり始めると危険です。

---

# Suspicious

Capability 境界を越えている可能性。

---

## 代表

```text id="jlwmne"
- domain_decision
- validation_decision
- persistence_decision
- rendering_decision
- history_decision
```

---

## 理由

これらは通常、

```text id="jlwmnf"
意味 ownership
```

を含むからです。

---

# Decision Ownership

Capability BOM Audit の核心概念の1つです。

---

## 定義

Decision ownership は、

```text id="jlwmng"
どこが最終的な意味判断を持つか
```

を表します。

---

## 例

### 軽い関与

```text id="jlwmnh"
ViewModel が ExportGrid を呼ぶ
```

これは ownership ではない可能性があります。

---

## 強い ownership

```text id="jlwmni"
ViewModel が
「この状態では export してよい」
を決める
```

これは decision ownership の可能性があります。

---

# Decision Leakage

## 定義

```text id="jlwmnj"
本来別Capabilityに属するDecisionが、
別の場所へ漏れている状態
```

---

## 例

```text id="jlwmnk"
- UI層へ domain_decision が漏れる
- rendering_decision が workflow層へ漏れる
- persistence_decision が ViewModelへ漏れる
```

---

# Decision Concentration

## 定義

```text id="jlwmnl"
1つのRuntime componentに
多くのDecisionが集中する状態
```

---

## 例

```text id="jlwmnm"
GridWorkspaceViewModel が
- workflow
- validation
- persistence
- rendering
- history
```

を全部決める。

---

## 注意

Decision concentration は即悪ではありません。

重要なのは：

```text id="jlwmnn"
- accidental complexity か
- 本当に必要か
- テスト可能か
- Capability境界が崩れていないか
```

です。

---

# Decision と Rule の関係

Rule は、

```text id="jlwmno"
保証されるべき意味制約
```

です。

Decision は、

```text id="jlwmnp"
そのRuleをどう扱うか
```

に関係します。

---

## 例

```text id="jlwmnq"
Rule:
  PlacementMustFitWithinGrid

Decision:
  どこがそのRuleを保証するか
```

---

# Decision と AI監査

Capability BOM Audit では、AIを：

```text id="jlwmnr"
コード修正者
```

ではなく、

```text id="jlwmns"
Decision測量者
```

として使います。

---

## 例

悪い依頼：

```text id="jlwmnt"
この巨大ViewModelをリファクタしてください
```

---

## 良い依頼

```text id="jlwmnu"
このViewModelが、
どのDecisionを所有しているか分類してください
```

---

# Capability BOM Audit の核心

Decision taxonomy の目的は、

```text id="jlwmnv"
コードを綺麗にすること
```

ではありません。

本当の目的は、

```text id="jlwmnw"
意味判断の所在を追跡可能にすること
```

です。

---

# 次に読むべき文書

次は以下を読むと理解が深まります。

```text id="jlwmnx"
05-rule-ledger.md
06-runtime-mapping.md
07-overreach-detection.md
08-viewmodel-audit-example.md
```

