# SEEDED HOLES — 答え合わせ用 (抽出器 subagent には非開示)

dry-run 1 の `INPUT-prose.md` に意図的に仕込んだ穴と、未仕込みでも出てほしい論点。
methodology/23 を「実運用で枯らす」ための題材。**新ドメイン (画像グリッド以外) + UI 画面** で:
(1) extractor-spec の capability 非依存性、(2) AI が prose から UI archetype を認識して lift する
end-to-end 経路 (prototype では hand-built BOM の smoke-test のみ)、(3) 新ドメインが診断カタログの
穴を炙り出すか、を測る。

## 仕込んだ穴 × 期待する捕捉者

| ID | 穴 | prose のどこ | 期待する捕捉 | ledger 対応 |
| --- | --- | --- | --- | --- |
| **S1** | ログイン失敗時「メール不在」と「パスワード相違」を区別して返すか不明 | 「失敗したら入れない」だけ | FAIL 粒度 + **情報開示の判断** (DEC/MD proposal-ERROR)。区別して返すと user enumeration 脆弱性 | A-1 系 + 新規? |
| **S2** | 5 回失敗ロックの「しばらく」= ロック時間・解除条件が未定義 | 「しばらくログインできない」 | MD/proposal-ERROR (成否を左右 = 意味的) | A-2/MD |
| **S3** | 失敗カウントの単位 (メール単位/IP 単位) と リセット条件が未定義 | 同上 | DEC/proposal-ERROR (所有・境界) | 14/DEC |
| **S4** | ログイン画面に「キャンセル/中止」操作なし + 認証失敗フィードバック表示が prose に無い | 画面イメージ | **[UI][ERROR]**: login archetype 必須 `cancel` + feedback `auth_failure` 欠落 (決定的ツール) | §4 / H7 |
| **S5** | パスワード変更画面の archetype 認識 (edit?) と必須 affordance | 画面イメージ | AI が archetype 認識 → edit なら `load/discard` + `validation_error`/`unsaved_warning` 欠落を決定的ツールが捕捉。または archetype 曖昧を UI 診断 | §4 |
| **S6** | パスワード最小長・複雑性が未定義 + そのポリシー所有者 (domain/validation) 未定義 | 「短すぎるのはダメ」 | MD/proposal (長さは成否を左右) + DEC | MD/DEC |
| **S7** | セッションの有効期限/タイムアウト/明示ログアウト/複数同時セッション可否が未定義 | 用語「セッション」 | lifecycle RUL / MD。**ライフサイクル診断**がカタログにあるか | RUL lifecycle |
| **S8** | 退会ユーザーの拒否失敗理由が NotFound か別か (退会=存在するが無効) 未定義 | 「退会した利用者は…」 | FAIL 粒度 (Disabled/Withdrawn vs NotFound) | A-1 系 |
| **S9** | パスワード変更に現在パスワードの本人確認が要るか未定義 (重要なのに prose に無い) | 変更画面 | DEC/security proposal-ERROR。本人確認の欠落 | 新規? |

## 正規化テスト (RULE A)

- 「メールアドレスが存在しない」→ canonical `NotFound` (+ `entity_kind: User`)。`UserNotFound`/`EmailNotFound` を作ったら RULE A 違反。

## 診断カタログの穴を探す観点 (枯らしの核心)

- **情報開示 / 本人確認 / user enumeration** のような **セキュリティ判断** を、Decision taxonomy の 7 種
  (domain/validation/workflow/persistence/ui_interaction/rendering/history) にきれいに収められるか?
  収まらず無理に押し込む or owned_by を空にして `unresolved` にするなら → **decision taxonomy のドメイン被覆ギャップ**
  (= 23/methodology 本体への finding 候補)。
- **login archetype** は `auth_failure` feedback を必須にするが **アカウントロック (locked_out) のフィードバック** は持たない。
  S2/S4 でロックを扱うと archetype library の不足が出るか? → archetype ライブラリのギャップ候補。
- セッション lifecycle (期限/失効) を扱う診断 ID 接頭辞が無い (RUL でやる想定だが EVT/lifecycle 固有がない)。

## 評価の観点 (執筆者が独立検証する)

1. AI 抽出器が S1〜S9 を何件検出したか (過小評価/見逃しは finding)。
2. UI: AI が 2 画面を archetype 認識して `ui_contracts` に lift したか。決定的 checker --authoring が
   login の cancel/auth_failure 欠落を `[UI][ERROR]` で捕捉したか (end-to-end seam の初実証)。
3. RULE A 正規化が画像以外ドメインでも働いたか。
4. 新ドメインが extractor-spec / 23 の **想定していなかった穴** (セキュリティ判断・archetype 不足・lifecycle) を炙り出したか。
5. checker --authoring の GATE 判定が「正しい理由だけ」で出ているか (命名ノイズ・偽 INCONCLUSIVE が無いか)。
