# 01 - Why Capability BOM Audit

## この文書の目的

この文書は、Capability BOM Audit が何を解決しようとしているのか、なぜ必要なのか、そして従来のソフトウェア設計と何が違うのかを説明するためのオンボーディング資料です。

これは新しいアーキテクチャ流派の説明ではありません。

Capability BOM Audit は、

* Clean Architecture
* DDD
* MVVM
* Vertical Slice
* Hexagonal Architecture

などを置き換えるものではなく、

> ソフトウェアの意味構造と意思決定の所在を測量するための監査方法論

です。

---

# 背景

## AI時代の変化

従来のソフトウェア開発では、コードを書くコストが高かったため、人間はコードそのものを中心に設計・レビューしていました。

```text
設計
  ↓
実装
  ↓
コードレビュー
  ↓
テスト
```

この前提では、

* クラス構造
* レイヤ構造
* モジュール分割
* ディレクトリ構成

が設計の中心でした。

しかしAIによるコード生成が普及すると、この前提が崩れます。

---

## 何が崩れたのか

AI時代では、

```text
コード生成コスト
<<
コードレビューコスト
```

になります。

つまり、

* コードを書くこと
* ボイラープレートを作ること
* リファクタ候補を出すこと
* テスト雛形を作ること

は非常に安価になります。

一方で、

* このコードは何を決めているのか
* どこがルールを保証しているのか
* どの層が意味判断を持っているのか
* 変更理由が混ざっていないか

を確認するコストは下がりません。

ここが重要です。

---

# 従来の問題

従来のレビューでは、次のような観点が中心でした。

```text
- クラスが大きい
- レイヤ違反
- 循環参照
- 命名
- DRY違反
- 依存方向
```

もちろんこれらは重要です。

しかし、AI時代には次の問題が増えます。

```text
- 意味判断がUIに漏れている
- Rule保証場所が散乱する
- AIが局所最適で実装する
- ViewModelがドメイン判断を持つ
- Validationが複数箇所に複製される
- どこが「本当の意味」を持つか分からなくなる
```

つまり、

> コード構造だけでは問題を測れなくなる

のです。

---

# Capability BOM Audit の考え方

Capability BOM Audit は、コードを直接評価する前に、

```text
- Capability
- Rule
- Role
- Decision
- Runtime mapping
```

を整理します。

これは、

> 「どのコードが何を決めているか」

を可視化するためです。

---

# Capability とは何か

Capability は、クラスでもサービスでもありません。

Capability は、

> ソフトウェアが持つ意味的能力

です。

例えば ViewGrid では、

```text
- GRID_COMPOSITION
- PLACEMENT_EDITING
- IMAGE_VARIANT_MANAGEMENT
- RENDERING_EXPORT
- HISTORY_MANAGEMENT
```

などが Capability になります。

重要なのは、

```text
Capability ≠ Runtime分割
```

という点です。

Capability は意味境界であり、最初からマイクロサービス化する必要はありません。

---

# Role とは何か

Role は、

> そのコードが Capability にどう関与しているか

を表します。

例：

```text
- observes
- projects
- invokes
- coordinates
- enforces
- owns
```

Capability BOM Audit では、

```text
複数Capabilityに関与している
```

こと自体は問題ではありません。

問題なのは、

```text
複数Capabilityの意思決定を所有している
```

ことです。

---

# Decision とは何か

Decision は、

> そのコードが何を決めているか

を表します。

例えば：

```text
- domain_decision
- validation_decision
- workflow_decision
- persistence_decision
- rendering_decision
```

などです。

---

# なぜ Decision が重要なのか

Capability BOM Audit の核心はここです。

例えば、

```text
ViewModel が UseCase を呼ぶ
```

のは自然です。

しかし、

```text
ViewModel が
- 配置の妥当性
- variant fork の必要性
- 保存単位
- Undo/Redo意味
- rendering geometry
```

を決めている場合、

それは意味判断がUI層へ漏れている可能性があります。

Capability BOM Audit は、この「意思決定の所在」を測量します。

---

# Runtime Mapping とは何か

Runtime Mapping は、

> Capability とコードの対応表

ではありません。

本当の目的は、

> 「どのコードが、どの Capability に対して、どの Role と Decision を持っているか」

を観測することです。

つまり、

```text
GridWorkspaceViewModel.cs
  ↓
どのCapabilityに触るか
  ↓
どう関与するか
  ↓
何を決めているか
```

を記録します。

---

# Overreach Detection とは何か

Capability BOM Audit では、

> 「大きいクラス」

を直接問題視しません。

問題視するのは、

```text
- 意思決定の越境
- Rule保証場所の漏れ
- UI層への意味判断流出
- Capability境界の混線
```

です。

これを Overreach Detection と呼びます。

---

# 重要な原則

Capability BOM Audit の中心原則は次の通りです。

```text
関与していることは問題ではない。
意思決定を所有していることが問題になり得る。
```

これは従来の「責務分割」と少し異なります。

---

# Capability BOM Audit は設計警察ではない

この方法論は、

```text
- 完璧なレイヤ分離
- すべてのViewModelを薄くする
- 巨大クラス禁止
- マイクロサービス化
```

を強制するものではありません。

むしろ逆です。

Capability BOM Audit の目的は、

> AI時代でも、人間が意味構造を見失わないようにすること

です。

---

# なぜ「監査」なのか

Capability BOM Audit は、AIに「綺麗そうなリファクタ」をさせる方法論ではありません。

例えば次の依頼は危険です。

```text
この巨大ViewModelをリファクタしてください
```

AIは局所的に「綺麗そうな構造」を提案します。

一方、Capability BOM Audit はこう聞きます。

```text
このコードは、
- どのCapabilityに関与し、
- どのRoleを持ち、
- どのDecisionを所有しているか
```

つまり、

> AIを実装者ではなく測量者・監査者として使う

のです。

---

# Capability BOM Audit が扱うもの

Capability BOM Audit は、主に次を扱います。

| 概念              | 意味               |
| --------------- | ---------------- |
| Capability      | ソフトウェアの意味的能力     |
| UseCase         | 実行可能な操作単位        |
| Rule            | 保証されるべき意味制約      |
| Role            | Capabilityへの関与方法 |
| Decision        | コードが何を決めているか     |
| Runtime Mapping | 意味構造と実装の対応       |
| Overreach       | 意思決定の越境          |

---

# 何を最初に行うのか

Capability BOM Audit では、最初にコード修正をしません。

最初に行うのは、

```text
- Capability inventory
- Runtime mapping
- Rule ledger
- Overreach detection
```

です。

つまり、

> まず意味構造を測量する

ことから始めます。

---

# ViewGrid が良い実験対象である理由

ViewGrid は Capability BOM Audit の教材として非常に良い性質を持っています。

理由：

```text
- UIがある
- ViewModelがある
- Undo/Redoがある
- 配置ルールがある
- 出力処理がある
- セッション保存がある
- 「論理コピー」という意味概念がある
```

特に「論理コピー」は重要です。

ImageCopy は単なる画像データではなく、

> 意味的な派生物

だからです。

これは Capability 的な思考に非常に近い概念です。

---

# Capability BOM Audit のゴール

Capability BOM Audit の目的は、

```text
綺麗なコードを書くこと
```

ではありません。

本当の目的は、

```text
AI時代でも、
意味境界と意思決定の所在を
人間が追跡可能にすること
```

です。

Capability BOM Audit は、

> ソフトウェアの意味構造を測量するための方法論

として位置づけられます。

---

# 関連拡張 (生成方向 — Step 5 で昇格)

本体 01〜10 は **監査方向 (コード → BOM 観測)** を定義する。これに対し、**生成方向 (BOM → コード生成) で
AI を実装者として安全に使う** ための拡張が Step 5 で canonical に昇格した
(実証: `../evaluation/90-feasibility-notes.md` Addendum A〜J / `../evaluation/91-findings-ledger.md`):

| 拡張 | 内容 | 本体との関係 |
| --- | --- | --- |
| `11-three-layer-disambiguation.md` | narrative + algorithmic + executable の三層で曖昧さを塞ぐ | 05 / 07 を拡張 |
| `12-must-decide-and-document.md` | 第三カテゴリ (決めてよいが記録必須) | 09 の 8 → 9 構造 |
| `13-norm-inheritance-and-inverse-audit.md` | 規範継承性 + Inverse Audit Protocol (BOM → コードの試行→改訂ループ) | 新規 |
| `14-author-checklist.md` | 人間執筆者向けチェックリスト | 01〜10 を補完 |
| `21-codebase-convention-contract.md` | 複数 Capability 合成のための横断規約契約 | 新規 (上位レイヤ) |
| `22-bom-conformance-check.md` | BOM ↔ 実装の機械照合 (受け入れゲート) | 新規 (検証) |

> `23-authoring-and-operating-model.md` (人間資料 → 意味設計コンパイラ → AI 実装 の運用層) は **draft 据え置き**
> (活発な frontier のため Step 5 では昇格しない)。
