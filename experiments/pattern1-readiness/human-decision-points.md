# Human Decision Points — deliberate decision 候補の整理 (工程管理層の前段)

> 実施 2026-06-01。IO-1 / IO-3 で production 改善が 2 件揃い、方法論 (研究→実コード→anchor 固定) は実証済。
> 工程管理層へ進む前に、**AI / Codex が「より自然」「より一貫」と判断して勝手に変えてはいけない設計判断**を
> 明示的に `deliberate` 化する (= BOM の decision ownership を強くする「閉じる」作業)。
> **本書は options / 現挙動 / 影響 / 推奨を整理した decision note**。ユーザー裁定後に各 BOM / overlay の
> `deliberate_decisions` ブロックへ反映する (実コード変更は裁定後・別工程)。

## なぜ人間決定点が先か
工程管理層 (change classification / gates / metrics) を作るには、まず「何を人間決定に分類するか」が要る。
下記 3 点はいずれも単なる実装詳細ではなく、変更するとテスト結果・ユーザー体験・将来互換性に影響する。
AI が判断して変えるべきものではなく、人間が deliberate decision として固定すべき対象。

## 反映スキーマ (裁定後に該当 BOM / overlay へ追加する形)
```yaml
deliberate_decisions:
  - id: <D2a | D2b | D-PV>
    subject: <対象>
    current_behavior: <現挙動 (file:line で裏取り)>
    decision: <preserve | change | document-only | …>   # 裁定後に確定
    provenance_transition: as-built-incidental -> deliberate
    rationale: <理由>
    owner: human
    anchor: <挙動を固定するテスト>
```

---

## D2a — ToPixelBbox の midpoint (x.5 px) 丸めモード
| 項目 | 内容 |
| --- | --- |
| subject | 比率→整数ピクセル bbox 変換時の中間値 (ちょうど x.5 px) の丸め |
| 現挙動 | `System.Math.Round(value)` 既定 = **MidpointRounding.ToEven (銀行家丸め)**。`CropFraction.cs:20-23` / `ManualCropFraction.cs:20-23` / `RegionRectFraction.cs:26-29` の **3 型で同一**。 |
| provenance (as-built) | **as-built-incidental** — 誰も「ToEven にしよう」と決めた形跡なし (`Math.Round` 既定)。ただし F-P12 で midpoint oracle `ToPixelBbox_Midpoint_Rounds_ToEven_AsBuilt` が固定済。 |
| 影響 | midpoint でのみ 1px 差。実画像で ちょうど x.5 px が出るのは稀だが、変更すると export/preview の bbox が sub-pixel 単位でずれる。3 型一貫なので変えるなら 3 箇所同時 = 仕様変更扱い。 |
| 選択肢 | **(a) preserve + document** (ToEven を deliberate として明文化、コード変更なし) / (b) change → AwayFromZero (「四捨五入」の直感に寄せる、bbox 境界が x.5 でずれる) / (c) document-only (現状維持だが provenance は incidental のまま) |
| **推奨** | **(a) preserve + document → deliberate**。F-P12 で oracle 固定済・3 型一貫・ToEven は中心揃いで系統誤差小・変更の UX 利得が薄く互換性リスクのみ。AI が「四捨五入の方が自然」と AwayFromZero へ変える事故を deliberate 固定で防ぐ。 |
| 反映先 | IMAGE_VARIANT overlay (CropFraction の `vo_method_contract.rounding`) + RENDERING。provenance を `as-built-incidental → deliberate`。 |

## D2b — ToPixelBbox の無効寸法 (負の軸引数) / 範囲外比率
| 項目 | 内容 |
| --- | --- |
| subject | `ToPixelBbox(width, height)` に負の軸寸法を渡した時の挙動 / 範囲外比率 (X,Y,W,H ∉ [0,1]) の扱い |
| 現挙動 (精密) | ① **範囲外比率**: x,y を `Clamp(round, 0, axis)` で [0,axis] に clamp、w,h は `Clamp(round, 0, axis-origin)` (origin≤axis なので max≥0) → **throw せず graceful に clamp**。② **負の軸引数** (width<0/height<0): `Clamp(_, 0, 負)` が min(0)>max(負) で **ArgumentException throw**。ただし axis=画像実寸で実運用は常に正 → **到達不能**。③ 入力ドメイン: Manual/Region/CropFraction は plain-data VO (ctor 検証なし、ID-9) で範囲外比率を型では弾かない。 |
| ★ 精密化 | d3 §3.4/§6 が「実=throw / 生成=正規化」と書いた発散は、実際には **負の軸引数のみ (到達不能)**。**範囲外比率は実装も clamp 済**で生成物と一致する → 当初想定より発散は狭い (記録すべき訂正)。 |
| 影響 | 実運用 (正の画像寸法) では (a)(b)(c) で観測差なし (到達不能パス)。oracle 非被覆。価値は「契約の明文化」= 将来 負寸法を渡す新コードを loud に弾くか silent に通すか。 |
| 選択肢 | **(a) document-only**: 「軸は正の前提 (画像実寸が保証)、範囲外比率は valid sub-rect に clamp」を precondition 明文化、コード変更なし / (b) explicit guard: 負軸を `ArgumentOutOfRangeException` で意図的に明示 throw / (c) normalize: 負軸を 0 等へ正規化 |
| **推奨** | **(a) document-only → deliberate (positive-axis precondition)**。範囲外比率は既に clamp で安全、throw は負軸のみで到達不能。最小リスクで現挙動を正確に固定し、d3 の「全域 conformance 未閉」caveat を「軸正の precondition」へ昇格して閉じる。(b) は defensive だが YAGNI、(c) は負軸を無音で通すため不可。 |
| 反映先 | IMAGE_VARIANT overlay (ToPixelBbox の `precondition` + `invalid_dimension`)。d3 §6 の caveat を解消。 |

## D-PV — PlacementValidator の conflict 同定順
| 項目 | 内容 |
| --- | --- |
| subject | 複数の既存配置が新配置と重複する時、どの `ConflictingPlacementId` を返すか |
| 現挙動 | validator は `existingPlacements` の **反復順で最初に重複した既存** の Id を返す (`PlacementValidator.cs:32-41`)。**呼び出し側 (Place/Move/Swap/UpdateOccupySize) は全て `placementRepository.FindByGridIdAsync(gridId)` から構築**し、同 repo は **`OrderBy(PlacementOrder)`** (`EfGridPlacementRepository.cs:15`)。→ **実運用では conflict 同定 = PlacementOrder 昇順で最初の重複 = 安定・決定的・意味づけ可** (PlacementOrder = 配置順/z 順)。 |
| provenance (as-built) | validator 単体では **as-built-incidental** (反復順依存と PV-2 anchor に明記)。だが caller 契約として実質 stable (PlacementOrder)。**決定性は production では既に成立しており、未固定なのは「validator は入力順を保存する」「caller は安定順を渡す」という契約が暗黙だから**。 |
| 影響 | conflict Id は UI エラーメッセージ・Move/Swap 却下・再現性に流れる。AI が validator を HashSet 化 / LINQ 並べ替え、または repo の `OrderBy(PlacementOrder)` を外すと、報告される conflict 対象が **silent に変わりうる**。 |
| 選択肢 | **(a) deliberate**: 「conflict identity = caller 提供順で最初の重複。契約として caller は安定順 (PlacementOrder 昇順) を渡す。validator は入力順を保存する」を明文化 + caller 契約を anchor 化 / (b) validator を順序非依存に (例: min PlacementId) = caller 順から decouple (validator は PlacementOrder を持たないので別 key が要る) / (c) as-built-incidental のまま |
| **推奨** | **(a) deliberate「first conflicting by caller order = PlacementOrder 昇順 (stable)」**。実運用は既にこの挙動。意図を明文化し「validator は入力順保存 + caller は安定順を渡す」を契約化すれば、AI が collection 型/LINQ/repo OrderBy を変えて conflict が変わる事故を防げる。ユーザー推奨「安定順序で返す」と一致。(b) は validator に新 key を要し over-engineering。 |
| 反映先 | GRID BOM (PlacementValidator) の decision_ownership + caller 契約注記。PV-2 spec/overlay の `conflict_identity.provenance` を `as-built-incidental → deliberate`。既存 anchor `ConflictingPlacementId_Is_First_In_Collection_Order_AsBuilt` のコメントを「caller 安定順 (PlacementOrder) が契約」へ更新 (任意)。 |

---

## サマリ / ★ 裁定結果 (2026-06-01、ユーザー裁定済)
| ID | 決定 | 裁定 | コード変更 | provenance |
| --- | --- | --- | --- | --- |
| D2a | midpoint 丸め | ✅ **preserve + document (ToEven)** | なし | incidental → **deliberate** |
| D2b | 無効寸法 | ✅ **document-only (軸正 precondition)** | なし | incidental → **deliberate** |
| D-PV | conflict 同定順 | ✅ **deliberate (PlacementOrder 昇順で先)** | なし (契約明文化のみ) | incidental → **deliberate** |

**3 件とも推奨どおり「実コード変更なし・現挙動を deliberate として明文化」で裁定** = 既存挙動を壊さず decision ownership だけ強める純粋な「閉じる」作業。実コード/oracle への波及はゼロ (いずれも preserve)。

## D02 — auto-save 保留中の編集がある状態での Undo / Redo / ジャンプ (2026-10-02 追補)
不具合調査報告 (2026-10-02) の D02 で表面化した設計判断。実コード修正 (797f13c) は flush-then-undo で実装済みで、
本節はその意図を人間決定として確定・記録する。

| 項目 | 内容 |
| --- | --- |
| subject | auto-save の保留中編集があるときの Undo / Redo / 履歴ジャンプの意味 |
| 現挙動 | `MainWindowViewModel` の Undo / Redo / ジャンプの 3 経路が履歴再生の前に `FlushPendingEditsBeforeHistoryAsync` → `GridWorkspaceViewModel.FlushAllPendingEditsAsync` を呼ぶ。修正前は履歴再生が先で、続く再読込が選択を外した際に旧 Inspector の保留保存が発火し、Undo の上に新規コマンドが積まれて Redo が消え、Undo 後の値が編集中の値で上書きされた。 |
| provenance (as-built) | 修正前は競合による偶発挙動。修正後は意図した前処理。 |
| 選択肢 | **(a) flush-then-undo**: 保留編集を確定してから履歴再生 (Undo = 確定した直前の編集を取り消す。Redo 可) / (b) discard-then-undo: 保留編集を破棄してから Undo (保存済みの 1 つ前へ戻る) |
| 影響 | どちらも報告の再現テストを満たす。(b) は auto-save が保存するはずだった入力を黙って捨てる。(a) は編集を失わず Undo の意味と一致する。 |
| 推奨 / ★ 裁定 | ✅ **(a) flush-then-undo** (2026-10-02 ユーザー裁定) |
| 反映先 | GRID BOM の `deliberate_decisions` に `D-UNDO-PENDING` (anchor: `Audit_UndoMustNotSavePendingDraftAfterUndoAndDestroyRedo`)。実コード変更なし (実装済み)。 |

## C02 — 出力 (Preview / PNG) 時の未保存編集の扱い (2026-10-02 追補)
不具合調査報告 (2026-10-02) の C02 で表面化。実コード修正 (797f13c) は現状の挙動で実装済みで、本節はその意図を確定・記録する。

| 項目 | 内容 |
| --- | --- |
| subject | 出力時に未保存の編集 (draft) をどう扱うか。特に手動保存モード |
| 現挙動 | `GridOutputViewModel` の `RequestPreviewAsync` / `ExportToPngAsync` が出力前に保留保存を確定 (`FlushAllPendingEditsAsync`)。auto-save ON で保存失敗が残れば `Status_OutputAbortedSaveFailed` で中止。auto-save OFF は draft を保存せず DB の保存済み値で出力。 |
| 選択肢 | **(a) 現状**: 出力は保存済み値。auto-save ON は出力前に確定し、失敗なら中止 / (b) 手動保存モードでも出力前に自動保存 / (c) draft をそのままレンダラーへ渡す (画面と出力が一致) |
| 影響 | (b) は「Save するまで永続化しない」を崩し、意図しない履歴が積まれる。(c) はレンダラーが永続値以外を入力に持つ契約変更になり、出力内容が保存されないまま消える。(a) は手動保存モードで画面と出力がずれうるが、保存の意味を保つ。 |
| ★ 裁定 | ✅ **(a) 現状のまま確定** (2026-10-02 ユーザー裁定) |
| 反映先 | RENDERING BOM の `deliberate_decisions` に `D-OUTPUT-PENDING`。実コード変更なし。画面と出力のずれへの警告表示は別判断として `open_ux_note` に残す。 |

## D-SWITCH-PENDING — 手動保存モードの切替・終了時の未保存編集 (2026-10-02 追補)
ユーザビリティ評価 (2026-10-02) の優先 1 (P1)。手動保存で別配置を選んで戻ると、未保存の編集が無言で消えた。

| 項目 | 内容 |
| --- | --- |
| subject | 自動保存 OFF で、未保存の編集があるまま別の対象へ移る / 終了するときの扱い |
| 現挙動 (修正前) | `PlacementInspectorViewModel.cs:296-317` は自動保存 OFF のとき切替で保存せず DB 値へ巻き戻す。グリッド切替 (`GridCanvasListViewModel.cs:200-216`) も OFF では保存しない。一方 `09-settings.md` §9.26.3 は「切替で自動 flush」と説明しており、文書と実装が食い違っていた。 |
| 選択肢 | (a) 切替時に自動保存 / **(b) 保存・破棄・戻る を確認** / (c) 文書を実装に合わせる (無言の破棄は残る) |
| 影響 | (a) は手動保存の意味と履歴の粒度を崩す。(c) は喪失を残す。(b) は確認が 1 回増えるが、保存の意味を保って無言の喪失だけを塞ぐ。 |
| ★ 裁定 | ✅ **(b) 保存・破棄・戻るの選択** (2026-10-02 ユーザー裁定) |
| 範囲 | 配置切替・選択解除 / 候補切替・見出しクリック / グリッド切替 / アプリ終了。対象外: Undo/Redo (D-UNDO-PENDING)、出力 (D-OUTPUT-PENDING)、削除・履歴操作などプログラム起因の遷移。 |
| 反映先 | GRID BOM の `deliberate_decisions` に `D-SWITCH-PENDING`。`09-settings.md` §9.26.3 を新しい動作へ更新。実装・テスト済み。 |

## D01 — 候補ツリーで見出しを選んだときの対象 (2026-10-02 追補)
不具合調査報告 (2026-10-02) の D01 で表面化。実コード修正は下記の挙動で実装済み (Application 層 `GridWorkspaceViewModel` と Presentation の TreeView バインド)で、本節はその意図を確定・記録する。

| 項目 | 内容 |
| --- | --- |
| subject | 候補ツリーで画像グループの見出しを選んだとき、候補向けコマンド (削除・配置・複製) の対象をどうするか |
| 現挙動 | `GridWorkspaceViewModel.SelectedCandidateNode` が見出しと候補の両方を受け、見出しを選ぶと `SelectedCandidate=null`。候補向けコマンドは無効。再読込は見出し選択を奪わない。修正前は型変換の失敗で前の候補が対象のまま残り、削除が最後のバリアントなら画像本体まで消した。 |
| 選択肢 | **(a) 見出し選択で対象を空にする (コマンド無効)** / (b) 見出し選択でも直前の候補を保持する / (c) 見出し選択で先頭候補を自動選択する |
| 影響 | (b) は元の不具合そのもの (見た目の選択と操作対象がずれる)。(c) はユーザーが選んだ見出しを奪い、見出し選択を表現できない。(a) は選択と対象を一致させ、破壊的操作の安全側に倒れる。 |
| ★ 裁定 | ✅ **(a) 見出し選択で対象を空にする** (2026-10-02、推奨かつ現状の実装を確定) |
| 反映先 | GRID BOM の `deliberate_decisions` に `D-HEADER-SELECT` (anchor: `D01_*` 4 件)。実コード変更なし。 |

## C06 — crop 数値入力の空欄・0・範囲外の扱い (2026-10-02 追補)
不具合調査報告 (2026-10-02) の C06 で表面化。実コード修正は下記の挙動で実装済みで、本節はその意図を確定・記録する。

| 項目 | 内容 |
| --- | --- |
| subject | 手動 crop の数値入力 (X / Y / 幅 / 高さ) で、空欄・0・範囲外の値をどう扱うか |
| 現挙動 | `CopyPropertiesViewModel` の手動 crop 入力 (`NormalizeManualCropRect` ほか)。空欄は直前の有効値を維持 (保存しても crop は消えない)、0 は未確定 (永続化しない)、X / Y は size-1 にクランプ、原点移動で幅・高さを切り詰め、入力欄の有効無効は矩形の値に依存しない。修正前は幅を消す・0 にすると全欄が無効化されて入力を続けられず、範囲外の値でドラッグが例外になった。 |
| 選択肢 | **(a) 空欄=直前の有効値を維持 + 範囲はクランプ** / (b) 空欄=crop 削除 / (c) 範囲外は拒否して元の値へ戻す |
| 影響 | (b) は入力の途中や空欄中の auto-save で保存済みの crop を黙って消す。(c) は打鍵の途中の値を弾いて入力しにくい。(a) は入力が常に有効で、ドラッグが前提とする W <= 画像幅 を保つ。crop を消す操作は「手動」の OFF のまま。 |
| ★ 裁定 | ✅ **(a) 空欄は直前の有効値を維持 + クランプ** (2026-10-02、推奨かつ現状の実装を確定) |
| 反映先 | IMAGE_VARIANT BOM の `deliberate_decisions` に `D-CROP-INPUT` (anchor: `C06_*` 7 件)。実コード変更なし。 |

## D-LIVE-PREVIEW — 開きっぱなしのプレビューの自動更新 (2026-10-02 追補)
ユーザビリティ評価 (2026-10-02) の「見本を見ながら調整する」(文書は自動再生成を説明するが、実装はモーダルの固定スナップショット)。
新しい人間裁定ではなく、C02 の裁定 (D-OUTPUT-PENDING: 出力は保存済みの値) から導いた実装判断の記録。

| 項目 | 内容 |
| --- | --- |
| subject | プレビューを開いたまま編集できるようにし、どの内容の変化に合わせて、どの経路で更新するか |
| 現挙動 | モードレスの `PreviewWindow` が `GridOutputViewModel.StartLivePreview` に参加。保存済みの内容 (履歴の変化・候補ライブラリの変更・表示グリッドの切替) と出力オプションの変更で、300ms の静止後に最新の 1 回だけ、専用 DI スコープ (専用 DbContext) で再描画する。flush も IsBusy も使わない。 |
| 選択肢 | **(a) 保存済みの値だけで自動更新 (専用スコープ)** / (b) 編集のたびに draft ごと flush して更新 / (c) draft をレンダラーへ渡して更新 / (d) 文書を実装 (固定スナップショット) に合わせるだけ |
| 影響 | (b) は手動保存の意味と履歴の粒度を崩す。(c) は D-OUTPUT-PENDING が退けた契約変更。(d) は「見本を見ながら調整」の摩擦を残す。(a) は手動保存モードの draft が保存まで見えないが、契約は保たれる。共有 DbContext は並行操作に弱いので、描画は専用スコープに分ける。 |
| 反映先 | RENDERING BOM の `deliberate_decisions` に `D-LIVE-PREVIEW`。文書 `06-output.md` §6.18.2 (ja/en)。 |

## 実機確認 (Windows 実 GUI、2026-10-02)
不具合調査報告 (2026-10-02) とユーザビリティ評価 (2026-10-02) への対応のうち、Presentation 層の変更はテストプロジェクトが無く、
ビルド確認までだった。ユーザーが Windows 実機で確認し、**「正しく動作した」と報告した** (項目ごとの内訳は報告されていない)。

| 対象 | 内容 | 確認 |
| --- | --- | --- |
| プレビュー自動更新 (D-LIVE-PREVIEW) | モードレス表示、自動更新、再押下で前面化、主ウィンドウと一緒に閉じる、旧 Bitmap の遅延 Dispose | ✅ |
| 手動保存の切替確認 (D-SWITCH-PENDING) | 保存 / 破棄 / 戻る (配置・候補・グリッド切替、アプリ終了、ワークスペース切替) | ✅ |
| 見出し選択 (D-HEADER-SELECT) / crop 数値入力 (D-CROP-INPUT) | View 側の挙動 | ✅ |
| 削除確認・複製・候補バッジ・行列追加・キー操作・取り込み通知・案件名表示・欠落ワークスペースの案内 | ユーザビリティ評価の実装分 | ✅ |

確認の範囲は上記の一括報告で、どの項目をどの条件 (自動保存 ON/OFF など) で試したかの記録は無い。
不具合が見つかった場合は、再現手順と自動保存の設定を添えて新しい不具合として扱う。

## 実機確認 2 (Windows 実 GUI、修正後ソースレビュー R01〜R10・P3、2026-10-02)
修正後ソースレビュー (対象 ba5290b) への対応 (96e5d8f / 0bc6613 / 20d137b) のうち、Presentation 層と実機でしか確かめられない項目を、
ユーザーが Windows 実機で確認し、**「OK」と報告した** (項目ごとの内訳は報告されていない)。

| 対象 | 内容 | 確認 |
| --- | --- | --- |
| R02 | 閉じるボタン・「ファイル → 終了」で、手動保存の未保存編集を 保存 / 破棄 / 戻る で確認してから閉じる (画面のないプロセスが残らない) | ✅ |
| R03 | ワークスペース切替の前の保存待ち・保存失敗時の中止表示 | ✅ |
| R08 / R09 | 保護領域 65 件以上の表示、1px 幅 crop の画面と出力の幅 | ✅ |
| R10 | 閉じたプレビューのメモリ解放 (大きいキャンバスでの増減で確認) | ✅ |
| P3 | 一度も開いていないワークスペースの書き出しと復元 | ✅ |
| 矩形トリミング | 選択矩形の縁に沿って全幅に出る線 (マットを 4 枚の矩形で塗っていた継ぎ目) を、1 つの図形で塗る修正 | ✅ |

確認の範囲は上記の一括報告で、どの項目をどの条件 (自動保存 ON/OFF、表示倍率など) で試したかの記録は無い。
不具合が見つかった場合は、再現手順と自動保存の設定を添えて新しい不具合として扱う。

## 反映 (裁定後・実施済)
1. ✅ ユーザー裁定 (3 件とも推奨採用、2026-06-01)。
2. ✅ `deliberate_decisions` を該当 BOM へ反映: IMAGE_VARIANT=D2a/D2b、GRID=D-PV。
3. ✅ PV-2 anchor `ConflictingPlacementId_Is_First_In_Collection_Order_AsBuilt` のコメントを「caller 安定順 (PlacementOrder) が契約」へ更新 (挙動=不変、意図注記のみ)。d3 §7 の D2a/D2b/D-PV を裁定済へ。
4. → 次は (4) 工程管理層: この `deliberate_decisions` 群が「人間決定に分類する」入力になる。
