# 22 — BOM ↔ Implementation Conformance Check (機械可読 照合)

> **Status: 方法論本体への昇格候補ドラフト**
> 由来: 残課題 F-1 / F-2 / D-1 / D-3 が「BOM の宣言 (canonical_failure_reasons・preconditions) と
> 実装が実際にすること」のズレに収束したことを受け、その **machine-checkable な照合** を規範化する。
> 実証: `experiments/bom-conformance-check/checker.py` + `90-feasibility-notes.md` Addendum I。

## この文書の目的

Phase 2 試行で繰り返し observed された残課題は、いずれも **「宣言された契約」と「実装の実体」の不一致**:

| 残課題 | 不一致の種類 | 由来 |
| --- | --- | --- |
| **D-1** | canonical_failure_reasons.applies_to と per-UC failure_reasons が食い違う | Addendum D |
| **F-1** | 宣言された失敗理由が、自己検証する値オブジェクトのため **到達不能 (dead)** | Addendum F |
| **F-2** | UC が宣言された失敗理由を **取り違える** (別 reason に潰す) | Addendum F |
| **D-3** | BOM が precondition を宣言しないため、実装が **強制しないまま再生成毎に再発** | Addendum B / G / H |

これらは人手レビュー (自己監査) では繰り返し見落とされ、**独立監査 (Codex / 別 AI)** が事後に捕捉してきた。
本文書は、これを **生成物に対して機械的に回せる照合** として定義する。

> **核心命題**: 「識別された spec gap は、サンプルを直すまで生成のたびに再発する」(Addendum H.9)。
> よって gap は **BOM 側を直し**、かつ **照合を CI 的に回して** 再発を防ぐ必要がある。

---

## 1. 三つの照合カテゴリ

### C3 — 静的: canonical_failure_reasons ↔ per-UC failure_reasons (D-1)

BOM (YAML) のみで完結する純粋な静的検査。実装不要。

- 各 UC の `failure_reasons` に挙がる reason は、すべて `canonical_failure_reasons` に定義されていること。
- 各 canonical reason の `applies_to` は、その reason を実際に挙げている UC 集合と **双方向に一致** すること。
  - `applies_to` にあるが UC が挙げていない → 過剰宣言。
  - UC が挙げているが `applies_to` にない → 宣言漏れ。

> **実証**: GRID v0.2 BOM に対し C3 が **3 件の latent drift** を検出した
> (`InvalidWeights.applies_to` に UC-01 / `OutOfBounds`・`Conflict.applies_to` に UC-02 が誤って含まれていた)。
> 誰も気づいていなかった D-1 級の不整合で、本照合で初めて顕在化した。

### C1 — 動的: 失敗理由カバレッジ (F-1 / F-2)

各 UC の宣言された failure_reason について、次のいずれかであることを検証する:

- **(a) UC-producible**: UC を呼ぶ何らかの入力で、その reason が実際に返る (probe で確認)。
- **(b) upstream-guaranteed**: `guaranteed_by` 注記があり、値オブジェクト/enum の構築時に保証される
  (= 無効値が UC に届く前に構築不能)。この場合、UC レベルの producibility は **免除** し、
  代わりに upstream ガードの存在を確認する。

どちらでもない reason は **FLAG**:
- UC が別 reason に潰している (例: `InvalidTransform` を渡したのに `InvalidCopyName` が返る) → **F-2**。
- 値オブジェクトが自己検証するため到達不能なのに `guaranteed_by` 注記がない → **F-1**。

> **F-1 / F-2 の規範的解決**: 自己検証する共有値オブジェクト (C-VALUE-SEMANTICS) を持つ Capability では、
> per-field の `Invalid*` 失敗理由は **upstream-guaranteed** であり、`guaranteed_by` を注記する。
> UC が直接 produce すべき失敗理由は、UC 固有のもの (NotFound / InvalidName 等) に限られる。

### C2 — 動的: precondition 強制 (D-3)

BOM が `preconditions` を宣言した UC について、その precondition に違反する入力を与え、
宣言された失敗理由で **拒否される** ことを検証する。

- BOM が precondition を **宣言していない** → 「その境界は UNSPECIFIED」と報告 (= まだ BOM 側の穴)。
- 宣言しているのに実装が強制しない → **FLAG** (= D-3 型の実装バグ)。

> **D-3 の規範的解決**: 「cross-grid swap 未定義」は、GRID UC-07 に precondition
> `BothPlacementsBelongToSameGrid` と失敗理由 `CrossGridSwapNotAllowed` を **BOM に追加** して閉じる。
> 以後 C2 が、precondition を強制しない実装を機械的に弾く。

---

## 2. `guaranteed_by` 注記 (C-VALUE-SEMANTICS との接続)

C1 の (b) を機械可読にするための、canonical_failure_reasons への注記:

```yaml
- name: InvalidTransform
  payload: { ... }
  applies_to: [UC-05, UC-09]
  guaranteed_by: "ImageTransform construction (R-09, C-VALUE-SEMANTICS) — 無効値は upstream で構築不能"
```

- `guaranteed_by` がある reason は、UC レベルの coverage を免除する **代わりに、指す値オブジェクト/enum を
  無効入力で実際に構築し「reject する」ことを動的検証** する (本プロトタイプで実装済み)。
  - 例: `OccupySize(0,0)` → ValueError / `ScalingMode("__invalid__")` → ValueError /
    `ImageTransform(rotation="__invalid__")` → TypeError。
  - **ガードが reject しなければ FLAG**。これにより「注記を足すだけで照合を pass させる」抜け道を塞ぐ
    (= 免除は upstream ガードの実在を証明できる場合に限る)。

これは 21-codebase-convention-contract.md の **C-VALUE-SEMANTICS / C-IDENTITY-BOUNDARY** と同じ系譜:
**physical 契約 (値オブジェクトの自己検証・identity の内部/境界表現) が semantic カタログ
(失敗理由) の到達可能性を左右する** ため、両者を照合する層が要る。

---

## 3. 照合の before / after (実証: Addendum I)

`experiments/bom-conformance-check/checker.py` を GRID/IMGVAR/RENDERING の BOM と
Phase 2 実装 (`phase2-cocompose-impl`) に対して実行した結果:

| | BEFORE (BOM 未修正) | AFTER (BOM 修正後) |
| --- | --- | --- |
| C3 | GRID で 3 件の applies_to drift を FLAG | 全 BOM consistent |
| C1 (UC-05) | InvalidTransform/ScalingMode/Alignment が `InvalidCopyName` に潰れる (F-2) を 3 件 FLAG | `guaranteed_by` + **upstream ガードの動的検証** で OK (F-1/F-2 解消) |
| C2 (UC-07) | BOM が precondition 未宣言 → UNSPECIFIED | precondition 宣言後、凍結実装が強制しない D-3 バグを FLAG |
| FLAGS 合計 | **6** | **1** (= C2 が impl の D-3 バグを正しく検出) |

最後に残る 1 FLAG は **意図通り**: BOM が precondition を宣言したことで、
照合が「強制しない実装」を機械的に弾けるようになった (再発防止が効く)。

---

## 4. 運用規範

- **新規 BOM 執筆時**: C3 を必ず通す (canonical_failure_reasons と per-UC failure_reasons を一致させる)。
- **Phase 2 生成後 / Phase 3 監査**: C1 / C2 を実装に対して回す。FLAG は (i) BOM の穴か (ii) 実装のバグ。
- **値オブジェクトが自己検証する失敗理由**: `guaranteed_by` を注記し、UC produce を要求しない。
- **precondition を宣言したら**: 対応失敗理由を canonical に追加し、C2 で強制を検証する。
- **14-author-checklist.md に 1 項目追加**: 「BOM ↔ 実装 照合 (C3/C1/C2) を回したか」。

### 4.1 生成受け入れゲート (shift-left。手戻りを構造的に断つ)

照合を **生成の後段の事後監査** ではなく **生成の受け入れ条件 (acceptance gate)** に前倒しする:

- **規範**: Phase 2 生成は、`checker.py` が **GATE: PASS (exit 0)** になるまで「完了」と見なさない。
  生成プロンプト (40 系) の POST_IMPLEMENTATION_SELF_AUDIT に「照合を回し GATE PASS を確認」を含める。
- **対象の指定 (重要)**: ゲートは **生成物の src を CLI で明示** して回す:
  `python experiments/bom-conformance-check/checker.py <生成物>/src`。
  引数なしの既定ターゲットは **デモ用の凍結 impl** (`phase2-cocompose-impl`、B-D3 を持つため意図的に FAIL) であり、
  これを新生成物のゲートに使ってはならない (古い凍結ツリーを検査してしまう)。
  BOM は常に正準の `docs/capability-bom-sample/` を参照する。
- これにより F-2 / B-D3 級の drift を **コミット前** に潰し、「事後発見 → 凍結/修正」の手戻りループを断つ。
- **GATE 判定**: 概念は 3 種 — **FLAG** (宣言と実装の不一致) / **INCONCLUSIVE** (検証不能。probe 未定義・ガード未定義・対象 UC 不在) / **[C3]** (BOM drift)。
  いずれか 1 件でも残れば **GATE: FAIL + 非ゼロ終了**。**「検証不能」を OK 扱いして PASS させない** (検証不能 ≠ 検証 OK)。
- **coverage manifest**: **(UC, failure_reason) ペア単位** で `guaranteed_by` / dynamically-probed /
  unverified-by-tool に分類して出力。共有失敗理由 (例: NotFound は多数 UC で使用) を probe した UC のみ
  「probed」と数え、他 UC での使用は unverified とする (reason 単位だと coverage を過大表示する)。
  **未検証 (動的 probe 未整備) を可視化**し「pass=全部 OK」の誤読を防ぐ。現状の動的 probe は focused
  (IMGVAR UC-05 / GRID UC-07) なので大半が unverified = C3 静的整合 + 人手 anchor test で担保。
  未検証項目は `../capability-bom-sample/91-findings-ledger.md` で追跡する。
- **既知の限界**: 現状 C1/C2 の動的 probe は GRID UC-07 / IMGVAR UC-05 に focused。
  汎用化 (BOM が trigger / anchored_by を宣言し全 UC を自動 probe) は次フェーズ。
  それまで unverified 項目は C3 静的整合 + 人手 anchor test で担保する。

---

## 5. 既存方法論本体・拡張との接続

| 文書 | 接続 |
| --- | --- |
| 09-ai-audit-prompt-guide.md | 監査 (Phase 3) の機械化。FLAG を観測項目に |
| 11-three-layer-disambiguation.md | C1/C2 は executable 層の自動化。narrative/algorithmic と整合を照合 |
| 12-must-decide-and-document.md | `guaranteed_by` か否かは「UC が決めるか上流が保証するか」の決定の明示 |
| 21-codebase-convention-contract.md | C-VALUE-SEMANTICS / C-IDENTITY-BOUNDARY が C1 の前提。physical↔semantic 照合 |
| 14-author-checklist.md | 照合実行をチェック項目に追加 |

---

## 6. 採用判定

| 評価軸 | 結果 |
| --- | --- |
| 実証根拠 | `checker.py` が C3 で 3 件の latent drift、C1 で F-2、C2 で D-3 を実際に検出 (Addendum I) |
| 適用コスト | 低〜中 (C3 は純 YAML で即可。C1/C2 は impl 構築の薄いハーネスが要る) |
| 既存方法論との整合 | 補完 (監査の機械化)。21 とセットで physical↔semantic を閉じる |
| CI 連携 | flag が残れば **非ゼロ終了** (CI ガードとして機能。凍結 impl の D-3 で現状 exit 1) |
| 残課題 | C1・C2 の汎用ハーネス化 (現状は GRID UC-07 / IMGVAR UC-05 に focused な prototype) |

---

## 7. 関連ドキュメント

- 実証根拠: `docs/capability-bom-sample/90-feasibility-notes.md` Addendum I
- 照合ツール: `experiments/bom-conformance-check/checker.py`
- 契約: `docs/capability-bom-sample/00-convention-contract.md` (C-VALUE-SEMANTICS / C-IDENTITY-BOUNDARY)
- 修正されたサンプル: GRID `21-grid-composition.yaml` (D-3 + C3 drift), IMGVAR `21-image-variant-management.yaml` (guaranteed_by)
- 残課題の由来: Addendum B (D-3) / D (D-1) / F (F-1/F-2) / G / H
