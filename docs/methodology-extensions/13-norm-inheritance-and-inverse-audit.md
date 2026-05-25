# 13 — 規範継承性と逆向き監査プロトコル

> **Status: 方法論本体への昇格候補ドラフト**
> 既存 01〜10 にない新規概念。逆向き監査 (BOM → コード生成) の正典化

## この文書の目的

既存 01〜10 は **「コード → BOM 観測」** という後付け監査を扱う。
本文書では、その逆方向の **「BOM → コード生成」** を **公式の運用プロトコル** として位置づける。

さらに、Phase 2 試行 3 回で実証された **規範継承性 (Norm Inheritance)** —
すなわち「方法論の規範が確立されれば、新規 Capability の初版から実用品質に到達できる」 —
を本文書で明文化する。

これは Capability BOM Audit を **実運用に乗せる** ための中核プロトコル。

---

## 1. 動機 — 既存方法論の片側性

### 1.1 既存方法論は監査方向のみ

01〜10 の中心は次の流れ:

```text
既存コード
   ↓ (AI が観測者として参加)
Capability BOM (= 観測台帳)
   ↓
Overreach / Decision ownership 違反の検出
   ↓
人間によるレビュー / 改善判断
```

これは **後付け監査**。既存のコードベースに対して有効。

### 1.2 しかし AI 時代の生成方向は逆

AI 時代のソフトウェア開発では:

```text
人間が要求仕様 + Capability BOM を設計
   ↓
AI がコードを生成
   ↓
人間 (or 別 AI) がレビュー / 監査
```

この **生成方向** で必要なのは:

- BOM が「コード生成に必要十分か」を検証する手段
- BOM が不足している場合、それを発見し改訂するループ
- 異なる Capability のサンプル間で **品質を継承** する仕組み

これらは既存 01〜10 で扱われていない。

### 1.3 本文書の位置づけ

本文書は次の 2 つの新規概念を導入する:

1. **Inverse Audit Protocol** (逆向き監査プロトコル) — BOM → コード生成の Phase 2 試行を正典化
2. **Norm Inheritance** (規範継承性) — 方法論の規範が次世代 Capability に継承される性質

---

## 2. Inverse Audit Protocol (逆向き監査プロトコル)

### 2.1 プロトコルの定義

```text
[Phase 1] サンプル執筆
    人間が要求仕様 + Capability BOM + 設計書 + AI 実装プロンプトを執筆
    
[Phase 2] AI 実装試行
    別 AI セッション (worktree 隔離) でサンプルだけを入力に実装
    既存実装は参照禁止
    Anchor tests / 1000-step random walk が必須
    実装ノートに unclear / suspected_overreach / MUST_DECIDE_AND_DOCUMENT を記録

[Phase 3] 観測 + サンプル改訂
    Phase 2 の実装ノートを観測
    顕在化した穴を分類:
      - 取りこぼし (執筆者の改訂漏れ)
      - 真の新規穴 (サンプルの underdetermined)
      - 些細な typo (改訂作業の品質問題)
    サンプル v0.X+1 を執筆

[Phase 4] 再試行 (Phase 2 で v0.X+1 を使う)
    穴が消えたかを確認
    新規発見の穴があれば v0.X+2 候補へ
```

このループは **複数回反復** することを前提とする。
PoC では `GRID v0.1 → v0.2 → v0.2 (verified)` の 1 サイクルを完遂し、有効性を確認した。

### 2.2 各 Phase の規範

#### Phase 1 (執筆) の規範

- 既存 01〜10 + 本ディレクトリの 11〜14 を遵守
- 三層構造 (11-three-layer-disambiguation.md) を該当箇所に適用
- MUST_DECIDE_AND_DOCUMENT (12) のリストを 40-prompt に明示
- Anchor Tests AT-XX を 30-design.md §8 に記載
- canonical_failure_reasons を 21-yaml に列挙

#### Phase 2 (AI 実装) の規範

- AI に対する隔離条件を明示 (既存実装参照禁止)
- AI に対する報告義務を明示:
  - unclear 列挙
  - suspected_overreach 列挙
  - MUST_DECIDE_AND_DOCUMENT 最低件数
  - POST_IMPLEMENTATION_SELF_AUDIT (六項目 / 七項目) の結果
- 出力先を worktree 隔離されたディレクトリに

#### Phase 3 (観測) の規範

- Phase 2 結果を `90-feasibility-notes.md` の Addendum として記録
- 観測項目: 三層構造の 3 つの主要指標 (`unclear`, `suspected_overreach`, 重大バグの有無)
- 新規発見の穴は v0.X+1 候補にリストアップ

#### Phase 4 (再試行) の規範

- Phase 2 と同じ条件で別 AI セッションを起動
- 「v0.X で発生した穴が v0.X+1 で消えたか」を主要観測対象とする
- 規範継承性 (§3) の検証も同時に行う

### 2.3 プロトコルの非対称性 — 重要な注意

監査方向と生成方向は **対称な問題ではない**:

| 方向 | 入力 | 出力 | 不足が顕在化する場所 |
| --- | --- | --- | --- |
| 監査 (既存 01〜10) | 既存コード | BOM (観測) | 観測者が `unclear` と書ける |
| 生成 (本プロトコル) | BOM (人間執筆) | コード (AI) | **AI が「合理的に補完」してしまう** |

生成方向では「執筆者が書ききれなかった事項を AI が勝手に決める」リスクが構造的に発生する。
これに対する防御が:

- 三層構造 (11) — 重要決定を冗長に表現
- MUST_DECIDE_AND_DOCUMENT (12) — 残った決定を追跡可能にする
- Anchor Tests — 実装の正しさを機械的に固定

の 3 段。これらが揃って初めて生成方向は実用可能となる。

---

## 3. Norm Inheritance (規範継承性)

### 3.1 規範継承性の定義

ある Capability の v0.X で確立した規範 (canonical_failure_reasons の構造、
Anchor Tests の必須化、MUST_DECIDE_AND_DOCUMENT の明示など) が、
**次の Capability の v0.1 段階で初版からそのまま機能する** 性質。

> 規範継承性が成立する = **方法論を反復試行で磨き込めば、次の Capability では最初から高品質に到達できる**

### 3.2 Phase 2 IMAGE_VARIANT v0.1 試行での実証

3 回の Phase 2 試行の主要指標比較:

| 指標 | GRID v0.1 | GRID v0.2 | IMAGE_VARIANT v0.1 (規範継承後) |
| --- | --- | --- | --- |
| unclear 件数 | 6 | 5 | **3** (過去最低) |
| suspected_overreach | 2 | 0 | **0** |
| Anchor tests 初回合格 | (概念なし) | 10/10 | **10/10** |
| Random walk 実バグ検出 | 1 (Swap) | 0 | **0** |

**IMAGE_VARIANT は v0.1 段階で GRID v0.2 と同等以上の品質に到達** した。

これは方法論的に非常に重要な発見:

> サンプル文書群と反復検証プロトコルが整っていれば、
> 新規 Capability は v0.1 段階から実用品質に到達できる。

### 3.3 規範継承の実装可能なメカニズム

IMAGE_VARIANT v0.1 が高品質に到達できた理由:

| 継承元 | 継承内容 | 効果 |
| --- | --- | --- |
| GRID v0.2 の `canonical_failure_reasons` セクション | テンプレ化された失敗理由カタログ | NotFound 系の曖昧さがゼロ |
| GRID v0.2 の Worked Examples / Anchor Tests 規範 | テスト関数の正典化 | AT-01〜AT-10 が初版で揃う |
| GRID v0.2 の MUST_DECIDE_AND_DOCUMENT | 実装決定の追跡カテゴリ | 9 件を構造化して記録 |
| GRID v0.2 の三層構造 | 重要決定の冗長表現 | R-08 tug を AI が即捕捉 |
| GRID v0.2 の post-implementation self-audit | 7 項目チェック | AI 自身が監査結果を出力 |

これらは **方法論の規範が確立されていれば誰でも繰り返せる** メカニズム。
個別の Capability の専門知識ではなく、**方法論レイヤの規範** が品質を決める。

### 3.4 規範継承の限界

規範継承性は **すべてを継承するわけではない**:

- Capability 固有の用語・概念 (例: ImageCopy の hash 重複) は継承しない
- Capability 境界の調整作業 (Addendum C で実測) は継承しない
- 個別の MUST_DECIDE_AND_DOCUMENT 項目は新規発生 (典型決定は重複するが、Capability 特異な決定もある)

つまり継承性は **方法論レイヤの規範のみ**。Capability 個別の知識は別個に必要。

---

## 4. プロトコルの実装

### 4.1 ディレクトリ構造の規範

```text
docs/capability-bom-sample/
├── README.md                          (全体ナビゲーション)
├── <capability-id>/                   (Capability ごとに subdirectory)
│   ├── README.md
│   ├── 10-requirements.md
│   ├── 20-capability-bom.md
│   ├── 21-<capability-id>.yaml
│   ├── 30-design.md
│   └── 40-ai-implementation-prompt.md
└── 90-feasibility-notes.md            (横断的な評価メモ、Addendum で各試行を記録)

experiments/
├── phase2-<capability-id>-v<X>-impl/  (Phase 2 試行ごとの実装)
```

### 4.2 ファイル命名規範

| ファイル | 命名 |
| --- | --- |
| 要求仕様 | `10-requirements.md` |
| BOM Markdown | `20-capability-bom.md` |
| BOM YAML | `21-<capability-id-lowercase>.yaml` |
| 設計書 | `30-design.md` |
| プロンプト | `40-ai-implementation-prompt.md` |
| 横断評価 | 親ディレクトリの `90-feasibility-notes.md` の Addendum |
| 実装 | `experiments/phase2-<capability-id>-v<X>-impl/` |

### 4.3 バージョン管理

サンプルのバージョンは **意味的バージョニング**:

- **v0.X.0**: 中規模の改訂 (新 UC 追加、Rule 改名、構造変更)
- **v0.X.Y**: 軽微な修正 (typo、補足、Rule の言い回し)
- **v1.0.0**: 方法論本体への昇格完了後、実運用承認

Phase 2 試行を実施したバージョンの実装は **不変** として保持し、回帰検証に使う。

### 4.4 Addendum の命名規範

`90-feasibility-notes.md` の Addendum は次のように:

- **Addendum A**: Phase 2 v0.1 GRID 結果
- **Addendum B**: v0.2 改訂 + Phase 2 v0.2 結果
- **Addendum C**: 隣接 Capability ドラフトの境界調整負荷
- **Addendum D**: Phase 2 IMAGE_VARIANT v0.1 結果 (規範継承性検証)
- **Addendum E** 以降: 新規 Capability 試行ごとに追加

---

## 5. 反復ループの停止条件

プロトコルは **無限に反復しない**。次のいずれかで停止:

### 5.1 品質停止条件 (目標達成)

- unclear ≤ 3
- suspected_overreach = 0
- Anchor tests AT-XX 全合格
- Random walk 1000-step で実バグ検出なし
- 新規発見の穴が typo / 改訂取りこぼしのみ (真の新規が出ない)

GRID v0.2 と IMAGE_VARIANT v0.1 は両者ともこの停止条件を **満たしている**。

### 5.2 リソース停止条件

- 反復回数が 3 回以上 (= 同じ Capability で v0.4 以降は通常不要)
- 各反復で新規発見の穴が 1 件以下 (= 改訂による品質改善が飽和)

### 5.3 構造変更停止条件

- 反復しても本質的解決にならない場合 (= サンプルの構造を再検討すべき)
- 例: Capability 境界の見直し、新規 Capability への分割

---

## 6. プロトコルが扱わない領域 (注意)

### 6.1 多 Capability の境界調整

Inverse Audit Protocol は **単一 Capability の品質確保** に焦点を当てる。
多 Capability 同時運用での境界調整 (cascade decision、shared concepts 等) は
本プロトコルの **守備範囲外**。これは `Addendum C` の主題で、別途のプロトコルが必要 (将来の 16-coordinator-pattern 等)。

### 6.2 実プロジェクトでの試験運用

PoC (本ディレクトリ) は人工的に隔離した実験。実プロジェクトでは:

- 経時的に要求が変わる
- ステークホルダーが複数
- 既存システムとの互換性

など、本プロトコルが扱わない複雑性が加わる。
実運用への移行は別フェーズ (`19-production-rollout.md` 等として将来検討)。

---

## 7. 採用判定

| 評価軸 | 結果 |
| --- | --- |
| 実証根拠 | Phase 2 試行 3 回で停止条件 (§5.1) を 2 回満たした |
| 適用コスト | 中 (ディレクトリ構造 + バージョニング規範を導入する負荷) |
| 既存方法論との整合 | 補完関係 (監査方向の既存 01〜10 と直交) |
| 認知負荷 | 中 (4 Phase が明確に分離されているので分かりやすい) |

---

## 8. 関連ドキュメント

- 11-three-layer-disambiguation.md — 本プロトコルの Phase 1 で必須となる執筆規範
- 12-must-decide-and-document.md — 本プロトコルの Phase 2/3 で観測する第三カテゴリ
- 14-author-checklist.md — Phase 1 執筆作業の実践チェックリスト
- 実証根拠: `docs/capability-bom-sample/90-feasibility-notes.md` Addendum A / B / C / D
- 実装履歴: `experiments/phase2-impl/`, `phase2-v02-impl/`, `phase2-image-variant-impl/`
