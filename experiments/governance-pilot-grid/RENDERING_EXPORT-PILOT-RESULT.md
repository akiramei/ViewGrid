# F-P14 — RENDERING_EXPORT as-built BOM + F-P13 実運用 (PILOT-RESULT)

> 実施 2026-06-01。3 つ目の Capability。`RENDERING_EXPORT.as-built.v0.1.yaml` の作成と、
> **F-P13 (工程管理層 v0.1) の change classification を実運用**した結果。
> 目的: (1) 出力パイプラインを地図化、(2) IO-1/IO-3 是正が RENDERING で正しく使われているか両側確認、
> (3) **F-P13 を「使ってみて」足りない分類/gate を v0.2 の材料として回収**。

## 1. 何をしたか
- 実コード (`SkiaGridImageRenderer` 中核 + RenderGrid/ExportGrid UseCase + GridOutputViewModel) × manual (`06-output.md`)
  × sample BOM (`21-rendering-export.yaml`, scope=focused) を突合し as-built BOM v0.1 を作成。
- Capability **三角形が閉じた**: 配置妥当性=GRID / 画像意味=IMGVAR / 合成=RENDERING、で read-only 境界が一致。
- **IO-1/IO-3 の RENDERING 側利用を裏取り**: crop 優先は Core `CropFraction.ResolveEffective` に 2 サイトで委譲 (独立再実装なし=IO-1 整合)、Fork 由来 Regions も `PaintProtectedRegions` で描画 (IO-3 整合)。

## 2. ★ F-P13 を実運用した所感 (= F-P14 自身が as_built_bom_authoring)
F-P14 という作業自体が、F-P13 §9 で **v0.1 では詳細化せず v0.2 送りとした `as_built_bom_authoring` change_type のインスタンス**だった。
→ §9 の「実例はあるが v0.2 へ」が正しかったことを実地で確認。as_built_bom_authoring の gate は実運用で次と判明:
```yaml
as_built_bom_authoring:
  required_gates:
    - source_reconciliation      # 実コード × manual × sample BOM の三方突合 (file:line 裏取り)
    - cross_capability_consistency  # 既存 BOM と境界突合 (gap/overlap なし)
    - finding_classification     # 各 finding を change_type に仕分け (本書 §3)
    - headline_evidence          # 主要所見はコード裏取り (例: PhotoBoardStyle=3 を enum で確認)
```

## 3. finding の F-P13 分類 (change classification 実運用)
| finding | 内容 | F-P13 type | required gates (適用) | status |
| --- | --- | --- | --- | --- |
| (BOM 作成) | RENDERING as-built 地図化 | **as_built_bom_authoring** | source_reconciliation / cross_capability / classification | ✅ done |
| RO-2 / RDD-3 | 丸め方針が 4 箇所で不統一 (AwayFromZero/ToEven/Ceiling) | **deliberate_decision** (候補) + gate gap | current_behavior_evidence ✅ / options_and_impact ✅ / **human_decision 待ち** | pending |
| RD-2 | 未解決 copy = NotFound エラー (sample は除外) | **deliberate_decision** (候補) | evidence ✅ / human_decision 待ち | pending |
| RDD-1,2,4,5,6 | 透過背景 / α閾値8 / DPI非埋込 / 1×1 fallback / PNG100 | **deliberate_decision** (候補) | evidence ✅ / human_decision 待ち | pending |
| RD-4,5,6 | manual 10 styles vs 実 3 / 背景色 / α>0 vs α≥8 | **doc_drift** (★ F-P13 に型が無い) | — | gap (§4) |
| RO-1 | scaling/alignment 意味写像が Infra 層 | observation → 将来 drift_elimination | (現状単一実装=drift でない) | latent |
| RD-3 | neutral DTO 未採用 (Core entity 直接保持) | design 乖離 (sample 抽象未採用) | 記録のみ | noted |

→ **F-P13 の 5 change_type は actionable finding を概ね綺麗に仕分けられた** (deliberate_decision が大半、as_built_bom_authoring が本作業)。
   ただし **doc_drift だけ型が無く**、deliberate_decision が **一度に 7 件 surface** した = 下記 v0.2 gap。

## 4. ★ F-P13 v0.1 に足りなかったもの (v0.2 の材料、机上でなく RENDERING で出た)
1. **`doc_drift` change_type** (RD-4/5/6): 「manual が言うこと vs コードがすること」のズレを直す型が無い。
   gates 案: `evidence (manual §x vs code)` / `decide fix-doc-or-code` / `manual_update`。as_built_bom_authoring の副産物として常に出る。
2. **`rendering_numeric_policy` gate** (RO-2/RDD-3): 描画系の丸め/clamp/snap 方針を**触るときは一元方針に照らす**ことを要求する gate。
   crop の midpoint gap (D2a) と同型の silent 盲点が rendering 側にも在る (丸めモード分散)。F-P13 の gate 語彙に numeric policy が無い。
3. **`visual_oracle` gate** (golden image / pixel-diff threshold): F-P13 の `oracle_tests` は**決定論的な値 oracle**を前提。rendering は
   pixel 出力なので、sub-pixel 丸めずれを捕まえるには golden image + pixel-diff 閾値が要る (既存 SkiaGridImageRendererTests はあるが
   丸めモード変更を捕まえる falsifier は無い)。RO-2 のリスクは「value oracle では見えない」。
4. **`preview_export_equivalence` gate** (RR-09): 同一意味を 2 出力経路 (preview/export) が共有することを保証する gate。
   本 Capability は構造的に満たす (共有 UseCase) が、二経路を持つ Capability 一般に効く gate として v0.2 へ。
5. **deliberate_decision の backlog 概念**: capability 1 本の authoring で deliberate_decision 候補が **7 件まとめて** 出た。
   F-P13 の deliberate_decision は 1 件ずつ裁定する形だが、authoring pass は**バッチで surface** する。v0.2 で「decision backlog → まとめ裁定」を扱う。

## 5. 裁定保留 (human が必要 = F-P13 deliberate_decision gate)
RDD-1..6 + RD-2 は **現挙動が意図か**を人間が固定すべき RENDERING 固有の設計判断 (D2a/D2b と同列、実コード変更は伴わない見込み)。
特に **RDD-3 (丸め方針)** は D2a (ToEven) と整合させるか rendering_numeric_policy として別途明文化するかの判断。
本 pilot では **surface に留め、裁定は別ターン** (IO-1/IO-3 と違い production バグではないため急がない)。

## 6. 結論
- **3 つ目の Capability で三角形が閉じ、IO-1/IO-3 の是正が RENDERING で正しく機能していることを両側突合で確認**。
- **F-P13 v0.1 は実運用に耐えた**: actionable finding を 5 type に仕分けでき、§9 で予告した as_built_bom_authoring が実在と確認。
- **v0.2 への具体的な材料を 5 つ回収** (doc_drift type / rendering_numeric_policy / visual_oracle / preview_export_equivalence / decision backlog) = 机上でなく RENDERING で実際に出た gap。これが「F-P13 を使ってみる」の成果。

## 7. next
- (a) v0.2: §4 の 5 gap を F-P13 へ取り込む (doc_drift type + 3 gate + decision backlog)。
- (b) RDD-*/RD-2 の human 裁定 (deliberate 化、別ターン)。
- (c) manual drift (RD-4/5/6) の修正 (doc_drift の最初の実例)。
- (d) RO-2 の rendering_numeric_policy 明文化 (D2a と整合 or 独立方針)。
- 成果物: `RENDERING_EXPORT.as-built.v0.1.yaml` + 本 RESULT。実コード変更なし (観測・地図化のみ)。
