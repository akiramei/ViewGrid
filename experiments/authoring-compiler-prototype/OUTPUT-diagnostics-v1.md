# 診断レポート — 画像配置グリッド (AI 抽出器 v1)

> 入力 prose: `experiments/authoring-compiler-prototype/INPUT-human-requirements.md`
> 対応 BOM 候補: `OUTPUT-bom-candidate-v1.yaml`
> severity と BOM の `provenance` は RULE C で一致させてある (各診断に `bom_ref`)。

---

## 失敗理由 (FAIL-)

### FAIL-001
- severity: WARNING
- message: 不在系の失敗理由 (グリッド不在・配置不在) を prose は名前で示していない。NotFound + payload{entity_kind, entity_id} に正規化したが、これは表現上の選択。
- source: UC-02〜06 の precondition GridExists/PlacementExists の破れ。prose は失敗理由名を一切書いていない。
- bom_ref: canonical_failure_reasons.NotFound (provenance: inferred)
- resolution: NotFound への一本化(entity_kind で区別)で良いか、Grid と Placement で別失敗理由が要るかを確認。

### FAIL-002
- severity: proposal-ERROR
- message: 「使える画像」が存在しない/無効な copy_id を指定した場合の失敗が未定義。画像は別仕組みの派生物=外部参照のため、内部実体の NotFound とは意味が異なる(UnknownCopyId 案)。
- source: 背景『別の仕組みで作られた画像派生物』+ 画像をマスに置く『使える画像を1つ選び』。失敗時の扱いが prose に無い。
- bom_ref: canonical_failure_reasons.UnknownCopyId (provenance: proposal) / UC-02
- resolution: 外部画像参照の不在を UnknownCopyId として独立に扱うか、NotFound に含めるかを人間が決定。

### FAIL-003
- severity: proposal-ERROR
- message: 占有サイズ (幅×高さ) の妥当範囲が未定義。0 や負、グリッドより大きい寸法を受け付けるかが配置の成否を直接左右する。
- source: 画像をマスに置く『占有する大きさ(幅×高さ、マス単位)を指定して置く』。下限・正値要件の記述なし。
- bom_ref: canonical_failure_reasons.InvalidDimensions (provenance: inferred) / R-07 OccupySizeAtLeastOne (provenance: proposal) / UC-02
- resolution: occupy_size の最小値(>=1)と、グリッド寸法を超える指定の扱い(OutOfBounds か InvalidDimensions か)を決定。

### FAIL-004
- severity: proposal-ERROR
- message: 重なり順操作の「端」での挙動が未定義。既に最前面の配置に「1つ前へ」、最背面に「1つ後ろへ」を適用した時、失敗かノーオペか未確定。
- source: 重なり順を変える『1つ前へ/1つ後ろへ』。端での結果が prose に無い。
- bom_ref: canonical_failure_reasons.InvalidIndex (provenance: proposal) / UC-05
- resolution: 端操作を no-op(成功)とするか InvalidIndex で失敗させるかを決定。

---

## precondition (PRE-)

### PRE-001
- severity: WARNING
- message: 配置 (Placement) を一意に特定する識別子の存在が prose に明示されていない。動かす/入れ替える/消す対象を指すには id が機械的に必要。
- source: 配置を動かす/入れ替える/消す は「どの配置か」を指す手段を述べていない。
- bom_ref: entities.owned.Placement / UC-03,04,06 inputs.placement_id
- resolution: Placement に id を持たせる前提で良いか確認 (表現上の選択)。

---

## Rule / 不変条件 (RUL-)

### RUL-001
- severity: proposal-ERROR
- message: Swap で「入れ替える2つの配置を互いの重なり判定から除外するか」が未定義。Move は『自分自身は重なり判定から除く』と明示するが、Swap には対応する規律が無い。これを除外しないと、隣接/重複領域の正当な入れ替えが Conflict で常に失敗しうる。
- source: 配置を動かす『動かしている自分自身は重なり判定から除く』 vs 2つの配置を入れ替える『他と重ならないこと』(自己/相手除外の言及なし)。
- bom_ref: R-04 SwapExcludesSwappedPairFromOverlap (provenance: proposal) / UC-04
- resolution: 入れ替え2者を互いの重なり判定から除外するか(Move の自己除外との一貫性)を決定。

### RUL-002
- severity: proposal-ERROR
- message: 「順番を表す値」(z-order) の不変条件が未定義。全順序か、一意か、連続(0,1,2..)か、重複を許すか、新規配置の初期順序値が何かが定まらず、最前面/最背面の意味が確定しない。
- source: 重なり順を変える『各配置には順番を表す値が付く』。値の制約・初期割当の記述なし。
- bom_ref: R-06 ZOrderTotalAndContiguous (provenance: proposal) / UC-05
- resolution: z-order を一意・連続の全順序とするか等の不変条件と、PlaceImage 時の初期 z 値を決定。

### RUL-003
- severity: WARNING
- message: グリッド寸法 (rows/cols) は『1 以上の整数』と下限のみ明示。上限(過大な行列数)や入力検証層が未定義。
- source: グリッドを作る『行数・列数は 1 以上の整数』。上限なし。
- bom_ref: R-05 GridDimensionsAtLeastOne (provenance: human-confirmed, 下限) / decision_ownership.validation_decision
- resolution: 上限の要否、検証を domain と validation のどちらが所有するかを確認。

---

## Decision ownership (DEC-)

### DEC-001
- severity: proposal-ERROR
- message: 「異なるグリッド間の配置入れ替え/移動を許すか」が未定義。許否で操作の成否が変わり、Capability 境界にも影響する。本候補は同一 grid_id 前提で lift したが prose に根拠が無い。
- source: 2つの配置を入れ替える/配置を動かす は対象配置が同一グリッドかを述べていない。典型シナリオは単一グリッド内のみ。
- bom_ref: UC-03, UC-04 (inputs に grid_id を単一で置いた前提) / decision_ownership.workflow_decision
- resolution: 入れ替え・移動を同一グリッド内に限定するか、グリッド跨ぎを許すかを決定。

### DEC-002
- severity: proposal-ERROR
- message: 重なり順 (z-order) の決定が「描画(rendering)」の所有か「ドメイン状態」の所有か未確定。『視覚的に前後する』は描画概念だが『順番を表す値が付く』はドメイン状態。所有が割れると重複実装/責務漏れになる。
- source: 重なり順を変える『複数の配置が視覚的に前後する』+『各配置には順番を表す値が付く』。
- bom_ref: decision_ownership.rendering_decision (provenance: proposal) / UC-05
- resolution: z 値の保持・変更を domain が所有し、実際の描画前後関係を rendering(別レイヤ/別 Capability)が所有する、という分界を確認。

### DEC-003
- severity: proposal-ERROR
- message: グリッド/配置の永続化(保存・再読込)の所有が prose に一切無い。CreateGrid 後の状態がどこに残るか、誰が所有するかが不明で、操作の前提(GridExists の持続)が宙に浮く。
- source: prose 全体に保存・永続化・データストアの記述なし。
- bom_ref: decision_ownership.persistence_decision (provenance: unresolved)
- resolution: 永続化の要否と所有者(本 Capability か外部)を人間が定義。案も出せないため unresolved。

### DEC-004
- severity: proposal-ERROR
- message: 『動かしたり入れ替えたり』を繰り返す編集に undo/履歴 (history) が要るか未定義。要るなら各 UC が履歴に与える効果が決定の所在になる。
- source: 典型シナリオ『気に入る配置になるまで動かしたり入れ替えたりする』。
- bom_ref: decision_ownership.history_decision (provenance: unresolved)
- resolution: undo/履歴の要否を人間が決定。要否不明のため unresolved。

---

## UI 意味契約 (UI-)

### UI-001
- severity: proposal-ERROR
- message: 配置画面のドラッグ操作 (画像をマスへドラッグ/置いたものをドラッグで移動) の意味契約をこの Capability が所有するか、別 UI レイヤが所有するか未確定。ドラッグ確定時にどの UC(PlaceImage/MovePlacement)へ写すか、ドロップ無効時のフィードバックも未定義。
- source: 使う人と典型シナリオ『画像をマスにドラッグして置く。置いたものはドラッグで動かせる。レイアウト(見た目)の細部はデザイナーに任せる』。
- bom_ref: ui_contracts (空) / decision_ownership.ui_interaction_decision (provenance: proposal)
- resolution: ドラッグ→UC の binding と無効ドロップ時 feedback の所有者(本 Capability か UI レイヤ/デザイナー)を決定。

---

## 境界 / 共有概念 (BND-)

### BND-001
- severity: WARNING
- message: 依存先「画像派生物を作る別仕組み」の正式な Capability 名/参照キーが prose に無い。ImageCopy / ImageDerivation は推定名。
- source: 背景『別の仕組みで作られた画像派生物(トリミングや拡縮の済んだもの)』。
- bom_ref: entities.referenced.ImageCopy (inferred) / boundaries.depends_on.ImageDerivation (inferred)
- resolution: 依存先 Capability の正式名と参照識別子 (copy_id 等) を確認。

### BND-002
- severity: proposal-ERROR
- message: 「レイアウト(見た目)の細部はデザイナーに任せる」がこの Capability の境界外 (excluded) なのか、単に未着手なのかが未確定。境界の引き方で z-order/rendering の責務帰属が変わる。
- source: 典型シナリオ『レイアウト(見た目)の細部はデザイナーに任せる』。
- bom_ref: boundaries.excluded.LayoutVisualStyling (provenance: proposal)
- resolution: 視覚スタイリングを明示的に excluded とするか、別 Capability として境界を引くかを決定。

---

## 受け入れ条件のテスト可能性 (AC-)

### AC-001
- severity: WARNING
- message: 受け入れ条件『不正な配置(はみ出し・重なり)は受け付けない』はテスト可能だが、上で未定義の項目(占有サイズ下限・swap 自己除外・z-order 不変条件・外部画像不在)に対する受け入れ基準が無く、これらを満たさない実装も「受け入れ条件は満たす」と通ってしまう。
- source: 受け入れ条件『上記シナリオ1〜5が一通り行える。不正な配置(はみ出し・重なり)は受け付けない』。
- bom_ref: UC-02,03,04,05 / FAIL-002,003,004 / RUL-001,002
- resolution: 上記 proposal-ERROR を解消した後、各失敗条件に対する受け入れシナリオ(負ケース)を追記。

---

## events (EVT-)

### EVT-001
- severity: WARNING
- message: 各操作が観測可能イベントを発するか、payload に何を載せるかが prose に無い。本候補はイベント化を inferred で補ったが、観測可能性の要否自体が未確定。
- source: prose にイベント/通知/監査の記述なし。
- bom_ref: events.* (すべて inferred)
- resolution: イベント発行の要否と payload を確認 (不要なら events を削除)。
