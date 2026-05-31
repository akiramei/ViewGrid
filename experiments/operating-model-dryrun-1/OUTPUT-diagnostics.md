# 診断レポート — アカウントアクセス (意味設計コンパイラ AI 抽出器)

> 入力: `experiments/operating-model-dryrun-1/INPUT-prose.md`
> 対の BOM: `OUTPUT-bom.yaml`
> 方針: 操作の成否/境界/不変条件/所有権を左右する未定義は **proposal-ERROR** (WARNING に格下げしない / RULE B)。
> 表現上の安全な選択は **WARNING** (= MUST_DECIDE)。確定事項は **INFO**。診断 severity と BOM provenance は一致 (RULE C)。

---

## 失敗理由 (FAIL-)

### FAIL-001
- severity: proposal-ERROR
- message: メールアドレスは存在するがパスワードが一致しない場合の失敗を、ユーザー不在 (`NotFound`) と区別するのか、それともセキュリティ上わざと同一扱い (どちらが間違っているか教えない) にするのかが未定義。これはログインの成否境界 + 情報漏洩面の意味的決定。
- source: §守りたいこと「ログインできるのは正しいメールアドレスとパスワードの組み合わせのときだけ」(不一致の内訳は未記載)
- bom_ref: UC-01 / InvalidCredentials / NotFound
- resolution: 人間が「不在とパスワード不一致を別失敗にするか / 列挙時に区別しないか」を決める。区別しないなら `InvalidCredentials` 1 本に畳み `NotFound` を UC-01 から外す。

### FAIL-002
- severity: proposal-ERROR
- message: パスワードの「ある程度の長さ」の最小値が数値で決まっていない。これは ChangePassword の成否を直接左右する不変条件 (`PasswordTooShort.min_length`)。
- source: §守りたいこと「パスワードが短すぎるのはダメ。ある程度の長さは要る」
- bom_ref: UC-02 / PasswordTooShort / R-04
- resolution: 最小長 (例: 8 / 12 文字) を人間が確定する。上限・文字種要件・既存パスワードとの差分要件の要否も併せて決める。

### FAIL-003
- severity: proposal-ERROR
- message: ロックアウト中のログイン試行をどの失敗理由で返すか (`AccountLockedOut`)、その payload `retry_after` の意味 (残り時間 / 解除時刻 / 固定文言) が未定義。
- source: §背景「5 回連続で失敗したらしばらくログインできない」
- bom_ref: UC-01 / AccountLockedOut / R-03
- resolution: ロック中の応答 (専用失敗 or 汎用失敗) と利用者に返す情報を人間が決める。

### FAIL-004
- severity: WARNING
- message: `NotFound` の payload を `{ entity_kind, entity_id }` に正規化したが、entity_id にメールアドレス (PII) をそのまま載せるか、内部 id にするかは記録すべき選択。
- source: RULE A 正規化 (prose に payload 記載なし)
- bom_ref: NotFound
- resolution: ログ/イベントに載せる識別子の種別を人間確認。PII ならマスキング方針も。

---

## precondition (PRE-)

### PRE-001
- severity: proposal-ERROR
- message: ChangePassword の `Authenticated` precondition (本人がログイン済みであること) を満たさない場合の挙動が prose に無い。未認証で変更画面に来たらどうするかが未定義。
- source: §背景「利用者が自分でパスワードを変更できるように」(本人性・ログイン要否は未明示)
- bom_ref: UC-02.preconditions[Authenticated] / precondition_coverage.Authenticated
- resolution: パスワード変更にログイン (セッション) を要求するか、別途現在パスワードの再入力を要求するかを人間が決める。決まれば失敗理由を被覆宣言する。

### PRE-002
- severity: WARNING
- message: ログインの対象ユーザー特定キーが EmailAddress であることは prose から導けるが、大文字小文字・前後空白の正規化 (照合の同値性) が未定義で、これが「組み合わせ一致」の成否に効く。
- source: §用語「メールアドレスで一人ひとりを区別する」
- bom_ref: UC-01.preconditions[UserExists] / EmailAddress (VO)
- resolution: メール照合の正規化規則 (小文字化・trim) を確認。値次第で成否が変わるなら proposal-ERROR へ昇格。

---

## Rule (RUL-)

### RUL-001
- severity: proposal-ERROR
- message: 「5 回連続で失敗」の連続性の判定範囲が未定義 — 同一メールアドレス単位か / 同一 IP・端末単位か、成功でカウンタをリセットするか、カウンタの保持期間 (時間窓) はあるか。判定範囲が違えばロックの発生条件 (=ログイン成否) が変わる。
- source: §背景「5 回連続で失敗したらしばらくログインできない」
- bom_ref: R-03 / AccountLockedOut
- resolution: 連続判定の対象軸・リセット条件・時間窓を人間が確定する。

### RUL-002
- severity: proposal-ERROR
- message: 「しばらく」の長さ (ロック継続時間) と解除条件 (時間経過で自動解除 / 管理者解除 / 段階的バックオフ) が未定義。ロック解除という状態遷移の成否そのもの。
- source: §背景「しばらくログインできないようにしたい」
- bom_ref: R-03 / AccountLockedOut.payload.retry_after
- resolution: ロック時間と解除トリガを人間が決める。

### RUL-003
- severity: proposal-ERROR
- message: パスワード変更を「本人のみ」に限定する不変条件 (R-06 / OnlySelfCanChangeOwnPassword) を置くかどうかが prose で確定していない。認可の所有を左右する。
- source: §背景「利用者が自分で」(他者のパスワード変更可否は未明示)
- bom_ref: R-06 / UC-02
- resolution: 自分以外 (管理者等) の変更を許すか、本人限定かを人間が決める。

---

## Decision ownership (DEC-)

### DEC-001
- severity: proposal-ERROR
- message: パスワード変更における「本人性 (認可)」の決定をどの層が所有するか未定義 (validation / domain / ui_interaction いずれも候補)。PRE-001/RUL-003 と連動。
- source: §背景「利用者が自分でパスワードを変更」
- bom_ref: decision_ownership.validation_decision / R-06
- resolution: 認可決定の所有層を人間が確定する。

### DEC-002
- severity: proposal-ERROR
- message: 最小長チェック・確認一致チェックを validation 層に置くか domain 不変条件に置くかが未定義。所有層が決まらないと R-04/R-05 の enforced_at を確定できない。
- source: §守りたいこと (層の割当は未記載)
- bom_ref: decision_ownership.validation_decision / R-04 / R-05
- resolution: 入力検証 (validation) と業務不変条件 (domain) の責務分界を人間が決める。

### DEC-003
- severity: proposal-ERROR
- message: ロックアウトの状態 (失敗回数・ロック中フラグ・解除時刻) を誰が所有・遷移させるか (workflow_decision) が未定義。
- source: §背景「5 回連続で失敗したら…」
- bom_ref: decision_ownership.workflow_decision / R-03
- resolution: ロック状態の所有者と遷移責務を人間が確定する。

### DEC-004
- severity: proposal-ERROR
- message: パスワードの保存方式 (平文禁止 / ハッシュ + ソルト / アルゴリズム) と失敗回数の保持先が未定義。`persistence_decision` を `unresolved` とした。これはセキュリティ要件であり「あるべき案」を AI が独断で確定すべきでない。
- source: §まだ決めきれていないこと「セキュリティ的に気をつけるべき点があれば教えてほしい」(保存方式は本文に無い)
- bom_ref: decision_ownership.persistence_decision
- resolution: パスワードハッシュ方式と保管要件を人間 (セキュリティ責任者) が確定する。最低限「平文保存しない」を明記。

---

## event / 観測可能性 (EVT-)

### EVT-001
- severity: proposal-ERROR
- message: ロックアウト判定には「ログイン失敗」が観測・蓄積されている必要があるが、失敗イベント (LoginFailed) を残すか・どの粒度かが prose で確定していない。観測できなければ R-03 が成立しない。
- source: §背景「5 回連続で失敗したら」(失敗の記録要否は未記載)
- bom_ref: events[LoginFailed] / R-03
- resolution: ログイン失敗の記録 (件数カウントに足る最小限) を残すことを人間が決める。

### EVT-002
- severity: WARNING
- message: ログイン成功・パスワード変更などのセキュリティ監査イベント (誰がいつ) を残すかが未定義。監査・不正検知の観点で推奨だが必須かは人間判断。
- source: §まだ決めきれていないこと「セキュリティ的に気をつけるべき点」
- bom_ref: events[LoginSucceeded, PasswordChanged] / history_decision
- resolution: 監査ログ/履歴の要否と保持期間を人間が決める。

---

## 境界 / 共有概念 (BND-)

### BND-001
- severity: proposal-ERROR
- message: 「パスワードを忘れたり」に触れているが、忘れた場合の **リセット / 再発行フロー** (本人確認手段・トークン発行・有効期限) が prose に全く無い。ChangePassword (本人がログイン済みで変更) ではカバーできない別ユースケース。現状は `excluded: PasswordReset` として除外宣言したが、スコープ判断は人間が要る。
- source: §背景「パスワードを忘れたり変えたくなったりすることがあるので」
- bom_ref: boundaries.excluded[PasswordReset]
- resolution: パスワードリセットを本リリース範囲に含めるか除外するかを人間が決める。含めるなら別 UC として要求を追記。

### BND-002
- severity: WARNING
- message: ログイン成功後に入る「自分専用の作業画面」「セッション」の寿命 (タイムアウト / 明示ログアウトの有無) が未定義。セッションは本 Capability の postcondition として現れるが、その後段境界が曖昧。
- source: §背景「成功すると自分専用の作業画面に入れる」/ §用語「セッション」
- bom_ref: UC-01.postconditions[SessionEstablished] / boundaries.depended_on_by[WorkArea]
- resolution: セッション寿命・ログアウト要件の所在 (本 Capability か下流か) を人間が決める。

---

## UI 意味契約 (UI-)

### UI-001
- severity: WARNING
- message: ログイン画面の失敗フィードバック (`auth_failure`) の表示内容が未定義。FAIL-001 と連動 — 「メール / パスワードのどちらが違うか」を表示するか、汎用「メールまたはパスワードが違います」にするか (セキュリティ上は後者推奨) が決まっていない。
- source: §背景「失敗したら入れない」(失敗時の画面表示は未記載)
- bom_ref: ui_contracts[LoginScreen].feedback[auth_failure] / UC-01
- resolution: 失敗メッセージの粒度を人間が決める。

### UI-002
- severity: WARNING
- message: パスワード変更画面に成功/失敗フィードバックの記述が prose に無く `feedback: []` とした。短すぎ・不一致時の表示が未定義。後段検査器が edit archetype の必須 feedback と照合する想定。
- source: §画面イメージ (変更画面の結果表示に言及なし)
- bom_ref: ui_contracts[ChangePasswordScreen].feedback
- resolution: 保存失敗 (短すぎ/不一致) と保存成功の表示を人間が決める。

### UI-003
- severity: INFO
- message: パスワード変更画面に「現在のパスワード」入力欄が prose に無いため、RULE E に従い捏造せず `interactions` に入れていない。edit archetype の本人確認として現在パスワード再入力が必要かは後段検査器/人間判断に委ねる (捏造回避の明示)。
- source: §画面イメージ「新しいパスワードの欄、確認用にもう一度入れる欄、「保存」ボタン」(現在パスワード欄の記載なし)
- bom_ref: ui_contracts[ChangePasswordScreen].interactions
- resolution: 本人確認のため現在パスワード再入力を要求するかを人間が決める (要求するなら secret_input を追加)。

---

## 受け入れ条件のテスト可能性 (AC-)

### AC-001
- severity: proposal-ERROR
- message: 「短すぎるのはダメ」「しばらくログインできない」「5 回連続で失敗」のうち、しきい値・期間・連続定義が数値化されていないため、そのままでは受け入れテストを書けない (FAIL-002/RUL-001/RUL-002 と同根)。
- source: §守りたいこと / §背景 の定性表現
- bom_ref: R-03 / R-04 / AccountLockedOut / PasswordTooShort
- resolution: 各しきい値を数値化し、合否を判定できる受け入れ条件に落とす。

### AC-002
- severity: INFO
- message: 「確認用パスワードが新パスワードと一致しないと保存させない」は等価判定でテスト可能な確定条件。R-05 として human-confirmed で lift 済み。
- source: §守りたいこと「確認用…が一致していないと保存させない」
- bom_ref: R-05 / PasswordConfirmationMismatch
- resolution: なし (確定。テスト可能)。

---

## MUST_DECIDE 候補サマリ (MD-)

### MD-001
- severity: WARNING
- message: 本診断の proposal-ERROR 群 (FAIL-001/002/003, PRE-001, RUL-001/002/003, DEC-001〜004, EVT-001, BND-001, AC-001) は、人間が決めるべき意味的決定の束。これらが未決のままでは下流 AI 実装が「勝手に決める」リスクが高い箇所。
- source: §まだ決めきれていないこと「抜けがあったら指摘してほしい / セキュリティ的に気をつける点を教えてほしい」
- bom_ref: 上記各 ID
- resolution: 上記 proposal-ERROR を MUST_DECIDE 台帳として人間が順に確定する。

---

## 集計

- proposal-ERROR: 14 (FAIL-001, FAIL-002, FAIL-003, PRE-001, RUL-001, RUL-002, RUL-003, DEC-001, DEC-002, DEC-003, DEC-004, EVT-001, BND-001, AC-001)
- WARNING: 7 (FAIL-004, PRE-002, EVT-002, BND-002, UI-001, UI-002, MD-001) ※ + 表現上の inferred 補完は BOM 側に記録
- INFO: 2 (UI-003, AC-002)
