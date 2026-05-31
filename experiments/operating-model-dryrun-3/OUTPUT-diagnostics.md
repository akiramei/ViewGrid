# 診断レポート — 備品・社用品の貸し出し管理 (意味設計コンパイラ AI 抽出器)

> extractor-spec v1.0 §3 形式。固定接頭辞 ID + severity + source + bom_ref + resolution。
> 読んだファイルは extractor-spec.md と INPUT-prose.md のみ。
> このドメインは underdetermined。prose は「まだ決めきれていないこと」を自ら列挙しており、
> それらは大半が **操作の成否/境界/可視範囲/ライフサイクル終端** を左右する意味的決定 = proposal-ERROR。
> よしなに補完せず、決められない意味的決定は正直に止める。

---

## DEC- (Decision ownership)

### DEC-001
- severity: proposal-ERROR
- message: 承認できる主体 (上長 / 総務 / 両方 / 他部署またぎの承認者) が確定しておらず、承認という決定の所有者が定まらない。
- source: prose 役割『上長と総務の権限の重なり方は、運用してみないと正直わからない』, まだ決めきれていないこと 第5項『部署をまたいで備品を貸すケースのときの承認者が誰になるか』
- bom_ref: decision_ownership.domain_decision / UC-05 ApproveLoanRequest / precondition ActorMayApprove
- resolution: 承認権限の主体集合と、他部署またぎ時の承認者ルールを人間が確定する (上長 only / 総務 only / 階層 / 部署横断時の特例)。

### DEC-002
- severity: proposal-ERROR
- message: 情報システム担当が IT 機器 (PC/社用スマホ) について「見たい・やりたいこと」が総務と異なり、その決定責務が本 Capability に属するか別 Capability かが未整理。decision の跨ぎが解消されていない。
- source: prose 役割『情シスは…キッティングや誰に渡ったかを別途把握する必要がある。総務とは見たい範囲が少し違う』, まだ決めきれていないこと 第4項
- bom_ref: decision_ownership.domain_decision / boundaries.excluded (キッティング) / entities.referenced Actor
- resolution: IT 機器の貸出に情シス固有の決定 (キッティング状態・受領者把握) を本 Capability に含めるか、別 Capability として切り出すかを人間が決める。

### DEC-003
- severity: proposal-ERROR
- message: persistence_decision の owner が prose に一切現れず、何を永続化し誰が所有するかが未定義 (owned_by を空・unresolved とした)。
- source: absent from prose
- bom_ref: decision_ownership.persistence_decision
- resolution: 永続化責務 (申請/貸出/履歴の保存) の owner を人間が指定する。本 Capability 内か基盤側かを含めて確定。

### DEC-004
- severity: WARNING
- message: LoanHistoryEntry を独立 entity とするか Loan の派生 (導出) とするかが、履歴 (history) 決定の表現として未確定。append-only 性 (R-04) 自体は確定だが、保持の形は表現上の選択。
- source: prose やりたいこと『誰がいつ借りていつ返したか』(履歴の構造は未記述)
- bom_ref: entities.owned LoanHistoryEntry / decision_ownership.history_decision
- resolution: 履歴を独立レコードとして保持するか Loan の状態遷移ログから導出するかを選び記録する (どちらでも安全)。

---

## RUL- (Rule)

### RUL-001
- severity: proposal-ERROR
- message: 承認が必要な備品とそうでない備品の線引き基準 (金額? 台数? カテゴリ?) が未確定。この基準は RequestLoan が承認ステップを挟むか即時貸出かを分け、操作の成否フローを左右する。
- source: prose やりたいこと『高価なものや台数が少ないものは…承認をはさみたい。安いものはその場で。ここの線引きはまだふわっとしてる』, まだ決めきれていないこと 第1項『総務内でもまだ意見が割れてる』
- bom_ref: R-01 HighValueOrScarceEquipmentRequiresApproval / UC-04 RequestLoan
- resolution: 承認要否の判定属性 (価格閾値 / 在庫台数 / カテゴリ allowlist 等) を人間が確定し、Equipment にその属性を持たせる。

### RUL-002
- severity: proposal-ERROR
- message: 返却完了の終端が「本人の『返しました』だけ」か「総務の現物確認まで必須」かが未確定。ライフサイクル遷移の最終状態と、備品が再び Available に戻るタイミングを変える。
- source: prose やりたいこと『現物確認するステップもほしい気がする』, まだ決めきれていないこと 第3項『現物確認を必須にするか、本人の「返しました」だけで完了にしていいか』
- bom_ref: R-02 ReturnCompletionRequiresPhysicalConfirmation / UC-07 / UC-08
- resolution: 現物確認を必須遷移にするか任意 (備品種別で分岐?) かを人間が決定する。これで UC-08 が必須 UC か optional かも確定する。

### RUL-003
- severity: proposal-ERROR
- message: 「借りている人が誰か」の可視範囲 (誰に・どこまで見せるか) が未確定。query (UC-01/02/03) の出力内容を左右し、保有者情報を payload/feedback に含めてよいかを決める。
- source: prose 守りたいこと『借りている“モノ”の空き状況と、借りている“人”が誰かは見せる範囲を分けて考えたい』, まだ決めきれていないこと 第2項『便利さとプライバシーのバランス。決めきれていない』
- bom_ref: R-05 HolderIdentityVisibilityIsRestricted / UC-01 / UC-03 / decision_ownership.rendering_decision
- resolution: 保有者氏名を見られる主体 (本人/上長/総務のみ 等) を人間が確定し、各 query の出力スキーマに反映する。

### RUL-004
- severity: INFO
- message: 総務専有操作 (登録/廃棄/台数変更) の認可不変条件 R-03 は prose で明示確定済み。確認のみ。
- source: prose 守りたいこと『大元の操作は総務以外が触れないように』, 画面イメージ 4『総務だけがいじれればいい』
- bom_ref: R-03 EquipmentMasterOperationsAreOfficeAdminOnly
- resolution: 不要 (確定)。ただし「総務」ロールの定義主体は DEC-002/BND-001 と連動。

### RUL-005
- severity: INFO
- message: 履歴改ざん不可 R-04 は prose で明示確定済み。確認のみ。
- source: prose 守りたいこと『過去の貸出履歴は勝手に書き換えられないように』
- bom_ref: R-04 LoanHistoryIsImmutable
- resolution: 不要 (確定)。

---

## PRE- (precondition)

### PRE-001
- severity: proposal-ERROR
- message: UC-05/UC-06 の認可 precondition `ActorMayApprove` の被覆基準が prose で確定していないため、precondition_coverage に捏造せず未宣言とした。誰が承認できるかが決まるまで PRECOND は INCONCLUSIVE。
- source: prose 役割『上長と総務の権限の重なり方は運用してみないとわからない』, まだ決めきれていないこと 第5項
- bom_ref: UC-05 / UC-06 precondition ActorMayApprove / precondition_coverage (未宣言)
- resolution: 承認権限ルールを確定したら `precondition_coverage: { ActorMayApprove: [Forbidden] }` を宣言し、UC-05/06 の failure_reasons に Forbidden が載っていること (済) と双方向一致させる。

### PRE-002
- severity: WARNING
- message: 返却状態の precondition (LoanIsActive / ReturnPendingConfirmation) の定義は RULE002 の現物確認要否に依存する。現物確認が任意なら ReturnPendingConfirmation 状態自体が存在しない可能性がある。
- source: prose まだ決めきれていないこと 第3項 (現物確認の要否)
- bom_ref: UC-07 precondition LoanIsActive / UC-08 precondition ReturnPendingConfirmation / R-02
- resolution: RUL-002 の決定後に、貸出ライフサイクルの状態集合 (Active→ReturnDeclared→Confirmed か Active→Returned か) を確定する。

### PRE-003
- severity: INFO
- message: 存在系 precondition (EquipmentExists / LoanExists / LoanRequestExists / EquipmentAvailable / RequestIsPending) は *Exists 系として NotFound・状態失敗で被覆済み。確認のみ。
- source: prose 各操作の対象 (備品/申請/貸出) の存在前提
- bom_ref: canonical_failure_reasons NotFound / NotPending / NotActive
- resolution: 不要。

---

## FAIL- (失敗理由)

### FAIL-001
- severity: proposal-ERROR
- message: 「貸出中で借りられない」失敗 `AlreadyLoaned` の存在と payload に保有者 (current_holder) を含めるかが、RUL-003 の保有者可視範囲と衝突する。失敗応答経由で保有者が漏れうる。
- source: prose 背景『「貸出中」のまま埋まってる』 + 守りたいこと (保有者可視範囲)
- bom_ref: canonical_failure_reasons AlreadyLoaned / UC-04 / R-05
- resolution: AlreadyLoaned の payload に保有者情報を含めるか (含めるなら誰に返すか) を RUL-003 と整合して人間が決める。

### FAIL-002
- severity: proposal-ERROR
- message: 認可失敗 `Forbidden` の payload 形 (actor/action/required) が baseline 注記どおり未確定。承認権限・総務専有の双方を表すが required の表現が未定。
- source: prose 守りたいこと『総務以外が触れないように』, やりたいこと『総務か上長の承認』
- bom_ref: canonical_failure_reasons Forbidden / UC-05,06,08,09,10,11
- resolution: Forbidden の payload スキーマ (required に role 名を入れるか権限種別か) を人間が確定する。

### FAIL-003
- severity: WARNING
- message: AlreadyLoaned を canonical Conflict に寄せるか capability 固有名のままにするかの選択。意味は「空きでない備品の貸出衝突」で Conflict に近いが、conflicting_ids より borrower 文脈が明確なため固有名を提案した。
- source: prose 背景『「貸出中」のまま埋まってる』
- bom_ref: canonical_failure_reasons AlreadyLoaned (RULE A)
- resolution: Conflict (payload conflicting_ids) に正規化するか AlreadyLoaned を固有宣言のまま使うかを人間が確認する。

### FAIL-004
- severity: INFO
- message: canonical_failure_reasons の name は一意に保つこと。初稿で AlreadyLoaned を二重宣言しかけたため単一宣言へ集約済み。確認のみ。
- source: (抽出器の自己整合)
- bom_ref: canonical_failure_reasons AlreadyLoaned
- resolution: 不要 (集約済み)。

### FAIL-005
- severity: WARNING
- message: 台数 (Quantity) の値域・不正値拒否 (InvalidDimensions) を prose が述べないため inferred で補った。負数/非整数/在庫超の扱いが未定。
- source: prose 役割『台数管理』, 画面イメージ 4 (値域記述なし)
- bom_ref: canonical_failure_reasons InvalidDimensions / UC-11 / value_objects Quantity
- resolution: 台数の許容値域 (>=0 / 整数 / 現在貸出数を下回れない 等) を人間が確定する。

---

## EVT- (event / 観測可能性)

### EVT-001
- severity: proposal-ERROR
- message: 督促通知 (返却予定日が近づいたら本人へ、過ぎたら総務へ) は actor 起動の UC ではなく時間経過で発火する。emitted_by を UC に結べず、観測モデル (スケジューラ/トリガ条件) が未定義。SCHEDULER は擬似値。
- source: prose やりたいこと『返却予定日が近づいたら借りてる本人に自動でお知らせ。過ぎたら総務にも通知』
- bom_ref: events ReturnDueSoonNotified / ReturnOverdueNotified (emitted_by: SCHEDULER)
- resolution: 督促を本 Capability のスケジュール起動操作 (UC) として定義するか、外部スケジューラからの入力イベントとして扱うかを人間が決める。「近づいたら」の閾値 (何日前) も確定が必要。

### EVT-002
- severity: WARNING
- message: 督促の宛先「総務」「本人」は DEC-002/RUL-001 のロール定義と連動する。通知チャネル (メール/アプリ内) は本 Capability の範囲外候補。
- source: prose やりたいこと (督促), 役割 (総務/本人)
- bom_ref: events ReturnOverdueNotified / boundaries.excluded
- resolution: 通知の宛先解決と送信チャネルの責務境界を人間が確定する。

---

## BND- (境界 / 共有概念)

### BND-001
- severity: proposal-ERROR
- message: 社員/上長/総務/情シスの役割・部署の権威が本 Capability 内か別 Capability (ACTOR_DIRECTORY 想定) かが未確定。Actor を referenced としたが authority が未宣言で、承認者・可視範囲・通知宛先の全てがこのロール定義に依存する。
- source: prose 役割『上長と総務の権限の重なり方は運用してみないとわからない』, まだ決めきれていないこと 第4・5項
- bom_ref: entities.referenced Actor / boundaries.depends_on ACTOR_DIRECTORY
- resolution: 役割・部署のマスタを別 Capability とするか本体に持つかを決め、shared_concepts / depends_on で authority を構造宣言する。

### BND-002
- severity: proposal-ERROR
- message: 情シスのキッティング・受領者把握が本 Capability の範囲か否かが未整理 (excluded に暫定計上)。IT 機器貸出に総務フローと別系統が必要かが境界を変える。
- source: prose 役割『情シスは…キッティングや誰に渡ったかを別途把握。総務とは見たい範囲が少し違う』, まだ決めきれていないこと 第4項
- bom_ref: boundaries.excluded (キッティング) / DEC-002
- resolution: キッティング/受領者把握を本 Capability に含めるか別 Capability として切るかを人間が確定する。

---

## UI- (UI 意味契約)

### UI-001
- severity: WARNING
- message: 「承認・管理画面」は承認待ち一覧 + 承認/却下 + 現物確認 + 返却遅れリストを 1 画面に詰めた複合画面。library に複合 archetype は無いため捏造せず、list/confirm の複数 ui_contracts エントリ (ApprovalQueueList / ApprovalDecisionConfirm / OverdueReturnsList / PhysicalReturnConfirm) へ分解した。分解の妥当性は要人間確認。
- source: prose 画面イメージ 3
- bom_ref: ui_contracts ApprovalQueueList / ApprovalDecisionConfirm / OverdueReturnsList / PhysicalReturnConfirm
- resolution: この 4 分解が運用上の単一画面意図と一致するか人間が確認する (1 画面=複数 archetype の合成で良いか)。

### UI-002
- severity: WARNING
- message: 「備品の登録・台数管理画面」も複合 (新規登録=form + 既存編集/廃棄=edit)。EquipmentRegisterForm (form) と EquipmentMaintenanceEdit (edit) に分解した。form/edit の判別基準は「既存値を load するか」で、登録=set→form、台数変更/廃棄=既存編集→edit とした。
- source: prose 画面イメージ 4
- bom_ref: ui_contracts EquipmentRegisterForm / EquipmentMaintenanceEdit
- resolution: 登録と編集を別画面に割るか単一画面のタブ等にするか、UI 構成意図を人間が確認する。

### UI-003
- severity: WARNING
- message: list/form/edit/confirm 各画面の必須 affordance (form の validation_error feedback、edit の load/discard/unsaved_warning、confirm の表示等) を prose が述べていないため捏造せず空のままにした。決定的検査器 (check_ui_contracts) が archetype テンプレートと照合し必須欠落を [UI][ERROR] で捕捉する想定。
- source: prose 画面イメージ 1-4 (affordance の詳細記述なし)
- bom_ref: 全 ui_contracts エントリの interactions/feedback
- resolution: 不要 (意図的に空)。必須充足は決定的検査器に委ねる。prose で affordance が追記されたら lift する。

### UI-004
- severity: WARNING
- message: 「誰が借りてるかまで全員に見せるか迷い中」が EquipmentBrowseList の表示内容を左右する (保有者カラムを出すか)。これは rendering / RUL-003 と連動し、UI の feedback/display affordance には未反映。
- source: prose 画面イメージ 1『誰が借りてるかまで全員に見せるかは迷い中』
- bom_ref: ui_contracts EquipmentBrowseList / R-05 / decision_ownership.rendering_decision
- resolution: RUL-003 の保有者可視範囲決定後に、一覧画面で保有者を表示する display 要素を追加するか決める。

### UI-005
- severity: INFO
- message: 「借りる」「返しました」「承認/却下」「確認」を canonical role へ正規化 (RULE A UI 版): 行アクション→select、承認→affirm、却下→deny (cancel と統合せず)、登録→primary_action、台数変更保存→save。global primary に潰していない。
- source: prose 画面イメージ 1-4 の各ボタン表現
- bom_ref: 全 ui_contracts の interactions
- resolution: 不要 (正規化済み)。

---

## AC- (受け入れ条件のテスト可能性)

### AC-001
- severity: proposal-ERROR
- message: 「一目でわかる」「自然に残る」「サッと借りられる」等の要求が定性的でテスト可能な受け入れ条件に落ちていない。特に督促の「近づいたら」「過ぎたら」は閾値未定でテスト不能。
- source: prose 背景・やりたいこと (定性表現), 督促 (閾値なし)
- bom_ref: UC-01 / events ReturnDueSoonNotified
- resolution: 督促閾値 (何日前/何日超過) と「空き状況の可視」の受け入れ基準を測定可能な形で人間が定義する。

### AC-002
- severity: WARNING
- message: 承認フローの受け入れ条件 (承認待ち→承認後の貸出成立、却下後の不成立) は UC として表現したが、承認要否分岐 (RUL-001) と承認者 (DEC-001) が未定のためエンドツーエンドのテストシナリオが書けない。
- source: prose やりたいこと (承認), まだ決めきれていないこと 第1・5項
- bom_ref: UC-04 / UC-05 / UC-06 / R-01
- resolution: RUL-001 / DEC-001 確定後にシナリオを確定する。
