# Capability BOM Audit — サンプル成果物 (IMAGE_VARIANT_MANAGEMENT)

> **Sample Version: v0.1** (隣接 Capability ドラフト — 境界調整負荷の実測が主目的)
> 親ディレクトリの GRID_COMPOSITION サンプルは v0.2 相当。本ディレクトリは初版

## このディレクトリの目的

GRID_COMPOSITION サンプル (`../`) と並ぶ **2 つ目の Capability サンプル**。
PoC として:

> **複数 Capability のサンプルを揃えた時、境界調整・用語整合・カスケード決定 等にどれだけの追加コストがかかるか**

を実測することが主目的である。

GRID_COMPOSITION v0.2 で確立した **三層構造 (narrative + algorithmic + executable)**、
**MUST_DECIDE_AND_DOCUMENT 第三カテゴリ**、**Anchor tests 同梱規範** は本サンプルにも
初回から組み込む (v0.1 だが v0.2 学習を継承)。

## 担当範囲

| 項目 | 内容 |
| --- | --- |
| 対象 Capability | `IMAGE_VARIANT_MANAGEMENT` のみ |
| 中核概念 | **ImageCopy (論理コピー / 派生物)**、**ImageAsset (元画像)** |
| 関連する隣接 Capability | `GRID_COMPOSITION` (依存される側、CopyId のみ参照されている)、`RENDERING_EXPORT` (依存される側、ImageCopy の特性を描画解釈)、`WORKSPACE_MANAGEMENT` (依存する側、ストレージ提供) |
| 対象外 | ProtectedRegion (PhotoBoard 連動の保護領域、v0.2 候補)、サムネ生成 (RENDERING_EXPORT)、ファイル物理保存形式 (Repository) |

## 成果物の構成

```text
docs/capability-bom-audit/samples/image-variant-management/
├── README.md                              ← このファイル
├── 10-requirements.md                     ← 要求仕様
├── 20-capability-bom.md                   ← PLM/BOM 人間可読版
├── 21-image-variant-management.yaml       ← 機械可読版 (正準)
├── 30-design.md                           ← 設計書 (Rule ledger / Entity / Event / Test 規範)
└── 40-ai-implementation-prompt.md         ← AI 実装プロンプト雛形
```

境界調整負荷の評価メモは親ディレクトリの `../../evaluation/90-feasibility-notes.md` の Addendum C を参照。

## 親 Capability (GRID_COMPOSITION) との境界

| 項目 | 担当 |
| --- | --- |
| `ImageCopy` の概念定義 | **本 Capability が権威** |
| `ImageCopy` の特性編集 (Scaling / Crop / Transform 等) | **本 Capability が権威** |
| `ImageCopy` の存在性確認 (`ImageCopyExists`) | **本 Capability が UC として提供** |
| `ImageCopy` を CopyId で参照 | GRID_COMPOSITION (本 Capability の外) |
| `ImageCopy` 削除時の Placement への影響 | **どちらにも属さない (上位 Coordinator)** ── 詳細は 30-design.md §5 |
| `ImageCopy` の描画解釈 | RENDERING_EXPORT (本 Capability の外) |

## 関連ドキュメント

- 親ディレクトリ: `../README.md` (GRID_COMPOSITION サンプルの全体像)
- 方法論本体: `../../methodology/` (01〜10)
- Phase 2 結果 / 境界調整負荷観測: `../../evaluation/90-feasibility-notes.md` Addendum A / B / C
