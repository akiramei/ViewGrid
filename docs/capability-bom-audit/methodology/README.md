# Capability BOM Audit — 方法論 (本体 01〜10 + 拡張 11〜14 / 21 / 22 / 23)

> **Encoding: UTF-8** (BOM なし、LF)。
> **構成**: `01〜10` = 方法論本体 (canonical)、`11〜14 / 21 / 22` = PoC で得た拡張 (status: draft)。
> 本体 01〜10 は元 OneDrive に置いていたが、本再編でリポジトリに取り込み自己完結化した
> (OneDrive はミラー/バックアップ扱い。旧 Shift-JIS 版は backup ZIP に保全)。

## このフォルダの目的

Capability BOM Audit の **方法論一式** を 1 箇所に集約する:

- **本体 (01〜10)**: Capability / Rule / Role / Decision / Runtime Mapping / Overreach の定義、
  監査プロンプト指針 (09)、誤解の整理 (10)。**「監査方向 (コード → BOM 観測)」** の正典。
- **拡張 (11〜14 / 21 / 22)**: ViewGrid を題材にした Phase 2 試行で得た、**「生成方向 (BOM → コード)」**
  と複数 Capability 運用のための知見。status: draft (昇格ポリシーは末尾)。

実証根拠は `../evaluation/90-feasibility-notes.md` (Addendum A〜J) と `../evaluation/91-findings-ledger.md`。

## 本体 (01〜10) と拡張の関係

| 本体 | 内容 | 拡張との関係 |
| --- | --- | --- |
| 01-why-capability-bom-audit.md | 背景・動機 | 引用元 |
| 02-core-concepts.md | Capability / Rule / Role / Decision 定義 | 12 (MUST_DECIDE_AND_DOCUMENT) で拡張 |
| 03-role-taxonomy.md | Role 8 種類 | 変更なし |
| 04-decision-taxonomy.md | Decision 7 種類 | 変更なし |
| 05-rule-ledger.md | Rule の記録方法 | 11 (三層構造) で参照 |
| 06-runtime-mapping.md | 意味構造と実装の対応 | 変更なし |
| 07-overreach-detection.md | 越境検出 | 11 で参照 |
| 08-viewmodel-audit-example.md | 監査の実例 | 変更なし |
| 09-ai-audit-prompt-guide.md | 監査者向けプロンプト | 12 / 14 で拡張 (執筆者向け第三カテゴリ) |
| 10-common-misunderstandings.md | 誤解の整理 | 変更なし |

## 本ディレクトリの構成

### 共有用サマリ (まずここから)

| ファイル | 内容 |
| --- | --- |
| [`00-summary-of-changes.md`](00-summary-of-changes.md) | 11〜14 が 01〜10 にもたらす変更と期待効果のサマリ。第三者への説明・採用提案用 |

### 最重要 4 件 (本ドラフト群)

| 番号 | ファイル | 内容 | 由来 |
| --- | --- | --- | --- |
| 11 | [`11-three-layer-disambiguation.md`](11-three-layer-disambiguation.md) | narrative + algorithmic + executable の三層で曖昧さを塞ぐパターン。AI の局所最適化衝動への防御 | Phase 2 v0.2 / IMAGE_VARIANT |
| 12 | [`12-must-decide-and-document.md`](12-must-decide-and-document.md) | ALLOWED/FORBIDDEN だけでは捉えきれない第三カテゴリ。実装決定の追跡 | Phase 2 全試行 (累計 25 件発生) |
| 13 | [`13-norm-inheritance-and-inverse-audit.md`](13-norm-inheritance-and-inverse-audit.md) | 規範継承性 (新 Capability v0.1 が既存 v0.2 と同等品質) + 反復検証プロトコル正典化 | Phase 2 IMAGE_VARIANT |
| 14 | [`14-author-checklist.md`](14-author-checklist.md) | 人間執筆者向け実運用チェックリスト | Addendum B / C / D |

### 先行ドラフト 2 件 (候補 E の合成・照合検証から導出)

| 番号 | ファイル | 内容 | 由来 |
| --- | --- | --- | --- |
| 21 | [`21-codebase-convention-contract.md`](21-codebase-convention-contract.md) | 複数 Capability の合成可能性を保証する横断規約契約 (identity 表現 / 共有型配置 / Result ラッパ / レイアウト / 命名 / 境界型 / 消費側 read ポート前倒し)。規範継承が届かない範囲を補完 | Addendum E〜H |
| 22 | [`22-bom-conformance-check.md`](22-bom-conformance-check.md) | BOM (canonical_failure_reasons・preconditions) ↔ 実装の machine-checkable 照合 (C3/C1/C2)。残課題 D-1/F-1/F-2/D-3 を検出・解消 | Addendum I |

> **番号について**: 21 は本来副候補 18 (Shared Concepts Schema) と対になる **物理レイヤ** の文書。18 が未ドラフトのため先行ドラフトとして 21 に置いた。22 は 21 の physical 契約と semantic カタログ (失敗理由) の整合を照合する。昇格時に再番号付けしてよい。

### 統合ブループリント 1 件 (Authoring/Operating 層、候補 E 完了後の方向づけ)

| 番号 | ファイル | 内容 | 由来 |
| --- | --- | --- | --- |
| 23 | [`23-authoring-and-operating-model.md`](23-authoring-and-operating-model.md) | 既存 01〜22 の **上に乗る運用モデル**。人間の意味資料 → 意味設計コンパイラ → AI実装 の二層化、全工程 × 人間/AI 主従 × ツール × Gate のワークフロー、UI 意味契約層、shift-left 連続体 (① authoring compiler / ② 照合ゲート 22 / ③ 事後監査 01-10)。コンパイラの診断カタログは findings ledger (91) から収穫する | 候補 E 完了後の方向づけ議論 |

> **位置づけ**: 23 は内側の方法論 (01〜22 = AI に BOM から実装させ監査する) の **外側のオーケストレーション**。新規の (b) 意味設計コンパイラ仕様 / (d) UI 意味契約 は本ブループリント確定後に個別文書へ分割する想定 (§7 未確定論点)。

### 副候補 6 件 (本ディレクトリでは未着手、将来のドラフト候補)

| 番号 | テーマ | 由来 |
| --- | --- | --- |
| 15 | Anchor Tests Spec (詳細規範) | Phase 2 v0.2 / IMAGE_VARIANT |
| 16 | Coordinator Pattern (Capability 外調停層) | Addendum C |
| 17 | Declaration-only Rules (Capability 跨ぎの Rule) | Addendum C |
| 18 | Shared Concepts Schema (= 21 の semantic 対) | Addendum C |
| 19 | Cross-Capability Naming Convention | Addendum C |
| 20 | Revision Checklist (改訂作業チェックリスト) | Addendum B / D |

これらは 14 (Author Checklist) から **下位パターンとして言及** することで、最重要 4 件を読めば全体像が掴めるようにした。本格的なスタンドアロン文書化は次フェーズ。21 (横断規約契約) は Addendum E で必要性が実コード実証されたため先行ドラフト化した。

## 読み順 (推奨)

```text
既存 01〜10 を一通り把握済み の読者向け:

   11 (三層構造) → 12 (第三カテゴリ) → 13 (規範継承性) → 14 (執筆者向け)
   └─ 方法論の防御メカニズム  └─ 実装の自由度の構造化  └─ 運用プロトコル  └─ 実践チェックリスト
```

各文書は独立して読めるが、上記順序で読むと前文書の概念を順に積み上げる構造になっている。

## エンコーディング

本体 01〜10 と拡張 00 / 11〜14 / 21 / 22 はすべて **UTF-8 (BOM なし、LF)**。
旧 Shift-JIS 版 01〜10 は `~/OneDrive/ドキュメント/Capability BOM Audit-shift-jis-backup-20260526.zip` に保全。
OneDrive の 01〜10 は本再編で **リポジトリ (本フォルダ) に取り込み済み**。OneDrive はミラー/履歴扱い。

## 昇格ポリシー (draft 拡張 → 本体への統合、手戻り回避)

01〜10 はリポジトリ内に取り込んだので「OneDrive へ移動」という作業は不要になった。
残る昇格作業は **拡張 (11〜14 / 21 / 22) を本体 01〜10 の体系へ正式統合** すること
(章番号の確定・本体への参照追加)。

**原則: baseline が固定されるまで本体へ正式統合しない。** 新発見が続く段階で統合すると
再番号付け等の手戻りが出る (Addendum F〜J で契約が v0.1→v1.0 と churn した)。

- **draft 状態 (11〜14 / 21 / 22)**: 反復改訂の対象。実験で新発見があれば随時更新。
- **統合の前提条件**:
  1. 契約が baseline 固定 (`../samples/00-convention-contract.md` v1.0 = 達成済み)
  2. 該当 finding が `../evaluation/91-findings-ledger.md` で ✅/📐 (解消 or 規範化) 済み
  3. 機械照合 (22) で GATE: PASS、または unverified 項目が ledger で追跡済み
- 現状: 11〜14 / 21 / 22 は上記をおおむね満たす。**統合は基盤固定済みの本フェーズ以降に実施可能**。
  ただし Coordinator (16) / Shared Concepts (18) など未実証の副候補は draft のまま据え置く。

## 関連ドキュメント

- 実証根拠: `../evaluation/90-feasibility-notes.md` (Addendum A / B / C / D)
- v0.2 GRID サンプル: `../samples/`
- IMAGE_VARIANT v0.1 サンプル: `../samples/image-variant-management/`
- 方法論本体: `01-10-*.md` (本フォルダ)
