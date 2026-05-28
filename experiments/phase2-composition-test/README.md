# Phase 2 候補 E ステップ 1 — 複数 Capability 合成試行

> **実施日**: 2026-05-26
> **目的**: 既存 2 実装 (GRID v0.2 + IMAGE_VARIANT v0.1) を 1 プロセスに同居させ、
> Capability 境界を結線できるか観測する
> **仮説**: 規範継承は Capability 内部品質を揃えるが、Capability 間のコード規約整合は保証しない

## 構成

| ファイル | 内容 |
| --- | --- |
| `compose.py` | 合成試行スクリプト。両パッケージを import し、境界結線を試みる |
| `RESULTS.md` | 実行結果と観測された不整合の詳細 |
| `README.md` | このファイル |

## 実行方法

```bash
# Windows (UTF-8 出力のため環境変数を設定)
cd experiments/phase2-composition-test
PYTHONIOENCODING=utf-8 PYTHONUTF8=1 python compose.py
```

合成対象:
- GRID_COMPOSITION v0.2: `../phase2-v02-impl/grid_composition/` (flat layout)
- IMAGE_VARIANT_MANAGEMENT v0.1: `../phase2-image-variant-impl/src/image_variant_management/` (src layout)

## 結論 (要約)

2 つの実装は **直接合成できない**。各 AI セッションが独立に決めた規約が境界で衝突する:

1. モジュールレイアウト (flat vs src/)
2. 共有値オブジェクトの型同一性 (別モジュール定義 = 別型)
3. identity 表現 (uuid.UUID vs str)
4. Result ラッパ命名 (Err vs Failure)
5. UC コンテナ命名 (UseCases vs Service)
6. 境界インターフェース型 (UUID/bool vs str/Result[bool]) → アダプタ必須

詳細と方法論への含意は `RESULTS.md` および
`../../docs/capability-bom-sample/90-feasibility-notes.md` Addendum E を参照。
