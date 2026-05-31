# 事前登録 (PRE-REGISTER) — unconfounded dry-run (operating-model-dryrun-3 / 通称 dry-run 4)

> **データを見る前に**書く。F-R2-D (decision taxonomy がセキュリティ判断=認可を被覆しない) の是非を、
> 執筆交絡を除いて検証する。dry-run 2/3 は単一執筆者 (Claude) が 7 種 taxonomy と本仮説を知った上で
> prose と答えを書いた → 認可が `domain_decision` に着地したのは AI の「最も近い種別」癖と
> 執筆者の authz 飽和の人工物かもしれない、という敵対的レビュー (harden-bodyside-design) の指摘への応答。

## 除く交絡
1. **執筆者の taxonomy 認識**: prose 執筆 agent に 7 種 decision kind・本研究・F-R2-D を**一切開示しない**。
2. **authz 飽和**: 執筆 agent は「認可を炙り出す」目的を持たない。普通の業務要求として自然に書く。ドメインも agent 自身が選ぶ。
3. **答えキー交絡**: 本ラウンドは seeded-holes 答えキーを作らない。観測は lift 後 BOM の `decision_ownership` を直接読むだけ。

## 手順
- Agent 1 (執筆、taxonomy 非開示): 複数ロールが協働する架空の社内ツールの要求 prose を自然文で書く。
- Agent 2 (抽出、独立): 更新版 extractor-spec で lift → BOM + 診断。
- 執筆者: lift 後 BOM の decision_ownership / preconditions を直接精査 (自己報告は裏取り)。

## 事前登録した falsifier / 判定規則 (データを見る前に固定)
観測対象 = prose に現れた**認可判断 (who-may: 誰が何をしてよいか)** が、lift 後 `decision_ownership` のどこに着地したか。

- **CONFOUND 確認 (= 新 kind 不要)**: 認可判断が**主に `domain_decision` に着地** (owner が付き、未確定時は provenance=proposal/unresolved で PROV が block) する場合。
  → 結論「`domain_decision` が who-may を吸収する。ギャップは『認可 kind の欠如』ではなく『domain_decision の過積載』→ 足すなら canonical kind でなく security/authz **タグ**」。**canonical 04 への kind 追加は却下/見送り**。
- **STRUCTURAL (= 新 kind 正当)**: 認可判断が**≥3 種に散る**、または**所有者なし (decision_ownership に entry 無し) で落ちる**のが多数の場合。
  → 結論「`authorization_decision` kind 追加が正当」。
- **disclosure (情報開示: 何を誰に見せるか) の着地**: 事前予想 = 所有者なし (真のギャップ)。再び `decision_ownership` に到達しなければ disclosure を一次ギャップと確認 (認可とは独立)。

## 注記
- n=1 の追加ラウンド。これ単独で canonical を動かすのでなく、dry-run 2/3 の交絡解釈を検証するのが目的。
- 認可が domain に着地 = dry-run 2/3 の「散る」解釈はやはり誤りで「domain 過積載」が正、を補強する向き。

---

## RESULT (データ取得後に追記、2026-05-31)

独立 extractor が lift した `OUTPUT-bom.yaml` の `decision_ownership` を執筆者が直接精査 + `checker.py --authoring` 実行 (BOM の `?` 2 箇所をサニタイズした `.clean.yaml` で。`?` は extractor の軽微な YAML スリップ)。

### 認可 (who-may) の着地 → **事前登録「CONFOUND 確認 (新 kind 不要)」分岐に合致**
- 総務専有操作 (R-03 登録/廃棄/台数変更) → **`domain_decision`** (human-confirmed)。`precondition_coverage:{ActorIsOfficeAdmin:[Forbidden]}` 宣言 → checker PASS。
- 承認要否 (R-01) → **`domain_decision`** (proposal、基準未確定)。
- 誰が承認可 (ActorMayApprove, UC-05/06) → `precondition_coverage` **未宣言** (prose 未確定) → `[PRECOND][INCONCLUSIVE]`/proposal-ERROR で human へ差し戻し (**所有者なしで沈黙せず**)。
→ 独立執筆でも認可は **一貫して `domain_decision`**。dry-run 2/3 と同じ。

### 情報開示 (disclosure) の着地 → 事前予想 (owner 無し) を**反証**
- 保有者可視範囲 (R-05「借りてる人が誰か」を本人/上長/総務に限定) → **`domain_decision` + `rendering_decision`** (proposal)。**owner 無しにならなかった**。
→ policy として framing されれば disclosure も domain に収まる。dry-run 2/3 の owner 無しは**執筆交絡の人工物**と確定。

### 結論 (D-taxonomy 決着)
**`authorization_decision`/`disclosure_decision` の canonical 追加は却下。7 種で足りる。** 残る実観測は `domain_decision` の過積載 → 必要なら canonical を動かさず **security/authz 監査タグ (非 canonical)** で対応。23 §5.2 NOTE を解決版へ更新。

### 副次の確証
- **D-deterministic (RULE F) を独立データで end-to-end 検証**: 確定認可 (ActorIsOfficeAdmin) は宣言 → PASS、未確定認可 (ActorMayApprove) は捏造せず未宣言 → INCONCLUSIVE。設計どおり。
- **`form` archetype 作動** (EquipmentRegisterForm → form、edit の偽 ERROR なし)。
- 軽微な finding: extractor が flow sequence に `?` を出しパース不能 → extractor-spec §8 自己チェックに YAML 妥当性項目を追加。状態 precondition (EquipmentAvailable 等) も `precondition_coverage` 宣言すれば INCONCLUSIVE を減らせる (任意)。
