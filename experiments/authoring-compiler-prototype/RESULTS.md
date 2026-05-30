# 意味設計コンパイラ prototype — 分界点の実測結果

> 実施日: 2026-05-30 / 方法論: `docs/capability-bom-audit/methodology/23-authoring-and-operating-model.md` §3.6 (実装形態 b) の実証。
> 命題: **(b) 決定的ツール + AI ハイブリッド** で、人間 prose の欠落を authoring 時に弾けるか。
> 決定的ツールと AI のどちらが何を捕捉するか (= 分界点) を実コードで測る。

## 1. 方法

| 段 | 担当 | 内容 |
| --- | --- | --- |
| 入力 | 人間 (本実験では執筆者) | `INPUT-human-requirements.md` — 縮小版 GRID の **人間向け prose**。既知の穴を意図的に仕込む (`SEEDED-HOLES.md`、AI には非開示) |
| AI 抽出器 | **独立 subagent** | prose **1 ファイルのみ**から BOM へ lift + provenance/source タグ + 診断 (proposal-ERROR/WARNING/INFO)。既存サンプル・方法論・seeded-holes は参照禁止 |
| 決定的検査器 | `checker.py --authoring` | lift 済み BOM YAML に static 検査 (SCHEMA / C3 / PRECOND / REF / PROV)。実コード不要 |

成果物: `INPUT-human-requirements.md` / `OUTPUT-bom-candidate.yaml` / `OUTPUT-diagnostics.md` / `SEEDED-HOLES.md` (答え) / 本書。

## 2. 生実測値

**AI 抽出器** (独立、穴を知らない): 6 UC / 8 rule / 8 failure_reason を lift。診断 **proposal-ERROR 12 / WARNING 13 / INFO 4**。

**決定的検査器** (`--authoring OUTPUT-bom-candidate.yaml`): GATE FAIL (exit 1)。

| 検査 | 結果 |
| --- | --- |
| SCHEMA (必須セクション/フィールド) | **OK** |
| C3 (canonical ↔ per-UC failure_reasons) | **OK** (AI が内部整合した BOM を lift したため drift 0) |
| PRECOND (前提条件の失敗理由被覆) | **9 ERROR** (すべて `GridExists`/`PlacementExists` の **命名相違**) + **9 INCONCLUSIVE** (規約マップ外の前提) |
| REF (applies_to の dangling) | **OK** |
| PROV (provenance ゲート) | **8 ERROR** (`unresolved`/`proposal`) + **5 WARNING** (`inferred`) |

参考: 修正済み正準 GRID BOM (`samples/grid-composition/21-*.yaml`) に同じ authoring 検査 → **GATE PASS (exit 0)**。prototype が良い BOM と穴あき BOM を区別できる傍証 (PROV は provenance タグの無い正準 BOM では無音 = 後方互換)。

## 3. 仕込んだ穴 × 捕捉者 (分界点の中核)

| 穴 | ledger | AI 抽出器 | 決定的ツール | 判定 |
| --- | --- | --- | --- | --- |
| **H1** NotFound 失敗欠落 | A-1 | **捕捉** FAIL-002 (proposal-ERROR)。GridNotFound/PlacementNotFound を `inferred` で補完 | **発火するが別根拠**: PRECOND ERROR は「命名が canonical `NotFound` でない」を指摘 (= 規約相違)。PROV は inferred を WARNING | 両層が同領域を別機構で捕捉 |
| **H2** cross-grid swap 未定義 | B-D3 | **部分**: SWAP-003 (WARNING、過小評価) + swap を単一 gridId と `inferred` して暗黙解消 | **構造的には不可** (同一grid前提を lift せず)。ただし関連する SWAP-002 を R-05=`unresolved`→**PROV ERROR で block** | AI 検出 (過小評価) / 橋で block |
| **H3** はみ出し命名ゆれ | D-1 系 | **正規化** (3 表現を `OutOfBounds` に統一、明示 finding なし) | **不可** (正規化で drift 消滅、C3 clean) | conditional → AI 正規化 → 決定的は無音 |
| **H4** 重なり定義の曖昧さ | A-3 | **捕捉** BOUND-002 (WARNING) | 不可 (意味) | **AI のみ** |
| **H5** 順番値 未定義 | A-2/MD | **捕捉** ORDER-002 (proposal-ERROR) +001/003 | 不可 (意味) | **AI のみ** |
| **H6** Decision ownership 皆無 | 14 | **捕捉** DEC-001/002 (proposal-ERROR)。owned_by:[] + `unresolved` で記述 | **SCHEMA は見逃し** (セクションは非空) / **PROV ERROR で捕捉** (5 decision が `unresolved`) | AI 検出+タグ → 橋で block |
| **H7** UI フィードバック欠落 | §4 | **捕捉** UI-001/002 (WARNING) + FAIL-004 | 不可 (UI ルール未実装) | **AI のみ** |

**想定外の良い発見 (未仕込み)**: AI が **SWAP-002** = 「Move は自己除外を明記するのに Swap は非対称」を proposal-ERROR で検出。これは旧 ledger の **A-3 (Swap 自身排除セマンティクス、実バグ)** を prose から独立に再発見したもの。SWAP-001 (占有サイズ違い swap)、SIZE-001、FAIL-001/003/004/005、DEC-003/006/007、MD-001、EVT-001、AC-001 も emergent。

## 4. 分界点 (実測結論)

> **検出は二分される。AI = 意味/意図の不完全性、決定的ツール = 構文/内部整合。
> しかし enforcement は provenance タグで決定的ツール側に一本化できる。**

1. **意図の不完全性 (H2/H4/H5/H7)** は構造に現れない (内部整合な BOM は「未完成」でも構文的に正しい) ため、**原理的に AI しか検出できない**。仮説どおり。
2. **内部不整合/規約/相互参照** は決定的ツールの領分。ただし本実験では AI が良く lift したため C3/SCHEMA/REF は clean。決定的 ERROR は **PRECOND の命名相違のみ** = 「構文・規約の準拠」を見ている (意図の欠落ではない)。
3. **橋 (PROV)**: AI が意味的ギャップを検出して `unresolved`/`proposal` と **タグ付け**すれば、決定的ツールは **タグを機械的に enforce** するだけで H6 や SWAP-002 を block できる。**ツールはギャップを理解しなくてよい**。→ §3.6「ブロックする ERROR は再現可能な決定的ツールが出す」を保ったまま、意味的ギャップも block する。

```
AI:   prose を読む → 意味的ギャップを検出 → provenance タグ付け (proposal/unresolved)
決定的ツール: 構造ルール (SCHEMA/C3/PRECOND/REF) + provenance タグの機械的 enforcement (PROV)
           → どちらも再現可能。AI は enforcement しない (検出+タグのみ)
```

## 5. calibration 発見 (prototype が炙り出した境界の歪み)

| ID | 発見 | 含意 / 対処案 |
| --- | --- | --- |
| **C-1** | PRECOND が `GridExists`→`GridNotFound` を「命名相違」で 9 件 ERROR 化。意味的には被覆済みなのに canonical 名 `NotFound` でないため発火 | 決定的 precond ルールは **構文 (命名規約)** を見ている。規約 registry を family-aware (`GridNotFound ∈ NotFound 系) にするか、**AI 抽出器が canonical 名へ正規化**してから決定的パスに渡す。**分界点 = 規約マップの被覆 + AI が lift した構造**、の実証 |
| **C-2** | SCHEMA が H6 を見逃した (decision_ownership セクションは非空。owned_by が空でも通る) | 構造の **存在** チェックだけでは不十分。**PROV ゲート (provenance=unresolved を block) が補完**。「セクションがある」≠「中身が確定」 |
| **C-3 (核心)** | **provenance が分界点を渡る橋**。AI のタグを決定的ツールが enforce すれば意味的 ERROR も再現可能に block | §3.6/§3.7 の経験的 sharpen: 「AI=検出+タグ / ツール=タグの enforcement」。proposal-ERROR (§3.7) は **provenance=proposal を PROV が機械 block** することで実装される |

## 6. 方法論 23 への含意 (反映済み)

- §3.6 の責務分界に **PROV (provenance ゲート)** を「決定的検査器が担う 4 つ目の static 検査」として追記。
- §3.7 proposal-ERROR は「AI が provenance=proposal でタグ → 決定的ツールが PROV で block」という **二者の協調**として具体化 (AI 単独でブロックしない = §3.6 と整合)。
- 命名正規化 (C-1) を AI 抽出器の責務に追加 (canonical failure reason 名へ寄せる)。

## 7. 独立検証 (執筆者)

- subagent 報告を鵜呑みにせず、`OUTPUT-bom-candidate.yaml` / `OUTPUT-diagnostics.md` を自分で精査。
- `checker.py --authoring` を **3 回**自分で実行: (a) 穴あき BOM → FAIL (PRECOND 9 ERROR / PROV 8 ERROR)、(b) 正準 GRID BOM → PASS、(c) 故意に壊した最小 BOM → SCHEMA/C3/PRECOND/REF が各々発火することを smoke test で確認。
- subagent は禁止パス (docs/ samples / SEEDED-HOLES) を開いていない (入力は prose 1 ファイルのみと報告、独立性を担保)。

## 8. 結論

**(b) 決定的ツール + AI ハイブリッドは authoring 時に機能する。**

- AI 抽出器は 7 仕込み穴すべてを検出し、旧 ledger の実バグ (A-3 相当) を prose から再発見した。
- 分界点は明確: **意図の不完全性は AI、構文/内部整合は決定的ツール**。
- **provenance タグが両者を繋ぐ**: 決定的ツールは AI のタグを機械 enforce することで、意味的ギャップを「理解せずに」再現可能に block できる。これが §3.6「ブロックは決定的ツール」と §3.7「AI は所有しない」を同時に満たす実装機構である。

残課題: 命名正規化 (C-1) の AI 責務化 / precond registry の family 化 / UI 意味契約ルールの決定的化 (H7 を構造で拾えるか)。

---

## 9. Step 1 spec の re-run 検証 (v1、2026-05-30)

Step 1 で確定した抽出器 spec (`../../docs/capability-bom-audit/tools/authoring-compiler/extractor-spec.md`、RULE A/B/C 入り) を
**別の独立 subagent** に渡し、**同じ prose** を lift させて `OUTPUT-bom-candidate-v1.yaml` / `OUTPUT-diagnostics-v1.md` を生成
(v0 の凍結出力は保持)。決定的検査器を回した結果。

### v0 (prototype) → v1 (spec 準拠) の比較

| 観点 | v0 prototype | v1 spec 準拠 | 検証 |
| --- | --- | --- | --- |
| 失敗理由名 | `GridNotFound`/`PlacementNotFound` (capability 接頭辞) | `NotFound` + `entity_kind` payload (canonical) | **RULE A 効果** |
| **PRECOND** | **9 ERROR (命名相違) + 9 INCONCLUSIVE** | **OK (0/0)** | **命名ノイズ完全消滅** |
| SCHEMA / C3 / REF | OK | OK | 同等 |
| **PROV (blocking ERROR)** | 8 (うち decision×5 等)。診断と一部不整合 (`GridNotFound` が inferred なのに診断は proposal-ERROR) | **12 ERROR、すべて AI の proposal-ERROR/unresolved 項目と一致** | **RULE C 効果** |
| ゲート FAIL の中身 | 命名ノイズ + 意味的ギャップが混在 | **純粋に意味的ギャップ 12 件のみ** (UC-05 z順/UnknownCopyId/InvalidIndex/R-04 swap自己除外/R-06 z順不変条件/R-07 占有サイズ/workflow・ui・persistence・rendering・history_decision/LayoutVisualStyling) | shift-left の信号純度向上 |
| INCONCLUSIVE | 9 (規約マップ外の前提) | **0** | 抽出器が存在前提のみを precondition 化し、検証条件は Rule へ (分界点が明瞭化) |

### 検証された 3 点

1. **RULE A (正規化)**: prototype の「PRECOND 9 命名 ERROR」が **0 に消えた**。決定的 registry を canonical 名のみに保ったまま、抽出器が canonical 名へ寄せることで命名相違の偽陽性が発生源で消滅。
2. **RULE C (provenance↔診断 結合)**: 決定的ツールの PROV ブロック 12 件が、AI の proposal-ERROR/unresolved 診断と **過不足なく一致**。prototype の「inferred なのに診断 ERROR」食い違いが解消。`inferred` 項目 (NotFound/VO/events 等) は WARNING (非ブロック) に正しく落ちた。
3. **RULE B (severity)**: 操作の成否・不変条件・所有権を左右する未定義 (swap 自己除外 R-04 / z順不変条件 R-06 / 占有サイズ R-07 / decision 5 種 / グリッド跨ぎ) がすべて proposal-ERROR/unresolved で blocking に乗った。

### 結論

**ゲートは「正しい理由だけ」で FAIL するようになった** — 命名ノイズ 0・unverifiable 0、ブロックは AI がタグ付けした 12 の真の意味的ギャップのみ。
Step 1 spec は実コードで実証された。**「検出は AI / enforcement は決定的ツール、橋は provenance」**(§3.7) が spec 準拠運用で安定動作する。
未検証の基盤の上に積まない、という原則どおり Step 1 を実証で閉じてから Step 2 (決定的ルール整理) / Step 3 (UI) へ進める。
