# Capability BOM Audit — 方法論本体への昇格候補ドラフト

> **Status: draft** (PoC で得た知見を方法論本体 (01〜10) に昇格させるためのドラフト集)
> **Encoding: UTF-8** (BOM なし、LF)。既存 01〜10 も 2026-05-26 に UTF-8 化済み — 旧 Shift-JIS 版はバックアップ ZIP として保全
> **Authored: 2026-05-25** (Phase 2 IMAGE_VARIANT_MANAGEMENT 試行直後)

## このディレクトリの目的

ViewGrid を題材にした **3 回の Phase 2 試行** (GRID v0.1 / GRID v0.2 / IMAGE_VARIANT v0.1)
を通じて得られた、方法論本体に取り込むべき知見を **昇格候補ドラフト** として整理する。

実証根拠は `docs/capability-bom-sample/90-feasibility-notes.md` の Addendum A / B / C / D。

## 既存方法論本体 (01〜10) との関係

| 既存 | 内容 | 本ドラフトとの関係 |
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

### 先行ドラフト 1 件 (複数 Capability 合成から導出)

| 番号 | ファイル | 内容 | 由来 |
| --- | --- | --- | --- |
| 21 | [`21-codebase-convention-contract.md`](21-codebase-convention-contract.md) | 複数 Capability の合成可能性を保証する横断規約契約 (identity 表現 / 共有型配置 / Result ラッパ / レイアウト / 命名 / 境界型)。規範継承が届かない範囲を補完 | Addendum E (候補 E ステップ 1) |

> **番号について**: 本来は副候補 18 (Shared Concepts Schema) と対になる **物理レイヤ** の文書。18 が未ドラフトのため先行ドラフトとして 21 に置いた。昇格時に 18 とセットで再番号付けしてよい。

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

## エンコーディングと最終形

既存 01〜10 と本ディレクトリの 00 / 11〜14 はすべて **UTF-8 (BOM なし、LF)** で統一済み (2026-05-26)。
旧 Shift-JIS 版の 01〜10 は `~/OneDrive/ドキュメント/Capability BOM Audit-shift-jis-backup-20260526.zip` にバックアップ保全。

ユーザーがレビュー後:

1. 受容可能と判断 → `~/OneDrive/ドキュメント/Capability BOM Audit/` 配下へ単純移動
2. 一部のみ受容 → 個別ファイルを移動
3. 全面書き換え必要 → 本ディレクトリで反復改訂

を選択する想定。

## 関連ドキュメント

- 実証根拠: `../capability-bom-sample/90-feasibility-notes.md` (Addendum A / B / C / D)
- v0.2 GRID サンプル: `../capability-bom-sample/`
- IMAGE_VARIANT v0.1 サンプル: `../capability-bom-sample/image-variant-management/`
- 既存方法論本体: `~/OneDrive/ドキュメント/Capability BOM Audit/01-10-*.md` (UTF-8)
