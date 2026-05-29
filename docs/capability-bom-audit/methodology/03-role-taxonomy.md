# 03 - Role Taxonomy

## この文書の目的

この文書では、Capability BOM Audit における `Role` の意味を定義します。

Role は、

> 「そのコードが Capability にどう関与しているか」

を表します。

これは従来の「責務分類」と似ていますが、目的が異なります。

Capability BOM Audit における Role は、

```text id="g9j0px"
コード構造を分類するため
```

ではなく、

```text id="hf4j70"
意思決定の所在を観測するため
```

に使われます。

---

# なぜ Role が必要なのか

Capability BOM Audit では、

```text id="15x8gx"
このコードはどのCapabilityに関係しているか
```

だけでは不十分です。

重要なのは、

```text id="jjlwmu"
そのCapabilityに対して、
どう関与しているか
```

です。

例えば：

```text id="d3vyl7"
ViewModel が ExportGrid を呼ぶ
```

のは自然です。

しかし：

```text id="09wyqv"
ViewModel が
- export geometry
- trim policy
- rendering boundary
```

を決めているなら、それは別問題です。

Role taxonomy は、この違いを観測するためのものです。

---

# Role の基本原則

Capability BOM Audit における Role の核心は次です。

```text id="7igv7r"
関与していることは問題ではない。
意思決定を所有していることが問題になり得る。
```

Role は、

```text id="3sdq1n"
- 安全な関与
- 注意が必要な関与
- 意思決定の越境
```

を区別するために使います。

---

# Role 一覧

Capability BOM Audit v0.1 では、次の Role を定義します。

| Role        | 意味                        |
| ----------- | ------------------------- |
| observes    | 状態変更やイベントを購読する            |
| projects    | UIや表示用に状態を射影する            |
| invokes     | UseCaseやServiceを呼び出す      |
| coordinates | 複数CapabilityやUseCaseを調停する |
| enforces    | Ruleやvalidationを保証する      |
| owns        | 意味判断や状態遷移を所有する            |
| persists    | 保存・復元境界を扱う                |
| renders     | 表示・出力形式へ変換する              |

---

# observes

## 定義

```text id="t5bqlt"
状態変更やイベントを購読する
```

---

## 例

```text id="6zdrjv"
- PropertyChanged を購読
- SelectionChanged を監視
- Event を受信してUI更新
```

---

## リスク

通常は低リスクです。

`observes` は、

```text id="o3bywo"
何かを決めている
```

わけではないからです。

---

## 例

```yaml id="4b8vk5"
roles:
  - observes
```

---

# projects

## 定義

```text id="66a0vm"
UIや表示用に状態を整形・射影する
```

---

## 例

```text id="mpp7r7"
- SelectedPlacement を ViewState に変換
- 表示用文字列を生成
- VisibleItems を計算
- UI向け DTO を作る
```

---

## 重要

`projects` は、

```text id="0xjfnn"
意味判断
```

ではなく、

```text id="mw4i9q"
表示変換
```

です。

---

## 許容される例

```text id="nsh1be"
CellSize → UI座標へ変換
```

---

## 注意点

次のようなものは `projects` ではなくなる可能性があります。

```text id="4oq7ql"
- placement validity を決める
- export geometry を決める
- variant fork を決める
```

---

# invokes

## 定義

```text id="1m3u5v"
UseCase、Service、Command を呼び出す
```

---

## 例

```text id="rkswlc"
- ExportGrid を呼ぶ
- SaveSession を呼ぶ
- MovePlacement を呼ぶ
```

---

## 重要

`invokes` は通常問題ありません。

Capability BOM Audit では、

```text id="l8x0nt"
UseCase を呼ぶこと
```

ではなく、

```text id="g81t5i"
何を決めているか
```

を問題にします。

---

## 例

```yaml id="ibeb5j"
roles:
  - invokes
```

---

# coordinates

## 定義

```text id="qb7b33"
複数UseCaseやCapability間の呼び出し順を調停する
```

---

## 例

```text id="kpg3ef"
- Save前にValidationを呼ぶ
- Placement変更後にHistoryへ積む
- Export前にRenderを更新する
```

---

## 重要

`coordinates` は悪ではありません。

Capability BOM Audit では、

```text id="rqarfv"
workflow coordination
```

は多くの場合自然です。

特に：

```text id="gj0gm3"
- ViewModel
- ApplicationService
- Coordinator
```

では普通に発生します。

---

## 注意点

ただし、

```text id="x6n6yv"
workflow coordination
```

が、

```text id="crp3ru"
domain decision
```

へ変化していないかを観測します。

---

## 許容される例

```text id="k2ctpw"
Placement編集後に UndoStack へ積む
```

---

## 疑わしい例

```text id="z8xyvx"
Variant fork の必要性を ViewModel が決める
```

これは coordination ではなく、

```text id="6o49a1"
domain_decision
```

の可能性があります。

---

# enforces

## 定義

```text id="2d4q14"
Rule、validation、invariant を保証する
```

---

## 例

```text id="g0qm3o"
- placement overlap を禁止
- offset 範囲を制限
- trim mode 制約を保証
```

---

## 重要

`enforces` は強い意味を持ちます。

Capability BOM Audit では、

```text id="jlwmrg"
どこがRuleを保証しているか
```

を非常に重視します。

---

## 注意点

`enforces` が ViewModel にある場合、監査対象になることがあります。

---

## 例

### 良い可能性

```text id="36t8ry"
DomainValidator が Rule を保証
```

---

### 疑わしい可能性

```text id="k9x24m"
ViewModel が placement validity を直接判定
```

---

# owns

## 定義

```text id="2rzd6u"
Capability の意味判断や方針を所有する
```

---

## 最重要 Role

`owns` は Capability BOM Audit の中で最も重要な Role の1つです。

---

## 例

```text id="vq9r7m"
- variant sharing policy
- placement lifecycle
- export boundary semantics
- undo meaning unit
```

---

## なぜ重要か

Capability BOM Audit の中心問題は、

```text id="e2qf7j"
どこが意思決定を所有しているか
```

だからです。

---

## 注意点

`owns` が UI 層にある場合、Overreach の可能性があります。

---

## 例

### 疑わしい例

```text id="4n64gd"
ViewModel が
「この編集では fork が必要」
を決める
```

---

### より自然な例

```text id="e5ov9w"
PlacementEditingCapability が fork policy を持つ
```

---

# persists

## 定義

```text id="p6u4k0"
保存・復元・永続化境界を扱う
```

---

## 例

```text id="9wv8rl"
- Session save boundary
- serialization unit
- migration handling
- persistence lifecycle
```

---

## 注意点

`persists` は単なる I/O ではありません。

重要なのは、

```text id="ht0qnj"
何を保存するか
```

を決めている点です。

---

## 疑わしい例

```text id="28z9r9"
ViewModel が
保存対象の意味単位を決める
```

---

# renders

## 定義

```text id="jlwmko"
表示・画像・出力形式へ変換する
```

---

## 例

```text id="3d0y9o"
- PNG生成
- preview render
- canvas geometry mapping
- trim mode application
```

---

## 注意点

`renders` は、

```text id="nq7s1d"
単なる表示
```

ではなく、

```text id="qvq2bx"
出力意味
```

を持つことがあります。

---

## 疑わしい例

```text id="rxlm8v"
ViewModel が render geometry を直接決める
```

---

# Allowed / Acceptable / Suspicious

Capability BOM Audit では、Role を次の3段階で扱います。

---

# Allowed

通常自然な関与。

## 例

```text id="10sgyl"
- observes
- projects
- invokes
```

---

## 意味

これらは通常、

```text id="ih0z8r"
意思決定 ownership
```

を持たないからです。

---

# Acceptable With Note

許容可能だが、複雑化に注意。

---

## 代表

```text id="qfyc8h"
- coordinates
```

---

## なぜ注意か

coordination は自然ですが、

```text id="yc54gl"
workflow decision
```

が増えすぎると、

```text id="d6n8tz"
domain decision
```

へ変質することがあります。

---

## 例

### 許容される

```text id="e5r52u"
Export前に SaveDirtyState を呼ぶ
```

---

### 危険化し始める

```text id="wjlwmz"
Variant fork 条件を UIフロー内で決める
```

---

# Suspicious

Capability 境界を越えている可能性。

---

## 代表

```text id="rry1pw"
- owns
- enforces
- persists
- renders
```

---

## 理由

これらは通常、

```text id="0vj8vh"
意味判断
```

や、

```text id="jlwmdb"
Rule ownership
```

を含むからです。

---

# Role と Decision の関係

Role と Decision は別物です。

---

## Role

```text id="96y5pn"
どう関与しているか
```

---

## Decision

```text id="jlwm0r"
何を決めているか
```

---

## 例

```yaml id="rmwbsh"
roles:
  - coordinates

decisions:
  - workflow_decision
```

これは自然な可能性があります。

---

## 例

```yaml id="t7mtrp"
roles:
  - owns

decisions:
  - domain_decision
```

これは強い意味判断です。

---

# 大きいクラス ≠ 危険なクラス

Capability BOM Audit は、

```text id="jlwmvt"
行数
```

や、

```text id="0s8bxa"
メソッド数
```

だけでは判断しません。

---

## 重要なのは

```text id="jlwmfm"
どのRoleを持っているか
```

と、

```text id="jlwmqw"
どのDecisionを所有しているか
```

です。

---

## 例

### 大きいが自然

```yaml id="44qtms"
roles:
  - observes
  - projects
  - invokes
  - coordinates
```

---

### 小さいが危険

```yaml id="jlwmnm"
roles:
  - owns
  - enforces

decisions:
  - domain_decision
```

---

# ViewModel に期待される典型 Role

Capability BOM Audit v0.1 では、ViewModel に対して通常次を期待します。

---

## 通常許容

```yaml id="jlwmnh"
roles:
  - observes
  - projects
  - invokes
```

---

## 条件付き許容

```yaml id="jlwmng"
roles:
  - coordinates
```

---

## 疑わしい可能性

```yaml id="jlwmnf"
roles:
  - owns
  - enforces
  - persists
  - renders
```

---

# Capability BOM Audit の核心

Role taxonomy の目的は、

```text id="jlwmne"
レイヤ違反検出
```

ではありません。

本当の目的は、

```text id="0mjlwm"
意思決定の所在を観測すること
```

です。

---

# 次に読むべき文書

次は以下を読むと理解が深まります。

```text id="jlwmnd"
04-decision-taxonomy.md
05-rule-ledger.md
06-runtime-mapping.md
07-overreach-detection.md
```

