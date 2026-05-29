# BOM ↔ Implementation Conformance Checker (候補 E ステップ 5)

残課題 **F-1 / F-2 / D-1 / D-3** が「BOM の宣言と実装の実体のズレ」に収束したことを受けた、
**machine-checkable な照合** のプロトタイプ。方法論側の規範は
`docs/capability-bom-audit/methodology/22-bom-conformance-check.md`。

## 実行

```bash
# 既定ターゲット (デモ用の凍結 impl phase2-cocompose-impl。B-D3 を持つため意図的に FAIL):
python docs/capability-bom-audit/tools/bom-conformance-check/checker.py

# 受け入れゲートとして使う場合は *生成物の src を CLI で明示* する:
python docs/capability-bom-audit/tools/bom-conformance-check/checker.py <生成物>/src
```

BOM は常に正準 `docs/capability-bom-audit/samples/` を参照。実装 src は CLI 引数で差し替え可能で、
**相対パスは cwd → repo root の順で解決**する (絶対パスはそのまま)。PyYAML が必要。

## 何を照合するか

| カテゴリ | 内容 | 捕捉する残課題 |
| --- | --- | --- |
| **C3** (静的) | `canonical_failure_reasons.applies_to` ↔ 各 UC の `failure_reasons` の双方向一致 | D-1 |
| **C1** (動的) | UC の宣言失敗理由が producible か / `guaranteed_by` で upstream 保証か | F-1 / F-2 |
| **C2** (動的) | BOM が宣言した precondition を実装が強制するか | D-3 |

- 対象 BOM: `docs/capability-bom-audit/samples/` の GRID / IMAGE_VARIANT / RENDERING の 21-*.yaml。
- 対象実装: `experiments/phase2-cocompose-impl/`(F-1/F-2/D-3 が実在する n=2 実装)。
  - 後発の `phase2-v03-n3-impl` は UC-05 が改良され C1 を通る — 照合がバグ実装と修正実装を
    区別できることの傍証。`IMPL_SRC` を切り替えれば他実装にも向けられる。

## 結果 (Addendum I 参照)

- BEFORE (BOM 未修正): FLAGS 6 — C3 で GRID の 3 drift、C1 で UC-05 の F-2 を 3 件。
- AFTER (BOM 修正後): FLAGS 1 — C2 が凍結実装の D-3 バグを検出 (意図通り、再発防止が効く)。

修正内容:
- GRID `21-grid-composition.yaml`: UC-07 に `BothPlacementsBelongToSameGrid` / `CrossGridSwapNotAllowed`
  (D-3 解消) + `InvalidWeights`/`OutOfBounds`/`Conflict` の applies_to drift 修正 (C3)。
- IMGVAR `21-image-variant-management.yaml`: UC-05 の per-field `Invalid*` に `guaranteed_by` 注記
  (F-1/F-2 解消 — 値オブジェクト/enum が upstream で保証)。

## 出力と終了コード (受け入れゲート)

- 末尾に **`GATE: PASS/FAIL`** と **coverage manifest** を出力する。
  - flag が残れば **`GATE: FAIL` + 非ゼロ終了** (CI / 生成受け入れゲート)。
    凍結 `phase2-cocompose-impl` は D-3 バグを持つため現状 **exit 1** (C2 の 1 FLAG)。準拠実装なら exit 0。
  - **coverage manifest**: **(UC, failure_reason) ペア単位**で `guaranteed_by` / dynamically-probed /
    unverified-by-tool に分類。共有理由 (NotFound 等) は probe した UC のみ probed と数える。
    **未検証 (動的 probe 未整備) を可視化**し「pass=全 OK」の誤読を防ぐ。
- 運用規範 (Phase 2 生成の受け入れゲート化) は `docs/capability-bom-audit/methodology/22-bom-conformance-check.md §4.1`。

## 既知の制約 (将来拡張)

- C1 の `guaranteed_by` は **指す値オブジェクト/enum を無効入力で構築し reject を動的検証する**
  (実装済み: `OccupySize(0,0)`→ValueError 等)。注記だけで pass する抜け道は塞いである。
- C1 / C2 の probe は GRID UC-07 / IMGVAR UC-05 に focused。全 UC への汎用ハーネス化は次フェーズ。
