# 12 — `MUST_DECIDE_AND_DOCUMENT` 第三カテゴリ

> **Status: canonical (本体拡張、Step 5 で昇格 2026-05-30)**
> 既存 09-ai-audit-prompt-guide.md を拡張する位置づけ

## この文書の目的

既存 09-ai-audit-prompt-guide.md は AI へのプロンプト 8 構造を提示する。
その中の `ALLOWED` (自由) / `FORBIDDEN` (禁止) の二項対立は **AI が決めざるを得ない多くの実装決定** を捉えきれない。

本文書では、`MUST_DECIDE_AND_DOCUMENT` という **第三カテゴリ** を導入する。
これは Phase 2 試行 3 回 (累計 **25 件** の決定事例) で実証された運用パターン。

---

## 1. 動機 — 二項対立の限界

### 1.1 二項対立 (ALLOWED / FORBIDDEN) の限界

09-ai-audit-prompt-guide.md は次の対立で AI を制約する:

| カテゴリ | 意味 |
| --- | --- |
| ALLOWED | AI が自由に決めてよい (報告も不要) |
| FORBIDDEN | AI が変更してはならない |

ところが Phase 2 試行で観測された事実: **どちらにも該当しない多くの決定事項** がある。

### 1.2 実例 — Phase 2 v0.1 GRID_COMPOSITION 試行で観測された実装決定

サンプル文書から一意に決まらないが、AI が実装上は決定せざるを得ない事項。9 件:

| 決定事項 | ALLOWED? | FORBIDDEN? | 実際の性質 |
| --- | --- | --- | --- |
| timestamp の UTC か local か | 言語仕様の話だから ALLOWED? | 違う | 意味的決定 |
| Repository の "not found" は None か例外か | 実装スタイルだから ALLOWED? | 違う | API 契約の話 |
| トランザクション境界の実装機構 | ALLOWED? | 違う | 意味的決定 |
| イベントバスの同期性 | ALLOWED? | 違う | 観測可能性の話 |
| 失敗理由の Exception 階層設計 | ALLOWED? | 違う | API 契約の話 |
| OccupySize 軸の解釈 (width = 列 / 行?) | 確認したい | 違う | 用語の話 |
| 言語選択の根拠 | 自由だが理由を残すべき | 違う | メタデータ |
| ... | | | |

これらをすべて ALLOWED に括ると **後でなぜそう決まったかが追跡不能**。
FORBIDDEN にすると **AI が動けない** (誰も決めてないため)。

### 1.3 結論

「**AI が決めてよいが、決定内容を明示する義務がある**」という第三カテゴリが必要。

---

## 2. パターンの定義

### 2.1 `MUST_DECIDE_AND_DOCUMENT` の定義

```text
MUST_DECIDE_AND_DOCUMENT (AI が決めてよいが実装ノートに明示する義務がある)
```

| 性質 | 内容 |
| --- | --- |
| 決定権 | AI 任意 (= ALLOWED と同じく自由に決めてよい) |
| 義務 | 決定内容を **`IMPLEMENTATION_NOTES.md`** などのノートに明示する |
| 件数の指針 | サンプルが想定する典型決定数を最小件数として要求 (例: ≥ 5 件) |
| 追跡可能性 | 後続の監査 / 改訂で「なぜこう決まったか」を遡れる |

### 2.2 ALLOWED / MUST_DECIDE_AND_DOCUMENT / FORBIDDEN の比較

| カテゴリ | 決定権 | 報告義務 | 例 |
| --- | --- | --- | --- |
| **ALLOWED** | AI | なし | ファイル配置、命名規約、DI コンテナ使用 |
| **MUST_DECIDE_AND_DOCUMENT** | AI | あり | timestamp tz、Repository の None vs 例外、Enum vs 文字列 |
| **FORBIDDEN** | 不可 | — | Rule ID 変更、Decision ownership 変更、用語の意味変更 |

### 2.3 三カテゴリの境界判定

ある決定事項がどのカテゴリに属するか:

```text
Q1. この決定はサンプル文書で一意に固定されているか?
    Yes → FORBIDDEN (変更不可)
    No  → Q2 へ

Q2. この決定は後続の監査 / 改訂で追跡可能であるべきか?
    No  → ALLOWED (報告不要)
    Yes → MUST_DECIDE_AND_DOCUMENT
```

Q2 の判定基準:

- **API 契約に影響するか** (例: Repository 戻り値型)
- **テストや他 Capability の実装に影響するか** (例: event の同期性)
- **後続の改訂で「あれはなぜそうした?」と疑問になりやすいか**

これらに該当すれば MUST_DECIDE_AND_DOCUMENT。

---

## 3. Phase 2 試行で観測された累積件数

3 回の Phase 2 試行で AI が分類した MUST_DECIDE_AND_DOCUMENT 項目の累計:

| 試行 | 件数 | 代表例 |
| --- | --- | --- |
| Phase 2 v0.1 GRID | 9 (自主分類) | timestamp tz / Repository None / トランザクション境界 / イベント同期性 / 失敗理由型 / OccupySize 軸 / 言語選択 / dense order / cross-grid swap |
| Phase 2 v0.2 GRID | 7 | (重複多数だが新規: silent locked-axis shrink、cross-grid swap = Conflict) |
| Phase 2 IMAGE_VARIANT v0.1 | 9 | timestamp tz / Repository None / image decoder 選択 / hash impl / blob storage / AutoCrop 集約表現 / Enum 表現 / 共有値オブジェクト配置 / EventBus 機構 |

**累積: 25 件** (重複除外で **~18 件のユニーク典型決定**)。

これだけの決定が ALLOWED に流れていれば、後続の監査者は「なぜそう決まった?」を毎回 1 から推測することになる。

---

## 4. 運用規範

### 4.1 サンプル文書の `40-ai-implementation-prompt.md` での記述

AI へのプロンプトに次のセクションを設ける:

```text
== MUST_DECIDE_AND_DOCUMENT (AI が決めてよいが実装ノートに明示する義務がある) ==
v0.2 で導入された第三カテゴリ。サンプル文書から一意に決まらないが、
実装上は決定せざるを得ない事項。決定内容を IMPLEMENTATION_NOTES.md に列挙すること。

代表例:
- Timestamp の時間帯 (UTC か local か)
- Repository の "not found" 表現 (None / Optional / 例外)
- トランザクション境界の実装機構
- イベントバスの同期性
- 失敗理由の Exception 階層設計
- Enum vs 文字列定数
- 言語選択の根拠

これらは ALLOWED と異なり「書類に書く義務」がある。なぜなら:
- 後続の監査 / 改訂で「なぜこう決まったか」を追跡可能にする
- 同じサンプル文書で異なる AI が異なる決定をした場合の横断比較を可能にする

最低 5 件を実装ノートに記載すること。
```

### 4.2 実装ノートでの記載形式

`IMPLEMENTATION_NOTES.md` に次のテーブル形式で記載:

```markdown
## MUST_DECIDE_AND_DOCUMENT items

| # | Topic | Choice | Rationale |
|---|-------|--------|-----------|
| MD-1 | Timestamp timezone | UTC, tz-aware | 観測可能性。local tz は test reproducibility を損なう |
| MD-2 | Repository "not found" | Optional / None | 30-design.md §5.1 が GetById(...) -> X | None を示唆 |
| ... |
```

### 4.3 監査時の取り扱い

Capability BOM Audit (本来の監査方向) で MUST_DECIDE_AND_DOCUMENT 項目を観察する:

- **項目自体は批判対象ではない** (= 規定外決定の正常な実施)
- **しかし数や種別から「設計の歪み」を読み取る**:
  - 件数が異常に多い → サンプル文書が underdetermined すぎる
  - 同じ典型決定が複数 Capability で重複 → Shared Concepts 規範を検討すべき
  - API 契約に影響する決定が多い → ALLOWED に分類すべきだった可能性

### 4.4 反復改訂への活用

MUST_DECIDE_AND_DOCUMENT の蓄積は、サンプルの **次バージョンへのインプット** として機能する:

- 同じ典型決定が 2 回以上発生 → サンプル v0.X+1 で明示的に決める価値あり (FORBIDDEN へ移動)
- 件数が増え続ける → サンプルの underdetermined を示唆 (再構造化が必要)

これは 13-norm-inheritance-and-inverse-audit.md で詳述する **反復検証ループ** の重要な入力。

---

## 5. アンチパターン

### 5.1 MUST_DECIDE_AND_DOCUMENT を ALLOWED の "親切な版" として扱う

「報告するけど、まあ自由でいいよ」では効果が薄い。**最低件数を要求** することで AI に網羅を強いる必要がある。

### 5.2 MUST_DECIDE_AND_DOCUMENT を FORBIDDEN の "曖昧な版" として扱う

「決められないから AI に任せる」のではなく、「決めて記録してほしい」という能動的要求として扱う。

### 5.3 件数だけ満たして内容が空疎

`MD-1 Language: Python` (Rationale: 慣れているから) のような表面的な記載は無意味。
「サンプル文書のどこを参照してそう決めたか」が追跡可能であることが重要。

### 5.4 サンプル改訂時に MUST_DECIDE_AND_DOCUMENT が増え続ける

増加が続くならサンプルの構造を見直すサイン (本文書 §4.4 参照)。

---

## 6. 既存方法論本体への接続

| 既存文書 | 本パターンとの接続 |
| --- | --- |
| 09-ai-audit-prompt-guide.md | 8 構造のうち `ALLOWED` / `FORBIDDEN` の中間に第三カテゴリを挿入。プロンプト構造は **9 構造に拡張** |
| 02-core-concepts.md | Decision ownership 表で AI に委ねる Decision を捉えきれない部分を補完 |
| 07-overreach-detection.md | MUST_DECIDE_AND_DOCUMENT 件数の異常を Overreach (の前兆) として観測する |

### 6.1 既存 09 の 8 構造への提案

```text
旧 (v0.1):
1. Goal
2. Scope
3. Non-goals
4. Capability context
5. Allowed interpretations
6. Forbidden actions
7. Output format
8. Confidence policy

新 (v0.2 提案):
1. Goal
2. Scope
3. Non-goals
4. Capability context
5. Allowed interpretations
5'. MUST_DECIDE_AND_DOCUMENT (← 新規挿入)
6. Forbidden actions
7. Output format
8. Confidence policy
9. Post-implementation self-audit (← 既存だが本文書で再強調)
```

---

## 7. 採用判定

| 評価軸 | 結果 |
| --- | --- |
| 実証根拠 | Phase 2 試行 3 回で累計 25 件の決定事例を観測 |
| 適用コスト | 低 (プロンプトに 1 セクション追加するだけ) |
| 既存方法論との整合 | 高 (09 を拡張するだけ、矛盾なし) |
| 認知負荷 | 低 (執筆者は典型例 7 件を渡せばよい) |

---

## 8. 関連ドキュメント

- 11-three-layer-disambiguation.md — MUST_DECIDE_AND_DOCUMENT より「強い」防御 (=決められる事項は三層で固定する)
- 13-norm-inheritance-and-inverse-audit.md — MUST_DECIDE_AND_DOCUMENT の累積を反復改訂への入力として活用
- 14-author-checklist.md — MUST_DECIDE_AND_DOCUMENT の典型例カタログ
- 実証根拠: `docs/capability-bom-audit/evaluation/90-feasibility-notes.md` Addendum A §A.5, Addendum B §B.5, Addendum D §D.2
