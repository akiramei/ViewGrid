# 10 - Common Misunderstandings

## この文書の目的

この文書では、Capability BOM Audit に関して発生しやすい誤解を整理します。

Capability BOM Audit は新しい概念を含むため、既存のソフトウェア開発経験者ほど、既存の設計概念へ引き寄せて解釈しやすい傾向があります。

特に：

```text id="mis001"
- DDD
- Clean Architecture
- MVVM
- SOLID
- マイクロサービス
- リファクタリング
```

などと混同されやすい。

この文書では、それらとの違いを明確化します。

---

# 誤解1

## 「これは新しいアーキテクチャですか？」

### 短い答え

```text id="mis002"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis003"
アーキテクチャ
```

ではありません。

Capability BOM Audit は、

> 既存アーキテクチャを意味構造として観測する監査方法論

です。

---

# つまり

Capability BOM Audit は：

```text id="mis004"
- MVVM
- DDD
- Clean Architecture
- Vertical Slice
- Hexagonal
```

などの上に適用できます。

---

# Capability BOM Audit が見るもの

```text id="mis005"
- Capability
- Rule
- Decision
- Role
- Runtime Mapping
- Overreach
```

---

# Capability BOM Audit が決めないもの

```text id="mis006"
- フォルダ構造
- DI構造
- Layer数
- Microservice分割
```

---

# 誤解2

## 「巨大ViewModelは禁止ですか？」

### 短い答え

```text id="mis007"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis008"
大きい = 悪
```

とは考えません。

重要なのは：

```text id="mis009"
何を決めているか
```

です。

---

# 例

## 大きいが自然

```yaml id="mis010"
roles:
  - observes
  - projects
  - invokes
  - coordinates
```

---

# 小さいが危険

```yaml id="mis011"
roles:
  - owns

decisions:
  - domain_decision
```

---

# 核心

Capability BOM Audit は：

```text id="mis012"
行数
```

ではなく、

```text id="mis013"
Decision ownership
```

を観測します。

---

# 誤解3

## 「複数Capabilityに触ると悪ですか？」

### 短い答え

```text id="mis014"
違います。
```

---

# 説明

Capability BOM Audit の中心原則：

```text id="mis015"
関与していることは問題ではない。
意思決定を所有していることが問題になり得る。
```

---

# 例

## 自然

```yaml id="mis016"
roles:
  - observes
  - projects
  - invokes
```

---

# 疑わしい可能性

```yaml id="mis017"
roles:
  - owns
  - enforces

decisions:
  - domain_decision
  - validation_decision
```

---

# つまり

```text id="mis018"
Capability overlap
```

ではなく、

```text id="mis019"
Decision overlap
```

を問題にします。

---

# 誤解4

## 「責務分割と何が違うのですか？」

### 短い答え

```text id="mis020"
Decision ownership を観測する点が違います。
```

---

# 従来

```text id="mis021"
責務:
  何を行うか
```

---

# Capability BOM Audit

```text id="mis022"
Decision:
  何を決めるか
```

---

# 例

## 従来なら問題視されにくい

```text id="mis023"
ViewModel が Validation を持つ
```

---

# Capability BOM Audit では

```text id="mis024"
そのValidationが、
domain meaning を含むか
```

を見る。

---

# 誤解5

## 「これはDDDですか？」

### 短い答え

```text id="mis025"
DDDとは違います。
```

---

# 説明

DDD は：

```text id="mis026"
ドメインモデル中心
```

です。

Capability BOM Audit は：

```text id="mis027"
意味構造監査中心
```

です。

---

# DDDとの関係

Capability BOM Audit は：

```text id="mis028"
DDD実装
```

にも適用できます。

---

# Capability BOM Audit が見るもの

```text id="mis029"
- Rule leakage
- Decision concentration
- Runtime mismatch
```

---

# DDD が主に扱うもの

```text id="mis030"
- Entity
- Aggregate
- Ubiquitous Language
```

---

# 誤解6

## 「これはSOLID違反検出ですか？」

### 短い答え

```text id="mis031"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis032"
設計流派監査
```

ではありません。

---

# Capability BOM Audit が見るもの

```text id="mis033"
- 意味 ownership
- Rule guarantee
- Decision leakage
```

---

# SOLID が見るもの

```text id="mis034"
- dependency
- abstraction
- substitution
```

---

# 誤解7

## 「最終的にはMicroservice化するのですか？」

### 短い答え

```text id="mis035"
必須ではありません。
```

---

# 説明

Capability は：

```text id="mis036"
意味境界
```

です。

---

# 重要

```text id="mis037"
Capability ≠ Runtime分割
```

---

# Capability は

```text id="mis038"
「何をできるか」
```

を表します。

---

# Runtime 分割は別問題

```text id="mis039"
- deploy
- scaling
- team topology
- infra
```

などで決まる。

---

# 誤解8

## 「最終的には薄いViewModelを目指すのですか？」

### 短い答え

```text id="mis040"
必ずしもそうではありません。
```

---

# 説明

Capability BOM Audit は：

```text id="mis041"
UI coordination
```

を否定しません。

---

# 許容されやすいもの

```yaml id="mis042"
roles:
  - projects
  - invokes
  - coordinates
```

---

# 疑わしい可能性

```yaml id="mis043"
roles:
  - owns
  - enforces
```

---

# つまり

問題は：

```text id="mis044"
薄さ
```

ではなく、

```text id="mis045"
Decision ownership
```

です。

---

# 誤解9

## 「Overreach は即修正ですか？」

### 短い答え

```text id="mis046"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis047"
観測
```

を優先します。

---

# なぜか

現実には：

```text id="mis048"
- migration code
- temporary adaptation
- compatibility logic
- experimental implementation
```

などがあるからです。

---

# 重要

Capability BOM Audit は：

```text id="mis049"
設計警察
```

ではありません。

---

# 誤解10

## 「AIに理想構造へ直させる方法論ですか？」

### 短い答え

```text id="mis050"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis051"
AIリファクタ方法論
```

ではありません。

---

# 本当の目的

```text id="mis052"
AI時代でも、
人間が意味構造を見失わないようにする
```

ことです。

---

# Capability BOM Audit における AI

AIは：

```text id="mis053"
実装者
```

ではなく、

```text id="mis054"
意味構造監査者
```

として使います。

---

# 誤解11

## 「unknown や unclear は悪ですか？」

### 短い答え

```text id="mis055"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis056"
unknown を可視化する
```

ことを重視します。

---

# なぜか

AIは：

```text id="mis057"
断定
```

を強制されると、

```text id="mis058"
過剰推論
```

しやすい。

---

# Capability BOM Audit の推奨

```text id="mis059"
- unclear
- suspected
- partially_verified
```

を積極利用する。

---

# 誤解12

## 「これは静的解析ですか？」

### 短い答え

```text id="mis060"
部分的には似ていますが違います。
```

---

# 静的解析

通常：

```text id="mis061"
- dependency
- complexity
- nullability
- style
```

を見る。

---

# Capability BOM Audit

Capability BOM Audit は：

```text id="mis062"
- meaning boundary
- decision ownership
- rule guarantee
- runtime mismatch
```

を見る。

---

# 誤解13

## 「これはリファクタリング手法ですか？」

### 短い答え

```text id="mis063"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis064"
リファクタ前の測量
```

です。

---

# 流れ

```text id="mis065"
Capability inventory
  ↓
Runtime mapping
  ↓
Rule ledger
  ↓
Overreach observation
  ↓
必要なら refactor proposal
```

---

# つまり

```text id="mis066"
refactor は最後
```

です。

---

# 誤解14

## 「全部を綺麗に分離するべきですか？」

### 短い答え

```text id="mis067"
違います。
```

---

# 説明

Capability BOM Audit は：

```text id="mis068"
潔癖分離
```

を目的としません。

---

# 許容されるもの

```text id="mis069"
- workflow coordination
- UI adaptation
- temporary coupling
```

---

# 問題なのは

```text id="mis070"
意味 ownership leakage
```

です。

---

# 誤解15

## 「これはコード中心設計を否定していますか？」

### 短い答え

```text id="mis071"
否定していません。
```

---

# 説明

Capability BOM Audit は：

```text id="mis072"
コード
```

を否定しません。

ただし：

```text id="mis073"
コードだけ
```

では、

```text id="mis074"
意味構造
```

を追跡できないと考えます。

---

# つまり

従来：

```text id="mis075"
コード中心
```

---

# Capability BOM Audit

```text id="mis076"
意味構造中心
```

---

# Capability BOM Audit の核心

Capability BOM Audit の目的は、

```text id="mis077"
理想アーキテクチャを強制すること
```

ではありません。

本当に重要なのは：

```text id="mis078"
AI時代でも、
意味境界と意思決定の所在を
追跡可能にすること
```

です。

---

# 最後に

Capability BOM Audit は、

```text id="mis079"
設計流派
```

ではなく、

```text id="mis080"
意味構造の測量方法論
```

です。

そしてその核心は：

```text id="mis081"
「どこにコードがあるか」
ではなく、
「どこに意思決定があるか」
```

を観測することにあります。

