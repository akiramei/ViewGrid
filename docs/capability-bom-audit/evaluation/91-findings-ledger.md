# 91 — Findings Ledger (PoC 全 findings の単一索引)

> **目的**: Addendum A〜I に散在する全 finding を **1 箇所に索引化** し、各 finding の
> 解決方法・現状・機械照合 (22-bom-conformance-check) で防げるか を addressable にする。
> 次フェーズ以降の実験が 9 個の Addendum を読み直さず、ここを起点にできるようにする。
>
> **凡例 (現状)**:
> - ✅ **解消 (サンプル修正)**: BOM/設計を直して閉じた
> - 📐 **規範化**: 方法論ドキュメント (11〜14 / 21 / 22) に昇格して防ぐ
> - 🔍 **照合で防止**: 22 の C1/C2/C3 が機械検出する
> - 🟡 **オープン**: 未着手の v0.3+ 候補
>
> **ID の整理 (重要)**: Addendum B と Addendum D の双方に "D-1/D-3" ラベルがあり衝突していた。
> 本台帳では **Addendum 接頭辞付き ID** に統一する (例: `B-D3` = Addendum B の cross-grid swap、
> `Dpc-1` = Addendum D の昇格候補 1)。以後はこの ID を正準とする。

---

## 1. 単一 Capability の仕様穴 (GRID / IMAGE_VARIANT)

| ID | 内容 | 由来 | 解決 | 現状 |
| --- | --- | --- | --- | --- |
| `A-1` | UC-02〜10 の NotFound 失敗理由が欠落 | Add. A | canonical_failure_reasons に NotFound 新設 (v0.2) | ✅ + 🔍C3 |
| `A-2` | UC-09 SetOrder の order_value 値チャネル未定義 | Add. A | YAML inputs に order_value 明示 (v0.2) | ✅ |
| `A-3` | UC-07 Swap の自身排除で A/B 新位置衝突を取り逃す (**実バグ**) | Add. A | 三層 (R-02 NOTE / §2.2 手順 / AT-03 + W-3) | ✅ 📐11 |
| `A-O1/O2` | R-02/R-06 の suspected_overreach | Add. A | v0.2 で 0 件化 (UC-07 手順を workflow_decision と明記) | ✅ |
| `B-D1` | R-08 が WouldDestroyLockedAxis を言及するが YAML 未登録 | Add. B | WouldOrphanPlacements/WouldConflict に変更 | ✅ 🔍C3 |
| `B-D2` | README に Addendum B の forward-reference | Add. B | Addendum B 追加で解消。「前方参照禁止」教訓 | ✅ 📐14 |
| `B-D3` | **Cross-grid swap が未定義** (UC-07) | Add. B / H.9 | UC-07 に BothPlacementsBelongToSameGrid + CrossGridSwapNotAllowed (三層: 21 / 30 §2.2 手順(ii) / AT-11) | ✅ 🔍C2 |
| `E-1` | 40-prompt の「六項目」typo | Add. D | 修正 | ✅ |
| `E-2` | UC-05 failure_reasons に InvalidCopyName 欠落 | Add. D | 追加 | ✅ 🔍C3 |
| `E-3` | Setter UC の no-op semantics 未定義 (Event 出すか) | Add. D | — | 🟡 (Dpc-2) |
| `E-4` | Storage-state invariants の精密化 (集合論的) | Add. D | — | 🟡 (Dpc-3) |
| `E-5` | decoder-error → InvalidImageData mapping 未定義 | Add. D | — | 🟡 (Dpc-4) |

## 2. Capability 間 / 横断の finding

| ID | 内容 | 由来 | 解決 | 現状 |
| --- | --- | --- | --- | --- |
| `E-comp` | 独立生成 2 実装が 6 カテゴリ規約衝突で compose 不可 | Add. E | Codebase Convention Contract (G-1) | ✅ 📐21 |
| `F-1` | 自己検証 VO のため宣言失敗理由が到達不能 (dead): InvalidOccupySize | Add. F | guaranteed_by 注記 + C1 が upstream ガードを動的検証 | ✅ 📐22 🔍C1 |
| `F-2` | UC-05 が構築例外を catch-all で InvalidCopyName に取り違え | Add. F | per-field を guaranteed_by 化 (UC produce は NotFound/InvalidCopyName のみ) | ✅ 📐22 🔍C1 |
| `G` | consumer Capability 追加時に producer へ projection retrofit が要る | Add. G | 契約に read ポートを **前倒し** (v0.3 C-CONSUMER-PORTS baseline_required) | ✅ 📐21 |
| `G-7` | RenderDescriptor が生 uuid.UUID を含み json.dumps 不可 | Add. G | C-IDENTITY-BOUNDARY (内部=UUID / 出力境界=str) | ✅ 📐21 |
| `I-C3a` | InvalidWeights.applies_to が UC-01 を誤って含む (latent drift) | Add. I | applies_to から UC-01 除外 | ✅ 🔍C3 |
| `I-C3b` | OutOfBounds.applies_to が UC-02 を誤って含む | Add. I | applies_to から UC-02 除外 | ✅ 🔍C3 |
| `I-C3c` | Conflict.applies_to が UC-02 を誤って含む | Add. I | applies_to から UC-02 除外 | ✅ 🔍C3 |

## 3. 方法論本体への昇格候補 (規範化の状況)

| ID | 内容 | 由来 | 現状 |
| --- | --- | --- | --- |
| `Cpc-1` | shared_concepts スキーマを BOM に追加 | Add. C | 🟡 (副候補 18。21 と対) |
| `Cpc-2` | Coordinator パターン (16) | Add. C | 🟡 (次フェーズ候補) |
| `Cpc-3` | Declaration-only Rules (17) | Add. C | 📐 部分 (R-08 を RENDERING が適用、20/30 に記載) |
| `Cpc-4` | ディレクトリ構造の Capability 別 subdir 対称化 | Add. C | 🟡 (GRID は直下のまま) |
| `Cpc-5` | Cross-Capability 存在性確認の命名規範 | Add. C | 📐 21 C-BOUNDARY-IFACE で一部 |
| `Dpc-1` | canonical_failure_reasons ↔ per-UC failure_reasons の machine-checkable 照合 | Add. D | ✅ 📐22 🔍C3 (**実装済み**) |
| `Dpc-2` | Setter UC の no-op semantics 規範 | Add. D | 🟡 |
| `Dpc-3` | Storage-state invariants の集合論的厳密化 | Add. D | 🟡 |
| `Dpc-4` | UseCase 層のエラー境界規範 (catch-and-translate vs propagate) | Add. D | 🟡 |
| `Dpc-5` | 改訂チェックリスト (forward-ref 禁止 / typo / Rule×UC matrix) | Add. D | 📐 部分 (14 + 22-C3) |

## 4. 確立した方法論パターン (実証済み)

| パターン | 文書 | 実証 |
| --- | --- | --- |
| 三層構造 (narrative + algorithmic + executable) | 11 | A-3 / R-08 tug を防御 |
| MUST_DECIDE_AND_DOCUMENT 第三カテゴリ | 12 | 累計 25 件を構造化 |
| 規範継承 + Inverse Audit Protocol | 13 | IMAGE_VARIANT v0.1 が GRID v0.2 並み品質 |
| 執筆者チェックリスト | 14 | 改訂事故の防止 |
| Codebase Convention Contract (横断規約) | 21 | n=2 アダプタ0 (F) / n=3 producer-free (H) |
| BOM↔実装 機械照合 (C1/C2/C3) | 22 | D-1/F-1/F-2/B-D3 を検出・解消 (I) |
| Authoring 層 (人間資料→意味設計コンパイラ→AI実装) + 分界点 | 23 | prototype 実測 (2026-05-30): AI=意図の不完全性 / 決定的ツール=内部整合 / provenance タグ=橋。AI が prose から旧 `A-3` を再発見。`experiments/authoring-compiler-prototype/` |

## 5. オープン項目の優先度 (次フェーズの入力)

| 項目 | 内容 | 推奨タイミング |
| --- | --- | --- |
| Coordinator パターン (Cpc-2) | cascade decision / cross-Capability orchestration を実コードで | 基盤固定後の「新規知見」フェーズ |
| 照合の汎用ハーネス化 | C1/C2 を全 UC へ自動展開 (BOM が trigger/anchored_by を宣言) | ゲート化の延長 |
| no-op / storage / error 境界 (E-3/E-4/E-5, Dpc-2/3/4) | IMAGE_VARIANT 系の残仕様穴 | IMAGE_VARIANT を再訪する実験時にまとめて |
| ディレクトリ対称化 (Cpc-4) | grid-composition/ subdir 化 | 低優先 (参照更新コスト) |
| 方法論本体への昇格 | 11-14/21/22 を 本体 01-10 (リポジトリ methodology/) へ | baseline 固定後 (本台帳 + 契約 v1.0 が前提) |

---

## 6. 関連

- 詳細: `90-feasibility-notes.md` Addendum A〜I
- 契約 baseline: `00-convention-contract.md` (v1.0)
- 機械照合: `../methodology/22-bom-conformance-check.md` + `../tools/bom-conformance-check/`
- 方法論拡張: `../methodology/` (11〜14 / 21 / 22)
