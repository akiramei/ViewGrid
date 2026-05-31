# SEEDED HOLES — dry-run 3 (操作モデル枯らし 2 回目、`operating-model-dryrun-2/`) — subagent 非開示

目的: **本体側 finding F-R2-B (UI archetype library) と F-R2-D (decision taxonomy のセキュリティ判断被覆)** を
**2 点目データ**で枯らす。dry-run 2 (アカウントアクセス) が 1 点目。題材は認可・情報開示が豊富で
複数画面 (list/detail/share/settings/confirm) を持つ「チーム文書共有・権限管理」。
構造的 finding が**ドメインを変えても再現するか** (= 偶然でなく taxonomy/library の構造的ギャップか) を確認する。

## F-R2-D 観点 — セキュリティ判断の落ち先 (本命)

| ID | 穴 | 期待 | 7 種のどこに落ちるか? |
| --- | --- | --- | --- |
| **D-S1** | 誰が共有/役割変更してよいか (持ち主のみ? 編集者も?) 未定義 | proposal-ERROR (認可の所有) | **authorization 不在** → domain? validation? に散る or 所有者なし |
| **D-S2** | 文書一覧に「アクセス権の無い文書」を存在だけ見せるか隠すか未定義 | proposal-ERROR (情報開示) | **情報開示の居場所なし** → UI/FAIL 診断のみで decision_ownership に owner 無しで落ちる懸念 |
| **D-S3** | 共有リンクの公開範囲 (リンクを知る全員 vs 招待者のみ)・誰が発行可か未定義 | proposal-ERROR | authorization + 情報開示 |
| **D-S4** | 役割昇格 (閲覧→編集) を誰が承認するか未定義 | proposal-ERROR | authorization |
| **D-S5** | アクセス監査ログ (誰がいつ閲覧/変更) を残すか未定義 | proposal/WARNING | history? security/audit? の境界 |
| **D-S6** | 共有リンクの失効/無効化 (有効期限・revoke) 未定義 | proposal-ERROR | persistence + security |

**枯らしの核心 (D)**: D-S1/S2/S3/S4 の**認可・情報開示**判断が、7 種 (domain/validation/workflow/persistence/ui_interaction/rendering/history) の
どれかに自然に落ちるか、それとも散る/所有者なしになるか。dry-run 2 と**同じ構造的欠落が再現**するなら、taxonomy の
ギャップは偶然でなく構造的 → `authorization_decision` 種別追加 (or 既存表現方針) の設計判断が要る。
資格情報保存系 (D-S6 のトークン保管) は `persistence_decision` で素直に収まるはず (dry-run 2 と同じく taxonomy ギャップでない) — 認可/開示と切り分けて観測する。

## F-R2-B 観点 — archetype library の被覆 (本命)

| ID | 穴 | 期待 |
| --- | --- | --- |
| **B-S7** | 文書詳細画面の archetype | library に detail/view 系が無い → AI は INCONCLUSIVE 相当の `UI-` 診断 (archetype 不在) |
| **B-S8** | 共有ダイアログの archetype + 主操作 (招待/リンク発行) ロール | library に share/dialog 系が無い → archetype 不在。主操作ロール名の曖昧さ |
| **B-S9** | 権限設定画面 (set 系) の archetype + 「保存」ロール | `edit` を当てると load/discard/unsaved_warning が不適合 (F-R2-B1 再現)。「保存」→ submit/save 不一致 (F-R2-B2 再現) |
| **B-S10** | 削除確認ダイアログの confirm archetype + 「削除する/やめる」ロール | `confirm` は affirm/deny 必須。「削除する」→affirm, 「やめる」→deny のロール正規化が要るか (B2 系) |

**枯らしの核心 (B)**: library 5 種 (login/search/edit/list/confirm) が実画面を被覆できるか。
detail/share のような**頻出画面に archetype が無い** (B-S7/S8) なら library 被覆ギャップが確定。
set 系 (B-S9) の edit mis-fit と 主操作ロール不統一 (B-S9/S10) が dry-run 2 と再現するか。

## 評価の観点 (執筆者が独立検証)

1. D: 認可/情報開示判断が decision_ownership にどう落ちたか (owner 無し/散り の再現)。`authorization` 的判断の宛先の有無。
2. B: 各画面の archetype 認識 (detail/share の不在 → `UI-` 診断)、set 系 mis-fit、主操作ロール正規化の要否。
3. checker --authoring の GATE 判定 (純度・偽 INCONCLUSIVE)。
4. dry-run 2 と**同じ構造的 finding が再現**したか (= 構造的) / **新しい失敗様式**が出たか (= 未収束)。
