# 本体側 finding F-R2-B / F-R2-D の設計空間 (dry-run 3 で確定)

> dry-run 3 (`operating-model-dryrun-2/`、認可/共有ドメイン) のデータ確定後に最終化する設計メモ。
> 現時点 (subagent 実行中) の設計空間 + 変更波及範囲 (blast radius)。

## F-R2-D — decision taxonomy がセキュリティ判断を被覆しない

### 証拠 (現時点)
- 04 の 7 種 (domain/validation/workflow/persistence/ui_interaction/rendering/history) は **画像/エディタ偏重** (placement/fork/trim/undo)。認可 (who-may) / 情報開示 (what-to-reveal) を表す第一級 kind が無い。
- dry-run 2: 情報開示判断が `decision_ownership` に **owner 無しで落ちた** (FAIL/UI 診断にのみ出現)。認可は validation_decision + Rule に散った。
- blast radius: 7 種リストは 13 ファイルで参照。だが **8 つ目の追加は additive (既存不変・後方互換)**。必須改変 = 04 (定義) + 02 (一覧) + extractor-spec §2 コメント + 09 (audit prompt)。sample BOM (画像) は authz 判断を持たず変更不要。
- 「authorization/認可/security」語彙は本体 01-10 に**完全に不在** (23 のみ) → ギャップは真の欠落。

### 設計オプション
- **D-α (新種別 `authorization_decision` 追加)**: who-may (操作・閲覧の許可) を第一級 decision に。情報開示 (what-to-reveal-to-whom) も「情報に対する認可」として同種別に収める。利点: 認可 ownership を専用に追跡=overreach 監査の高価値レンズ (例: UI 層が認可判断を所有=典型 overreach)。taxonomy の目的「意味判断の所在を追跡可能に」と一致。コスト: 低 (additive)。
- **D-β (`authorization_decision` + `disclosure_decision` の 2 種)**: 認可と情報開示を分離。利点: 粒度。コスト: 2 種追加は過剰か (情報開示 ⊂ 認可 と見なせる)。
- **D-γ (種別追加せず domain_decision に集約 + security タグ)**: churn 0。欠点: dry-run 2 で AI が domain に素直に入れられず散った=認知的に非自明。専用監査レンズを失う。taxonomy の存在意義 (kind 分離) に反する。
- 資格情報保存 (ハッシュ/トークン保管) は `persistence_decision` で素直に収まる (dry-run 2 で実証) → **taxonomy ギャップでない**。認可/開示と切り分ける。

### 暫定推奨 (dry-run 3 で再現確認後に確定)
**D-α** (単一 `authorization_decision`、情報開示を内包)。additive で低 churn、監査レンズの価値が高い。dry-run 3 で認可/開示が再び散る/owner 無しなら構造的と確定し、04/02/extractor-spec/09 へ additive に反映。命名 (`authorization_decision` vs `security_decision`) は人間確認点。

## F-R2-B — UI archetype library のギャップ

### 証拠 (現時点)
- library 5 種 (login/search/edit/list/confirm)。dry-run 2: パスワード設定画面に `edit` を当てて load/discard/unsaved_warning が不適合 (B1)、「保存ボタン」を `submit` と lift して `edit.save` 不一致 (B2)。
- dry-run 3 で detail/share/settings 画面を投入 → library 被覆の穴を追加収集予定。

### 設計オプション
- **B1 (set 系 archetype 追加)**: `form`/`set` archetype (新値を set するフォーム、load 不要)。必須 = primary-action + (任意 cancel) / feedback = validation_error。`edit` は「既存値 load→編集→save」に限定。
- **B2 (主操作ロール正規化)**: 確定操作ロールを canonical 化。案 (i) 全 archetype で `submit` を主操作とし統一 / 案 (ii) RULE E に正規化表 (保存/送信/ログイン/確定/OK/削除 → primary role)。extractor-spec に既に部分対応を入れた。
- **B3 (detail/view archetype)**: 表示専用画面。必須 interaction = ほぼ無し (display)。library に無いと頻出画面が全部 INCONCLUSIVE になる。
- **B4 (dialog/share 系)**: ダイアログは confirm の拡張で表現? or 専用。

### 暫定方針 (dry-run 3 で確定)
checker.py `UI_ARCHETYPES` に **set/form** と **view/detail** を追加候補。主操作ロールは案(ii) 正規化表を RULE E に。実装したら回帰 (good GRID/dry-run/cross-cap) + dry-run 2/3 の mis-fit が解消するか再検証。B は tool レベルなので**実装して実証**できる (D は canonical 設計判断)。

### 収束条件
dry-run 3 が dry-run 2 と**同じ構造的 finding を再現** (認可散り/owner 無し、set mis-fit、detail/share 不在) → 構造的と確定し設計を反映。**新しい失敗様式の連鎖**が出れば未収束で追加ラウンド。

---

## dry-run 3 検証済み結果 (2026-05-31、執筆者が checker + BOM 精査)

`checker.py --authoring` 実測: **GATE FAIL / 22 concerns (blocking ERROR 12 + INCONCLUSIVE 10) / exit 1**。subagent 自己報告を裏取り済。

### D 再現確認 (構造的と確定) + 深化
- **認可ルールが domain_decision に押し込まれた**: `domain_decision: proposal` owned_by=[R-01..04, UC-06/07/08]。BOM コメントに「authorization 種別が 7 種に無いので最も近い domain に置いた」と明記 (AI が独立に同結論)。
- **第二の構造シグナル**: 認可 precondition 7 件が **[PRECOND][INCONCLUSIVE]** (`ActorIsOwner`/`ActorMayManageMembers`/`ActorMayShareExternally`/`ActorIsMemberOrOwner`/`ActorHasEditRole`)。registry は `*Exists→NotFound`/`IndexInRange` のみ。**認可 precond 違反は定義上 `Forbidden`** → `*Exists→NotFound` と対称なパターン余地 (D-deterministic 案)。AI は `Forbidden` を capability 固有 failure reason として宣言。
- 資格情報/リンク保管 = `persistence_decision: unresolved` で素直に収まる (taxonomy ギャップでない、dry-run 2 と一致)。

### B 再現確認 + 拡大
- **3 画面が archetype 不在**: DocumentDetail (表示+条件付き編集) / ShareDialog (招待+役割選択+リンク発行) / PermissionSetting (一覧+inline編集+保存) → `archetype: unresolved` → `[UI][INCONCLUSIVE]`。list/confirm は適合。
- **ロール不一致が主操作を超えて拡大**: AI は `row_select`/`cancel`/`content_display` を使うが archetype は `select`/`deny`/`display` を要求 → `[UI][ERROR]`。主操作の部分正規化 (submit/save/affirm) だけでは不足、**全ロール語彙の正規化が必要** (B-rolevocab)。confirm の「削除する」→`affirm` 正規化は成功 (RULE E 部分対応が効いた)。

### 収束判定 (枯らし)
新しい失敗様式の連鎖なし。同 2 クラスタ (D=認可/開示の taxonomy 被覆 / B=archetype library + ロール語彙) が**深化して再現** = 構造的と確定。設計は敵対的 workflow (harden-bodyside-design) で硬化後に反映。

---

## 最終決定 (敵対的レビュー workflow `harden-bodyside-design` 後、2026-05-31)

4 レンズ (最小主義 / 整合 / 代替 / 厳密性) → 統合の結論。**3/4 が今サイクルで land、D-taxonomy は 1 ラウンド保留**。レビューが過剰構築 (authorization_decision 追加 + 3 archetype 一括) を是正した。

| finding | verdict | 反映 (実装済) |
| --- | --- | --- |
| **D-deterministic** | adopt | `checker.py` **不変** / extractor-spec RULE F (認可 precond は prose 確定時のみ `precondition_coverage:{<p>:[Forbidden]}` 宣言 + UC に `Forbidden`、未確定は捏造せず INCONCLUSIVE 維持) + RULE A に `Forbidden` baseline。injection 実験で実証 (宣言済 UC=PASS / 未確定 UC=ERROR) |
| **B-library** | adopt-modified | `form` archetype 追加 (`checker.py UI_ARCHETYPES` + RULE E 判別基準=load 要否)。view/detail・dialog は **defer** (空テンプレ/catch-all 回避)。実証: edit→form で偽 ERROR 4→0 |
| **B-rolevocab** | adopt-modified | RULE E に正規化表 (**AI 側のみ**、checker は canonical のみ受理)。`cancel`↔`deny` 統合禁止・global `primary` 平坦化禁止 |
| **D-taxonomy** | **✅ 解決 (新 kind 却下)** | unconfounded dry-run 4 (`../operating-model-dryrun-3/PREREGISTER.md`) で認可は `domain_decision` に一貫着地・情報開示も policy なら domain と確認 → **7 種で足り canonical 04 不変**。残余は `domain_decision` 過積載 → 必要なら非 canonical な security/authz タグ |

**人間決定点** (synthesis `human_decision_points`):
1. **disclosure** を専用 kind (`disclosure_decision`) にするか / failure-reason 感度層へ送るか / 23-NOTE 据置か。
2. **canonical 04 を今変えるか** unconfounded round 待ちか (現 n=2 は交絡)。変えるなら must-change に `07` ovr055 を含める。
3. **`Forbidden` payload 形** (`{reason_code}` vs `{actor, action, required}`) — 監査可能性に効く。
4. **unconfounded falsifier round** を回すか (7 種を伏せて prose+答えを執筆、falsifier「認可が domain_decision の外に着地」)。
5. form + rolevocab の同サイクル land = ✅ 実施済。

**収束**: tool 側 3 finding は **DRY (実装+実証済)**。D-taxonomy も **unconfounded dry-run 4 で決着 (新 kind 却下、7 種で足る)** → 本体側 finding (B/D) すべて枯れた。canonical 01-10 は不変。
