# 診断レポート — 意味設計コンパイラ AI 抽出器

> 入力 prose: `experiments/operating-model-dryrun-2/INPUT-prose.md`
> ドメイン: チーム文書共有・権限管理 (認可・役割・共有リンク)
> 対応 BOM: `OUTPUT-bom.yaml`
> 方針: 操作の成否/境界/不変条件/所有権を左右する未定義は **proposal-ERROR**。表現上の選択は WARNING。確定事項は INFO。
> RULE C: proposal-ERROR を出した項目の BOM provenance は `proposal`/`unresolved` に揃えてある。

---

## Decision ownership (DEC-)

### DEC-01
- severity: proposal-ERROR
- message: 認可 (誰が削除/編集/閲覧できるか) の判断を **どの層が所有するか** が prose に明示されていない。BOM では domain に置いたが、認可を domain ルールとして焼くか、ui_interaction でのゲートに留めるかで成否の出し方が変わる。
- source: prose §守りたいこと「文書を削除できるのは持ち主だけ」「閲覧のメンバーは編集できない」「招待されていない人は開けない」
- bom_ref: decision_ownership.domain_decision / R-01 / R-02 / R-03
- resolution: 認可ポリシーの所有層 (domain invariant か、別途認可 Capability か) を人間が確定する。

### DEC-02
- severity: proposal-ERROR
- message: メンバーの **招待・役割変更・除去を誰が実行できるか** (持ち主のみ? 編集役割も可?) が prose に無い。前提 `ActorMayManageMembers` の中身が未定義で、これが定まらないと UC-02/03/04 の成否が決まらない。
- source: prose §背景「他のメンバーを招待」「あとから役割を変えたり、メンバーを外したり」(主語=誰か未記述)
- bom_ref: UC-02 / UC-03 / UC-04 (precondition: ActorMayManageMembers)
- resolution: 管理操作の認可主体を人間が定義する (例: 持ち主のみ / 編集役割以上)。

### DEC-03
- severity: proposal-ERROR
- message: **共有リンクを誰が発行できるか** が未定義。社外公開は影響範囲が大きく、認可主体 (持ち主のみ? メンバー全員?) が成否と情報開示範囲を左右する。
- source: prose §背景「共有リンクを発行する機能も欲しい」(主語未記述)
- bom_ref: UC-05 (precondition: ActorMayShareExternally)
- resolution: リンク発行の認可主体を人間が確定する。

### DEC-04
- severity: proposal-ERROR
- message: 招待の **ワークフロー** が未定義。招待された人の承諾 (承認) が必要か、即座にメンバーになるかで状態遷移と OpenDocument の成否が変わる。workflow_decision を `unresolved` で停止した。
- source: prose §背景「他のメンバーを招待して一緒に使えるようにする」(承諾フローの有無に言及なし)
- bom_ref: decision_ownership.workflow_decision / UC-02
- resolution: 招待が即時有効か承諾制かを人間が決める。

---

## Rule (RUL-)

### RUL-01
- severity: WARNING
- message: 認可ルール R-01〜R-03 の **enforced_at (適用層)** を domain と推定したが prose 根拠なし。表現上の選択だが、検査器のルール層判定に影響する。
- source: prose §守りたいこと (層に言及なし)
- bom_ref: R-01 / R-02 / R-03 (enforced_at)
- resolution: 認可を domain で強制するかの確認。

### RUL-02
- severity: proposal-ERROR
- message: **所有権 (持ち主) が移譲可能か** が未定義。R-04 を「持ち主 1 人・不変」と推定したが、持ち主が退職/離脱した場合の所有権の行方は削除可否 (R-01) に直結する意味的決定。
- source: prose §用語「持ち主が 1 人いる」(移譲・継承に言及なし)
- bom_ref: R-04 (OwnerIsSingleAndImmutable) / UC-06
- resolution: 所有権の移譲/継承ポリシーを人間が定義する。

---

## 失敗理由 (FAIL-)

### FAIL-001
- severity: WARNING
- message: 存在前提 (文書/メンバー不在) の **破れ時に失敗として返るか** が prose に無い。`NotFound` を補ったが、これは構造化に近い (存在前提の自然な破れ)。命名は RULE A で canonical 化済み。
- source: prose は文書/メンバーを前提とするが不在時の挙動を述べない
- bom_ref: canonical_failure_reasons.NotFound (UC-02〜08)
- resolution: 不在を失敗で返すか暗黙無視かの確認 (通常は NotFound でよい)。

### FAIL-002
- severity: proposal-ERROR
- message: **認可違反の失敗理由** (持ち主以外の削除 / 閲覧役割の編集 / 非メンバーの閲覧) を prose は「〜できない」とだけ述べ、失敗理由名・payload を定めていない。これは操作の成否を直接決めるため `Forbidden` を proposal として提案し停止する。
- source: prose §守りたいこと 3 件
- bom_ref: canonical_failure_reasons.Forbidden (UC-06/07/08)
- resolution: 認可違反を表す失敗理由名・payload (reason_code の値域) を人間が確定する。

### FAIL-003
- severity: proposal-ERROR
- message: `Forbidden` の **payload (reason_code の値域)** が未定義。「持ち主でない」「閲覧役割」「非メンバー」を区別して返すかどうかは UI のフィードバックと認可監査に影響する。
- source: prose §守りたいこと (失敗の内訳表現に言及なし)
- bom_ref: canonical_failure_reasons.Forbidden.payload
- resolution: reason_code を列挙するか単一にするかを人間が決める。

### FAIL-004
- severity: proposal-ERROR
- message: **共有リンク経由アクセスの認可モデル** が未定義。リンク所持者は閲覧のみか編集も可か、失効/期限/パスワードがあるか — これらが定まらないとリンク発行が情報開示境界を破る危険がある (prose §まだ決めきれていないこと「社外共有リンクのセキュリティ」が明示的に依頼)。失敗理由も未確定。
- source: prose §用語「共有リンク: 社外向けに発行する URL」/ §まだ決めきれていないこと「社外共有リンクのセキュリティで気をつけることがあれば教えてほしい」
- bom_ref: UC-05 / ShareLink (entity)
- resolution: リンクの権限 (閲覧固定か)・失効・期限・再発行・無効化を人間が定義する。SEC-01 も参照。

### FAIL-005
- severity: WARNING
- message: **文書作成 (UC-01) の失敗条件** が prose に無い。作成 UC 自体を inferred で起こしたが、命名重複や上限等の失敗を返すかは未確定。
- source: prose は作成操作を「持ち主(作成した人)がいる」から間接的に含意するのみ
- bom_ref: UC-01 (failure_reasons: [])
- resolution: 作成 UC を要求に含めるか、失敗条件があるかを人間が確認する。

---

## Precondition (PRE-)

### PRE-01
- severity: WARNING
- message: `DocumentExists` を全管理 UC の前提に補ったが、prose は明示していない (構造化に近い)。被覆失敗理由 `NotFound` と対応づけ済み。
- source: prose は文書を操作対象とするが「存在前提」を明文化していない
- bom_ref: UC-02〜08 (precondition: DocumentExists)
- resolution: 確認のみ (通常自然)。

### PRE-02
- severity: proposal-ERROR
- message: `ActorMayManageMembers` / `ActorMayShareExternally` / `ActorIsOwner` / `ActorIsMemberOrOwner` / `ActorHasEditRole` といった **認可系 precondition の判定基準** が prose 未定義。RULE F の precondition_coverage を宣言できない (被覆失敗理由が Forbidden に集約されるが、各 precond の中身が未確定)。
- source: prose §守りたいこと / §背景 (操作主体の資格を述べていない)
- bom_ref: UC-02〜08 の認可系 preconditions
- resolution: 各認可 precondition の判定ルール (役割×操作のマトリクス) を人間が確定する。

---

## Event / 観測可能性 (EVT-)

### EVT-01
- severity: WARNING
- message: BOM に挙げた events (MemberInvited 等) は **prose に観測要求が無く** AI が推論したもの。通知・監査・他システム連携の要否が未確定。
- source: prose に「事象を観測/通知する」記述なし
- bom_ref: events.*
- resolution: イベント発行 (通知/監査連携) が要件かを人間が確認する。

### EVT-02
- severity: proposal-ERROR
- message: **権限変更の監査履歴** が要るか未定義。prose §まだ決めきれていないこと「権限まわりは間違えると事故になる」は履歴/復元の要求を含意しうるが明示されていない。history_decision を `unresolved` で停止した。
- source: prose §まだ決めきれていないこと「権限まわりは間違えると事故になるので、抜けや危ない点があれば指摘してほしい」
- bom_ref: decision_ownership.history_decision
- resolution: 権限操作の履歴/監査/取り消しが要件かを人間が決める。

---

## 境界 / 共有概念 (BND-)

### BND-01
- severity: proposal-ERROR
- message: **Actor (操作者 / 招待された人 / 持ち主) の出所** が未定義。「メンバー」は社内ユーザー、招待は email、共有リンクは社外 — これらの主体が同じ User 概念か別かが不明。認証 Capability への依存 (depends_on) も宣言できず、RULE F の shared_concepts authority を空にせざるを得なかった。
- source: prose §背景「メンバー」「他のメンバーを招待」/ §用語「メンバー: 招待された人」(User/認証への言及なし)
- bom_ref: entities.referenced.Actor / boundaries.depends_on / shared_concepts
- resolution: 認証・ユーザー管理を別 Capability に置くか、本 Capability が Actor を所有するかを人間が決める。

### BND-02
- severity: WARNING
- message: 社外共有リンク経由のアクセス主体は **メンバーでない (Actor でない)**。OpenDocument (UC-07) の前提 `ActorIsMemberOrOwner` がリンク経由アクセスと矛盾する可能性。リンクアクセスを別 UC にすべきか境界が曖昧。
- source: prose §守りたいこと「招待されていない人は開けない」と §背景「社外の人にもリンクを送って見てもらえる」の緊張
- bom_ref: UC-07 / UC-05 / R-03
- resolution: 「非招待は開けない」とリンク公開の両立条件 (リンクは R-03 の例外か) を人間が確定する。**FAIL-004 と同根。**

---

## 受け入れ条件 / テスト可能性 (AC-)

### AC-01
- severity: WARNING
- message: 招待の **email の妥当性ルール** (形式・既存ユーザー限定・重複招待の扱い) が未定義。validation_decision に置いたがルール本体が prose に無い。
- source: prose §画面「メンバーをメールアドレスで招待する欄」
- bom_ref: UC-02 / value_objects.EmailAddress / validation_decision
- resolution: email 検証と重複招待の扱いを人間が定義する。

### AC-02
- severity: WARNING
- message: **編集 (UC-08) の単位** が未定義 — 何を保存し、同時編集の競合をどう扱うかが不明。テスト可能な受け入れ条件にならない。
- source: prose §画面「編集権限があれば編集もできる」(編集内容・競合に言及なし)
- bom_ref: UC-08
- resolution: 編集の保存単位・競合解決を人間が定義する (本要求の範囲外なら excluded へ)。

### AC-03
- severity: proposal-ERROR
- message: 一覧 (UC-09) の **「自分が関われる文書」の定義** が未確定。持ち主 + 被招待メンバー + リンク経由を含むかで返る集合が変わり、情報開示境界に影響する。
- source: prose §画面「自分が関われる文書がリストで並ぶ」
- bom_ref: UC-09 (postcondition: RelatedDocumentsReturned)
- resolution: 「関われる」の定義 (owner/member/link) を人間が確定する。

### AC-04
- severity: WARNING
- message: `Membership` / `ShareLink` エンティティの **識別子 (id) の存在** を推定した (対象特定に機械的に必要・安全 = RULE B 中段)。表現上の選択。
- source: prose は id に言及しない
- bom_ref: entities.owned.Membership / ShareLink
- resolution: 確認のみ。

---

## UI 意味契約 (UI-)

### UI-01
- severity: proposal-ERROR
- message: **文書詳細画面** に当てはまる archetype が library (login/search/edit/list/confirm) に **無い**。「中身を表示」(read/view) + 「編集権限があれば編集」(条件付き edit) の複合で、`edit` archetype は「既存値を load して編集」前提だが本画面は閲覧主体。archetype を `unresolved` とした (捏造せず)。
- source: prose §画面「文書詳細画面: 文書の中身を表示する。編集権限があれば編集もできる。」
- bom_ref: ui_contracts.DocumentDetailScreen
- resolution: read/view archetype (またはモード切替 view↔edit) をライブラリに追加するか、画面を分割するかを人間が決める。決定的検査器では INCONCLUSIVE。

### UI-02
- severity: proposal-ERROR
- message: **共有ダイアログ** に単一の library archetype が無い。「招待入力」+「役割選択」+「リンク発行」の 3 機能複合。主操作ボタン (共有リンク発行) を archetype の主操作ロールへ正規化したかったが、archetype が不定のため正規化先が決まらない (RULE E の主操作正規化が適用不能)。`unresolved` とした。
- source: prose §画面「共有ダイアログ: … 招待する欄、役割を選ぶところ、共有リンクを発行するボタン。」
- bom_ref: ui_contracts.ShareDialog
- resolution: ダイアログを単機能 archetype に分解するか、複合 form archetype を定義するかを人間が決める。INCONCLUSIVE。

### UI-03
- severity: proposal-ERROR
- message: **権限設定画面** も単一 archetype に収まらない。「メンバー一覧 (list)」+「役割変更 (edit/set)」+「除去」+「保存」の複合。確定操作「保存」を archetype 主操作ロールへ正規化したいが archetype 不定。さらに **役割変更は既存値の編集 (edit) より新値の set に近く**、set 系 archetype 不足 (F-R2-B1 と同型) に該当。`unresolved` とした。
- source: prose §画面「権限設定画面: 招待済みメンバーの一覧と、それぞれの役割を変えたり外したりする操作。「保存」する。」
- bom_ref: ui_contracts.PermissionSettingScreen
- resolution: list+inline-edit の複合 archetype を定義するか画面分割するかを人間が決める。INCONCLUSIVE。

### UI-04
- severity: proposal-ERROR
- message: 権限設定画面の **「保存」がどの操作を確定するか** が曖昧。役割変更 (UC-03) と除去 (UC-04) をまとめて 1 トランザクションで保存するのか、即時反映なのかで状態遷移と失敗時のロールバックが変わる。usecase_bindings.save を暫定で UC-03 にしたが確証なし。
- source: prose §画面「役割を変えたり外したりする操作。「保存」する。」
- bom_ref: ui_contracts.PermissionSettingScreen.usecase_bindings.save
- resolution: 「保存」のトランザクション境界 (即時/一括) を人間が確定する。

### UI-05
- severity: WARNING
- message: 全 5 画面で **feedback affordance (結果/エラーをどう画面表示するか) を prose が一切述べていない** ため、`feedback` を空にした (RULE E に従い捏造しない)。特に削除確認・共有ダイアログでの失敗 (Forbidden 等) の表示は意味層 failure_reasons に lift 済だが、画面表示の有無は未定義。決定的検査器が archetype 必須 feedback の欠落を `[UI][ERROR]` で捕捉する余地を残した (マスクしない)。
- source: prose §画面イメージ全体 (結果/エラー表示に言及なし)
- bom_ref: 全 ui_contracts.feedback
- resolution: 各画面のエラー/成功フィードバック表示要件を人間が定義する。

---

## MUST_DECIDE 候補 (MD-)

### MD-01
- severity: proposal-ERROR
- message: 本 prose で人間が必ず決めるべき意味的決定の束 (集約): (1) 各操作の認可主体 = DEC-01/02/03/PRE-02, (2) 共有リンクの権限・失効・境界 = FAIL-004/BND-02/SEC-01, (3) 招待ワークフロー (承諾の有無) = DEC-04, (4) 所有権移譲 = RUL-02, (5) 「関われる」の定義 = AC-03。これらが未決のまま実装すると認可事故 (prose が最も恐れている点) になる。
- source: prose §まだ決めきれていないこと「権限まわりは間違えると事故になる … 抜けや危ない点があれば指摘してほしい」
- bom_ref: decision_ownership 全体 / 認可系 preconditions / ShareLink
- resolution: 上記 5 束を人間が MUST_DECIDE として確定してから実装に渡す。

---

## セキュリティ補足 (SEC-) — prose §まだ決めきれていないことの明示依頼への回答

> prose が「社外共有リンクのセキュリティで気をつけることがあれば教えてほしい」と明示依頼。診断として正直に列挙する (BOM では未決 = proposal/unresolved)。

### SEC-01
- severity: proposal-ERROR
- message: 共有リンクの **失効・期限・無効化・推測困難性** が未定義。URL が漏れると非招待者が閲覧でき、R-03「招待されていない人は開けない」を実質的に破る。リンクは「閲覧固定」か、編集も許すかも未確定。
- source: prose §用語「社外向けに発行する URL」/ §守りたいこと「招待されていない人は開けない」
- bom_ref: UC-05 / ShareLink / R-03
- resolution: リンクの権限固定 (閲覧のみ)・有効期限・失効/再発行・トークンの推測困難性・(任意) パスワード保護を人間が定義する。**BND-02 / FAIL-004 と同根。**

### SEC-02
- severity: proposal-ERROR
- message: リンクの **永続化と無効化の所在** が未定義。発行済みリンクの一覧/失効操作が無いと、漏洩時に止められない。persistence_decision を `unresolved` で停止した。
- source: prose にリンク管理 (一覧/失効) の記述なし
- bom_ref: decision_ownership.persistence_decision / UC-05
- resolution: 発行済みリンクの管理 (一覧表示・個別失効) を要件に含めるか人間が決める。

### SEC-03
- severity: WARNING
- message: 役割の **最小権限**: 「閲覧」役割が一覧 (UC-09) で他メンバーの email や権限設定を見られるべきかが未定義。情報開示の粒度が不明。
- source: prose は役割を「閲覧/編集」の 2 値とするが、メタ情報 (メンバー一覧/email) の可視性に言及なし
- bom_ref: UC-09 / R-02 / PermissionSettingScreen
- resolution: 役割ごとのメタ情報可視性を人間が定義する。
