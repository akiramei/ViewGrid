# ViewGrid bomdd/ — ハブ先行の as-built 起票(部分移行)

## 経緯

2026-07-07 の stage-0 健診(変更トポロジー測定 — 宣言なしリポの遡及健診。
impact-retrospective.py の下位形・scratch 治具)で、以下を実測:

- code コミット 245 件中 **複数 unit 跨ぎ 49.8% / 層跨ぎ 40.8%**
- 変更集中ハブ = `Application/ViewModels`(multi-unit コミットの 68%)と
  `Presentation/Views`(66%)。最頻同時変更ペア = VM↔View 62 回
- ファイル粒度ハブ = GridWorkspaceViewModel.cs(39回/64KB)・
  CopyPropertiesViewModel.cs(37回/54KB)・GridCanvasView.axaml.cs(35回/117KB・2,498行)・
  GridWorkspaceView.axaml(34回/53KB)

scale-01 の教訓(61 §1.4: 実 under はハブ unit へ系統的に集中する)に従い、
全 unit の一括起票ではなく**ハブ unit から as-built M-BOM を起こす**順路を採る。
数字の全文は [52-metrics.yaml](52-metrics.yaml)。

## 本起票の範囲(部分性の明示)

| 成果物 | 状態 |
|---|---|
| 32-mbom.yaml | **ハブ 8 unit のみ**(file unit 6 + catch-all 2)。他 21 dir-unit 相当は未起票 |
| 30-ebom.yaml | **最小 4 品目**。docs/capability-bom-audit/samples の 3 capability を昇格+SHELL |
| 52-metrics.yaml | stage-0 健診の数字+ハブ台帳(61 §1.4 の既定点検リスト) |
| 00/10/20/31/33/34/60 系 | **未起票**。ECO 運用開始時に必要分から起こす |
| plm-intake/migration-inventory.md | **未実施**(existing-project-migration.md の完全フローは後続) |

## 規律

1. **M0 Freeze**: 本起票では実装コードを一切変更していない(as-built = 宣言=実装)。
2. 旧世代 PoC(docs/capability-bom-audit/ — F-P13〜16 系列)は**参照**で扱い、移動しない。
   capability 境界の権威(R-08 等)は当該文書が引き続き持つ。
3. 以後の実装変更は ECO(60-change-register)経由。affected_refs には
   [52-metrics.yaml](52-metrics.yaml) のハブ台帳掲載 unit を既定点検すること。

## 次の一歩(候補)

- 60-change-register.yaml の空起票+最初の実 ECO で運用開始(影響宣言 → 実 diff 突合が回り出す)
- catch-all(M-VM-REST-004 / M-VIEW-REST-008)の段階的縮小(ECO-036 系列の型)
- migration-inventory の完全実施(10/20/33 への対応付け)
