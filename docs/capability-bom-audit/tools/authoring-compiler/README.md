# 意味設計コンパイラ (Meaning Design Compiler)

`methodology/23-authoring-and-operating-model.md` の **① authoring-time コンパイラ**。
人間の自由文要求 (prose) を Capability BOM へ変換し、欠落・矛盾を authoring 時に診断する。
形態は **(b) 決定的ツール + AI ハイブリッド** (§3.6)。**2 つの半身**で 1 つのコンパイラ:

| 半身 | 実体 | 役割 |
| --- | --- | --- |
| **AI 抽出器** (前段) | [`extractor-spec.md`](extractor-spec.md) | prose → BOM 候補 へ lift + 正規化 + provenance/source タグ + 意味的ギャップの診断 (proposal-ERROR/WARNING/INFO) |
| **決定的検査器** (後段) | [`../bom-conformance-check/checker.py --authoring <bom.yaml>`](../bom-conformance-check/) | 構造ルール (SCHEMA/C3/PRECOND/REF/UI) + AI タグの機械的 enforcement (PROV) |

## 分界点 (どちらが何を捕捉するか)

- **AI 抽出器** = 意図の不完全性 (構造に現れない「あるべきだが欠落」)。意味判断。
- **決定的検査器** = 内部不整合 / 規約準拠 / 相互参照。再現可能。
- **橋 = provenance タグ**: AI が `unresolved`/`proposal` でタグ付け → 検査器の **PROV** が機械的に block。検査器は意味を理解せずタグを enforce する (§3.7)。
- **ブロックする ERROR は必ず決定的検査器が出す** (再現性必須、§3.6)。AI は検出+タグのみ。

## 使い方 (2 段)

```bash
# 1. AI 抽出器: extractor-spec.md を AI セッションに渡し、prose 1 ファイルから BOM 候補 + 診断を生成させる
#    ({{INPUT_PROSE}} / {{OUT_BOM}} / {{OUT_DIAG}} を差し替え)

# 2. 決定的検査器: 生成された BOM に static 検査を回す
python docs/capability-bom-audit/tools/bom-conformance-check/checker.py --authoring <OUT_BOM>
#    -> AUTHORING GATE: PASS / FAIL / NEEDS-AI
```

`error`/`unresolved` が残る限り実装フェーズに進まない (§3.5 の単一ゲート)。手戻りはコードでなく **prose/BOM の改訂**として蓄積する。

## 実証

- prototype 実測: `../../../../experiments/authoring-compiler-prototype/RESULTS.md` (分界点 + calibration C-1/C-2/C-3)。
- 本 spec (v1.0) は prototype の subagent プロンプトを Step 0 baseline (§7.2) で固めたもの。主な確定: RULE A 正規化 / RULE B 推定 severity (成否を左右する未定義は proposal-ERROR) / RULE C provenance↔診断 severity 結合 (prototype 不整合の修正)。
