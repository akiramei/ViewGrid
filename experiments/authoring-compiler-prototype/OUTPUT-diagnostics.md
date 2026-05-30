# 診断レポート — 画像配置グリッド (GRID_COMPOSITION)

> 入力: `experiments/authoring-compiler-prototype/INPUT-human-requirements.md`
> 出力 BOM: `OUTPUT-bom-candidate.yaml`
>
> このレポートは、prose を Capability BOM へ lift する過程で AI 抽出器が検出した
> **欠落・矛盾・曖昧さ・実装者が勝手に決めそうな点** を網羅的に列挙する。
> severity:
> - **proposal-ERROR**: AI には決められない意味の欠落/矛盾。人間が決めないと実装に進めない。
> - **WARNING**: AI が妥当に決められるが記録すべき実装決定 (MUST_DECIDE 候補)。
> - **INFO**: 明確に抽出できた確定事項のうち補足したいもの。

---

## A. 失敗時の挙動・失敗理由 (FAIL)

### FAIL-001
- severity: proposal-ERROR
- message: 行数・列数が「1 以上の整数」でない場合に「どう失敗するか」(失敗理由の名前・粒度・行と列を別理由にするか) が未定義。
- source: "## やりたいこと > グリッドを作る: 「行数・列数は 1 以上の整数。」" — 制約はあるが違反時の挙動が無い。
- resolution: 0 や負・非整数を渡したときの失敗理由を定義し、行/列を別理由にするか単一理由にするかを決める。

### FAIL-002
- severity: proposal-ERROR
- message: 存在しないグリッド ID / 配置 ID を指定したときの失敗が prose に一切書かれていない (NotFound 系の失敗理由が欠落)。
- source: "「既存のグリッドに対して」「置いた画像を…」 — 既存前提のみで不在時の記述が無い。"
- resolution: GridNotFound / PlacementNotFound を canonical failure として採用するか、または前提保証 (呼び出し側が存在を保証) かを決める。

### FAIL-003
- severity: proposal-ERROR
- message: 「使える画像を 1 つ選び」とあるが、選べない画像 (存在しない・一覧に無い・権限外) を指定した場合の挙動が未定義。
- source: "## やりたいこと > 画像をマスに置く: 「使える画像を 1 つ選び」"
- resolution: 画像の選択可能性をこの capability が検証するのか、UI/呼び出し側の責務とするのかを決める。検証するなら失敗理由を定義する。

### FAIL-004
- severity: proposal-ERROR
- message: 「受け付けない」の具体的表現が未定義。例外を投げるのか、失敗結果値 (Result/エラー戻り) を返すのか、サイレントに無視するのかが決まっていない。
- source: "## 受け入れ条件: 「不正な配置 (はみ出し・重なり) は受け付けない。」"
- resolution: 失敗の返し方 (例外 / 結果値 / バリデーションエラー集約) を統一的に決める。これは全 command UC に波及する。

### FAIL-005
- severity: WARNING
- message: 重なり (Overlap) 失敗時に「どの配置と重なったか」を呼び出し側へ返すか (payload に競合相手 ID を含めるか) が未定義。実装者が独断で決めがち。
- source: "「すでに何かが置かれているマスと重なってはいけない」 — 競合相手の通知要否が無い。"
- resolution: Overlap の payload に conflictingPlacementId を含めるか否かを決める。BOM では proposal として含めている。

---

## B. Swap (入れ替え) の意味 (SWAP)

### SWAP-001
- severity: proposal-ERROR
- message: 占有サイズが異なる 2 配置の「位置の入れ替え」が何を意味するか不定義。位置 (左上座標) だけ交換するのか、サイズも含めて交換するのか。サイズが違うと交換後にはみ出し/重なりが起きうるが、その扱いが曖昧。
- source: "## やりたいこと > 2 つの配置を入れ替える: 「互いの位置を入れ替える。入れ替えた結果、どちらもグリッド内に収まり、かつ他と重ならないこと。」"
- resolution: swap が交換する属性 (位置のみ / 位置+サイズ) を明確化し、サイズ差で破綻するケースを失敗とするか禁止するかを決める。

### SWAP-002
- severity: proposal-ERROR
- message: swap の重なり判定で「入れ替える 2 つ自身を判定対象から除外するか」が未定義。Move では「自分自身を除く」(明示) のに、swap では同種のルールが書かれていない。除外しないと、入れ替え前に互いが隣接/重複していると常に失敗しうる。
- source: "Move 節「自分自身は重なり判定から除く」 vs Swap 節 (除外規定なし) の非対称。"
- resolution: swap 時に参加 2 配置を相互の重なり判定から除外するか (R-05) を人間が決める。Move (R-04) との整合を取る。

### SWAP-003
- severity: WARNING
- message: 同一の配置を 2 回指定 (A==B) した swap、または同じグリッドに属さない 2 配置の swap の扱いが未定義。
- source: "「2 つの配置を選んで」 — 2 つが相異なる/同一グリッド所属である保証が無い。"
- resolution: A==B を no-op とするかエラーとするか、別グリッド間 swap を禁止するかを決める。

---

## C. 重なり順 (Stacking Order) (ORDER)

### ORDER-001
- severity: proposal-ERROR
- message: 「1 つ前へ / 1 つ後ろへ」を端 (既に最前面 / 最背面) で実行したときの挙動が未定義 (no-op か、エラーか)。「最前面へ / 最背面へ」も既に端にある場合の扱いが不明。
- source: "## やりたいこと > 重なり順を変える: 「最前面に持ってくる/最背面に送る/1 つ前へ/1 つ後ろへ」"
- resolution: 端での順序変更操作を no-op とするかエラーとするかを決める。

### ORDER-002
- severity: proposal-ERROR
- message: 「順番を表す値」の表現が未定義。整数の連番か、一意か、連続か、重複を許すか、初期値 (新規配置時の順番) は何かが決まっていない。
- source: "「各配置には『順番』を表す値が付く」 — 値の型・性質・初期化が無い。"
- resolution: stackingOrder の型・一意性・連続性・配置作成時の初期順番を定義する。

### ORDER-003
- severity: WARNING
- message: 配置を削除 (UC-06) して順番に欠番が生じたとき、残りを詰め直すか欠番のまま残すかが未定義。
- source: "## やりたいこと > 配置を消す (削除の副作用が順番に与える影響が無い)。"
- resolution: 削除後の順番の正規化 (詰め直し) 有無を決める。

---

## D. 配置・サイズ・座標の境界条件 (SIZE / BOUND)

### SIZE-001
- severity: proposal-ERROR
- message: 占有サイズ (幅×高さ) の下限が未定義。0 や負のサイズを拒否すべきだが prose に規定が無い。
- source: "## やりたいこと > 画像をマスに置く: 「占有する大きさ (幅×高さ、マス単位) を指定」"
- resolution: width>=1, height>=1 を不変条件 (R-07) として確定するか、別の下限を決める。

### BOUND-001
- severity: WARNING
- message: 「はみ出し」の判定基準が曖昧。位置 (row,col) + サイズ (w,h) がグリッド (rows,cols) を超えない条件 (例: row+h <= rows かつ col+w <= cols) を実装者が独自に解釈する余地がある。座標が幅×高さなのか高さ×幅なのか (rows が縦か) の対応も明示されていない。
- source: "「外にはみ出してはいけない」「左上を 0,0 とする座標」「占有する大きさ (幅×高さ)」"
- resolution: 座標軸 (row=縦/col=横) と幅/高さの対応、はみ出し境界式を明記する。

### BOUND-002
- severity: WARNING
- message: 「重なり」の定義 (セル単位の集合が交差したら重なり、と解釈) が暗黙。隣接 (辺を共有) は重なりでない、という前提も明示されていない。
- source: "「すでに何かが置かれているマスと重なってはいけない」"
- resolution: 重なりをセル占有集合の交差で定義することを確認し、隣接が許容であることを明記する。

---

## E. 決定の所在 (Decision Ownership) (DEC)

### DEC-001
- severity: proposal-ERROR
- message: ドメイン不変条件 (はみ出し禁止・重なり禁止・1 以上) を「どの層が所有して拒否するか」が未定義。ドメインに置くのが妥当だが prose は決めていない。
- source: "## 受け入れ条件: 「不正な配置は受け付けない」 — 拒否の所在が無い。"
- resolution: バリデーション拒否の所有層 (domain か application か) を決める。

### DEC-002
- severity: proposal-ERROR
- message: 「受け付けない」のがドメインなのか UI 入力時のガードなのかが不明。両方ありえるが、信頼できる検証点が決まっていないと UI を信頼した抜け道が生じうる。
- source: "## 受け入れ条件 + ## 使う人 (配置画面でのドラッグ操作) の併記。"
- resolution: 検証の権威点 (UI は補助、ドメインが最終) という方針を確定する。

### DEC-003
- severity: WARNING
- message: 操作の原子性・ワークフロー決定が未定義。例: swap が片側だけ成功して片側失敗したときロールバックするか。
- source: "## やりたいこと > 2 つの配置を入れ替える (途中失敗時の扱いが無い)。"
- resolution: 各 command を全成功か全失敗 (原子的) とする方針を確定する。

### DEC-005
- severity: proposal-ERROR
- message: 永続化・保存・同時編集 (複数編集者) の決定が完全に欠落。グリッド/配置がどこに保存され、誰がいつ保存するかが不明。
- source: "prose 全体に永続化への言及なし。"
- resolution: 永続化の有無・保存タイミング・同時編集ポリシーを決める (この capability の責務範囲かどうかを含む)。

### DEC-006
- severity: WARNING
- message: rendering (描画) の責務境界が曖昧。「見た目はデザイナーに任せる」とあるが、この capability がセル座標→ピクセルの算出を持つのか、純粋にデータだけ持つのかが不明。
- source: "## 使う人: 「レイアウト (見た目) の細部はデザイナーに任せる」"
- resolution: この capability の出力がデータ (位置/サイズ/順番) までで、描画は別責務、という境界を明文化する。

### DEC-007
- severity: WARNING
- message: 編集履歴・undo/redo の決定が未定義。シナリオの「気に入る配置になるまで動かしたり入れ替えたり」は試行錯誤=undo を示唆するが断定不可。
- source: "## 使う人と典型シナリオ: 「気に入る配置になるまで動かしたり入れ替えたりする。」"
- resolution: undo/redo・履歴を範囲に含めるかを決める。含めるなら別途要求が必要。

---

## F. UI / 操作の対応 (UI)

### UI-001
- severity: WARNING
- message: 配置画面のドラッグ操作と、ドメイン操作 (PlaceImage / MovePlacement) の対応が暗黙。「ドラッグして置く」=PlaceImage、「ドラッグで動かす」=MovePlacement と推論したが、ドラッグ中のプレビュー・スナップ・キャンセルの扱いは未定義。
- source: "## 使う人: 「画像をマスにドラッグして置く。置いたものはドラッグで動かせる。」"
- resolution: ドラッグ↔ドメイン操作の対応と、ドラッグ中の中間状態 (確定前) の扱いを UI 仕様として決める。

### UI-002
- severity: WARNING
- message: swap / 重なり順変更 / 削除を配置画面でどう起動するか (ジェスチャ/メニュー) が未定義。ドラッグで明示されているのは place/move のみ。
- source: "## 使う人 (place/move のドラッグのみ言及) vs ## やりたいこと (swap/order/delete も存在)。"
- resolution: 各操作の UI トリガを定義する (この BOM の責務外でも記録が必要)。

---

## G. メタデータ・モデル (MD / EVT)

### MD-001
- severity: WARNING
- message: Grid と Placement の識別子 (id) の存在・型が prose に無い。操作は対象を特定する必要があるため id を inferred で導入したが、人間の確認が必要。
- source: "## 用語 (id への言及なし) vs 操作群 (対象特定が必須)。"
- resolution: Grid/Placement に一意 id を持たせることを確定し、Image の参照キー (imageId) の形式を決める。

### MD-002
- severity: INFO
- message: Image は「別仕組みで作られた派生物・中身を編集しない」ため、この capability の **referenced (外部参照)** エンティティとして lift した。owned ではない。
- source: "## 背景 / ## 用語: 「別の仕組みで作られた画像派生物」「中身そのものは編集しない」"
- resolution: 追加対応不要 (確定事項の記録)。境界 excluded: ImageContentEditing も参照。

### MD-003
- severity: INFO
- message: Grid の name は作成時入力として確定 (human-confirmed)。ただし name の一意性・長さ制約・空文字許容は未定義。
- source: "## やりたいこと > グリッドを作る: 「名前と、行数・列数を指定して」"
- resolution: name に制約が必要なら定義する (任意; 現状は制約なしと解釈)。

### EVT-001
- severity: WARNING
- message: ドメインイベント (配置された/動かされた等の通知) が prose に無いため events を空にした。通知・連携が必要かは決定の所在が不明。
- source: "prose にイベント/通知への言及なし。"
- resolution: 他システムへの通知が要るかを決める。要らなければ events 空のままで確定。

---

## H. 受け入れ条件のテスト可能性 (AC)

### AC-001
- severity: WARNING
- message: 受け入れ条件「シナリオ 1〜5 が一通り行える」は正常系のみで、失敗系 (はみ出し/重なり/不在) の期待結果 (どう拒否されるか) を検証する受け入れ条件が無い。FAIL-001〜005 が解決しないとテストの期待値が書けない。
- source: "## 受け入れ条件: 「シナリオ 1〜5 が一通り行える」「不正な配置は受け付けない」"
- resolution: 失敗系の期待挙動 (FAIL 系) を確定してから、それを検証する受け入れ条件を追加する。

---

## 確定できた事項 (INFO まとめ)

### INFO-OK-001
- severity: INFO
- message: 6 つの UseCase (CreateGrid / PlaceImage / MovePlacement / SwapPlacements / ChangeStackingOrder / DeletePlacement) はすべて prose に明示され、human-confirmed で lift できた。
- source: "## やりたいこと (操作) の 6 小節。"
- resolution: 追加対応不要。

### INFO-OK-002
- severity: INFO
- message: Move の「自分自身を重なり判定から除く」は prose に明示されており、R-04 として human-confirmed で確定。これが Swap (SWAP-002) との非対称を際立たせる根拠になっている。
- source: "## やりたいこと > 配置を動かす: 「動かしている自分自身は重なり判定から除く」"
- resolution: 追加対応不要 (ただし Swap 側の対称ルールは未確定; SWAP-002 参照)。
