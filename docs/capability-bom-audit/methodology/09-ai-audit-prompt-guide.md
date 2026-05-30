# 09 - AI Audit Prompt Guide

## この文書の目的

この文書では、Capability BOM Audit を AI に実行させる際のプロンプト設計指針を説明します。

Capability BOM Audit において、AIは：

```text id="aip001"
コード生成器
```

ではなく、

```text id="aip002"
意味構造監査者
```

として使います。

これは非常に重要です。

---

# なぜ Prompt Guide が必要なのか

AIは、何も指示しないと：

```text id="aip003"
- 綺麗そうなリファクタ
- クラス分割
- デザインパターン適用
- レイヤ整理
```

へ向かいやすい。

しかし Capability BOM Audit の目的は：

```text id="aip004"
意味構造を観測すること
```

です。

そのため：

```text id="aip005"
AIの役割を強制的に制限する
```

必要があります。

---

# Capability BOM Audit における AI の役割

Capability BOM Audit では、AIに期待するのは：

```text id="aip006"
- Capability inventory
- Role classification
- Decision observation
- Rule observation
- Runtime mapping
- Overreach detection
```

です。

---

# AIに期待しないこと

少なくとも監査フェーズでは、次を期待しません。

```text id="aip007"
- コード修正
- リファクタ
- クラス分割
- デザインパターン適用
- 新アーキテクチャ提案
```

---

# Capability BOM Audit Prompt の核心

Capability BOM Audit のAIプロンプトの核心は：

```text id="aip008"
修正より観測を優先させる
```

ことです。

---

# 悪いプロンプト

## 例

```text id="aip009"
この巨大ViewModelを綺麗にしてください
```

---

# なぜ危険か

AIは：

```text id="aip010"
- 小さいクラス
- 見た目の分離
- パターン適用
```

へ向かいやすい。

しかし：

```text id="aip011"
意味 ownership
```

は観測されません。

---

# 良いプロンプト

## 例

```text id="aip012"
GridWorkspaceViewModel が、
どのCapabilityに対して、
どのRoleとDecisionを持っているか分類してください。

コード修正は禁止です。
```

---

# なぜ良いのか

AIを：

```text id="aip013"
生成器
```

ではなく、

```text id="aip014"
測量器
```

として使っているからです。

---

# Capability BOM Audit Prompt の基本構造

Capability BOM Audit v0.1 では、通常次の構造を推奨します。

```text id="aip015"
1. Goal
2. Scope
3. Non-goals
4. Capability context
5. Allowed interpretations
6. Forbidden actions
7. Output format
8. Confidence policy
```

> **Step 5 昇格 (2026-05-30): 第三カテゴリ `MUST_DECIDE_AND_DOCUMENT` で 8 → 9 構造へ**
> ALLOWED / FORBIDDEN の二分では捉えきれない「AI が実装上どうしても決めざるを得ないが、決定内容を記録すべき」決定がある。
> これを **Allowed と Forbidden の間に第三カテゴリ** として挿入し、基本構造は 9 項目になる:
> `… 5. Allowed → (新) Must-decide-and-document = 決めてよいが記録必須 → Forbidden → Output → Confidence`。
> これは生成方向 (BOM → コード) で AI を実装者として安全に使うための防御。
> 詳細・運用 (実装ノートでの分類義務、典型決定カタログ) は `12-must-decide-and-document.md` を参照。

---

# 1. Goal

## 定義

```text id="aip016"
何を観測するのか
```

を明示する。

---

## 例

```text id="aip017"
Goal:
  GridWorkspaceViewModel が関与している
  Capability / Role / Decision を観測する
```

---

# 2. Scope

## 定義

```text id="aip018"
観測対象範囲
```

を限定する。

---

## 例

```text id="aip019"
Scope:
  - GridWorkspaceViewModel.cs
  - related commands
  - directly invoked use cases
```

---

# なぜ必要か

AIは範囲を与えないと：

```text id="aip020"
全体最適
```

へ暴走しやすい。

---

# 3. Non-goals

## 定義

```text id="aip021"
絶対にやらせないこと
```

を書く。

---

# 非常に重要

Capability BOM Audit では：

```text id="aip022"
Non-goals
```

が特に重要です。

---

# 例

```text id="aip023"
Non-goals:
  - コード修正しない
  - クラス分割しない
  - 新アーキテクチャを提案しない
  - デザインパターン化しない
```

---

# なぜ必要か

AIは：

```text id="aip024"
「改善」
```

という言葉を見ると、

```text id="aip025"
実装修正
```

へ向かいやすい。

---

# 4. Capability Context

## 定義

```text id="aip026"
監査対象Capability
```

を事前に与える。

---

# 例

```text id="aip027"
Capabilities:
  - GRID_COMPOSITION
  - PLACEMENT_EDITING
  - RENDERING_EXPORT
  - HISTORY_MANAGEMENT
```

---

# なぜ重要か

Capability context がないと、AIは：

```text id="aip028"
クラス構造
```

だけで判断しやすい。

---

# 5. Allowed Interpretations

## 定義

```text id="aip029"
許容されるRoleやDecision
```

を先に定義する。

---

# 例

```text id="aip030"
Allowed in ViewModel:
  - observes
  - projects
  - invokes
  - ui_interaction_decision
```

---

# 効果

AIが：

```text id="aip031"
全部悪
```

と誤判定しにくくなる。

---

# 6. Forbidden Actions

## 定義

```text id="aip032"
監査中に禁止する行為
```

を定義する。

---

# 例

```text id="aip033"
Forbidden:
  - refactor proposal
  - class split
  - DI redesign
  - microservice suggestion
```

---

# 重要

Capability BOM Audit は：

```text id="aip034"
観測
```

を先に行います。

---

# 7. Output Format

## 定義

```text id="aip035"
AI出力形式
```

を固定する。

---

# なぜ重要か

フォーマットを指定しないと：

```text id="aip036"
長文レビュー
```

になりやすい。

---

# 推奨形式

```yaml id="aip037"
file:
  path:

  mapped_capabilities:

  roles:

  decisions:

  suspected_overreach:

  evidence:
```

---

# 8. Confidence Policy

## 定義

```text id="aip038"
不確実性の扱い
```

を指定する。

---

# 非常に重要

Capability BOM Audit では：

```text id="aip039"
unknown を許容する
```

ことが重要です。

---

# 悪い例

```text id="aip040"
必ず断定してください
```

---

# 良い例

```text id="aip041"
不明な場合は unclear を使うこと
```

---

# Capability BOM Audit における AI の禁止事項

監査フェーズでは、AIに次を禁止することを推奨します。

---

# 禁止事項

```text id="aip042"
- 「SOLID違反」と即断
- 「責務過多」と即断
- 「MVVM違反」と即断
- デザインパターン強制
- 過剰抽象化
- microservice 化提案
```

---

# なぜか

Capability BOM Audit は：

```text id="aip043"
設計流派監査
```

ではなく、

```text id="aip044"
意味構造監査
```

だからです。

---

# 良い Capability BOM Audit Prompt の特徴

良いプロンプトは：

```text id="aip045"
- 観測中心
- 非断定的
- Capability中心
- Decision中心
- evidence要求
- unknown許容
```

を持ちます。

---

# 悪い Capability BOM Audit Prompt の特徴

悪いプロンプトは：

```text id="aip046"
- 修正中心
- 完璧分離要求
- 断定強制
- 全体最適化
- 見た目改善
```

へ向かいます。

---

# 典型プロンプト例

## 良い例

```text id="aip047"
GridWorkspaceViewModel を監査してください。

目的:
  Capability / Role / Decision を観測すること。

禁止:
  コード修正
  クラス分割
  リファクタ提案

期待:
  Runtime mapping
  suspected overreach
  evidence

不明な場合:
  unclear を使用すること。
```

---

# 悪い例

```text id="aip048"
このViewModelを綺麗なMVVMにしてください
```

---

# なぜ危険か

AIは：

```text id="aip049"
「綺麗」
```

を：

```text id="aip050"
小さいクラス
```

や：

```text id="aip051"
薄いViewModel
```

として解釈しやすい。

---

# AIに「監査モード」を強制する

Capability BOM Audit では、AIへ：

```text id="aip052"
あなたは設計監査者であり、
実装者ではない
```

と明示するのが有効です。

---

# 例

```text id="aip053"
You are performing a Capability BOM Audit.

You are NOT refactoring the code.
You are observing:
  - capability involvement
  - decision ownership
  - rule enforcement
  - suspected overreach
```

---

# Evidence-first Principle

Capability BOM Audit では：

```text id="aip054"
印象
```

ではなく、

```text id="aip055"
evidence
```

を要求します。

---

# 悪い例

```text id="aip056"
このVMは責務が多すぎます
```

---

# 良い例

```text id="aip057"
MoveSelectedPlacement に
placement validity branch が存在する可能性
```

---

# Unknown-first Principle

Capability BOM Audit では：

```text id="aip058"
unknown を残す
```

ことを推奨します。

---

# なぜか

AIは：

```text id="aip059"
断定
```

を求められると：

```text id="aip060"
過剰推論
```

しやすい。

---

# 推奨

```text id="aip061"
- unclear
- suspected
- partially_verified
```

を積極利用する。

---

# Prompt Anti-patterns

Capability BOM Audit で避けるべきプロンプト。

---

# Anti-pattern 1

```text id="aip062"
綺麗にしてください
```

---

# 理由

意味が曖昧。

---

# Anti-pattern 2

```text id="aip063"
SOLIDにしてください
```

---

# 理由

設計流派へ誘導される。

---

# Anti-pattern 3

```text id="aip064"
責務を分離してください
```

---

# 理由

Decision ownership が観測されない。

---

# Anti-pattern 4

```text id="aip065"
理想的アーキテクチャへ直してください
```

---

# 理由

AIが：

```text id="aip066"
全体最適妄想
```

へ向かいやすい。

---

# Capability BOM Audit の核心

Capability BOM Audit における AI Prompt の目的は、

```text id="aip067"
AIに設計させること
```

ではありません。

本当に重要なのは：

```text id="aip068"
AIに意味構造を観測させること
```

です。

---

# 次に読むべき文書

次は以下を読むと理解が深まります。

```text id="aip069"
10-common-misunderstandings.md
```

