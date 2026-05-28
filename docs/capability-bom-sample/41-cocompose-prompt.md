# 41 — 同時生成プロンプト (GRID_COMPOSITION × IMAGE_VARIANT_MANAGEMENT、契約下)

> **Version: v0.1** (ステップ 2 本体用)
> 既存 `40-ai-implementation-prompt.md` (GRID) と `image-variant-management/40-ai-implementation-prompt.md` (IMAGE_VARIANT)
> を **1 セッションで同時生成** するために統合し、`00-convention-contract.md` を横断拘束として追加したもの。

## このプロンプトの位置づけ

これまでの Phase 2 は **単一 Capability を単独生成** していた。本プロンプトは
**2 Capability を 1 つのコードベースに同時生成** し、`00-convention-contract.md` の下で
**アダプタ 0 行で境界結線できるか** を実証する (= 候補 E ステップ 2 本体)。

---

## A. 完全版プロンプト (subagent に渡す)

```text
あなたは Capability BOM Audit 方法論に従って動作するソフトウェア実装者である。
本タスクでは GRID_COMPOSITION と IMAGE_VARIANT_MANAGEMENT の 2 つの Capability を
1 つのコードベースに *同時生成* し、両者を共有契約の下で結線する。

== INPUT DOCUMENTS (これら *のみ* を正準入力とする) ==
横断契約 (最優先・変更不可):
  0. docs/capability-bom-sample/00-convention-contract.md

GRID_COMPOSITION:
  1. docs/capability-bom-sample/10-requirements.md
  2. docs/capability-bom-sample/20-capability-bom.md
  3. docs/capability-bom-sample/21-grid-composition.yaml
  4. docs/capability-bom-sample/30-design.md

IMAGE_VARIANT_MANAGEMENT:
  5. docs/capability-bom-sample/image-variant-management/10-requirements.md
  6. docs/capability-bom-sample/image-variant-management/20-capability-bom.md
  7. docs/capability-bom-sample/image-variant-management/21-image-variant-management.yaml
  8. docs/capability-bom-sample/image-variant-management/30-design.md

YAML と Markdown が矛盾する場合は YAML が正。矛盾は実装ノートに明示すること。

== 厳守: 参照禁止 (科学的統制) ==
以下を *絶対に読まない* こと (読むと本実験が無効になる):
  - src/ , tests/ , tools/ (ViewGrid 既存実装)
  - experiments/phase2-impl/ , experiments/phase2-v02-impl/ ,
    experiments/phase2-image-variant-impl/ , experiments/phase2-composition-test/
    (過去の Phase 2 生成物。コピーは禁止。白紙から書くこと)
本サンプル成果物 (上記 INPUT DOCUMENTS) だけを根拠に、白紙から実装する。

== GOAL ==
両 Capability の全 UseCase / Rule / Event を、各 30-design.md の指定どおり実装し、
00-convention-contract.md の下で両者を *アダプタ 0 行で* 結線する。

  GRID:        UC-01..UC-11, R-01..R-09, 全 Event
  IMAGE_VARIANT: UC-01..UC-17, R-01..R-11 (R-08 は宣言のみ), 全 Event

最重要の成功条件:
  - GRID UC-05 (PlaceImageCopy) の ImageCopy 存在確認が、IMAGE_VARIANT を経由して
    *手書きアダプタなし* で動く (00-convention-contract.md §1 C-BOUNDARY-IFACE / §4)
  - shared/value_objects.py の OccupySize が両 Capability で *同一型* (is 比較で True)

== SCOPE ==
- 対象: GRID_COMPOSITION + IMAGE_VARIANT_MANAGEMENT の 2 Capability
- 他 Capability (HISTORY / RENDERING / WORKSPACE) は最小スタブで可
- UI / PNG 出力 / ProtectedRegion は対象外

== CODEBASE_CONVENTION_CONTRACT (横断規約契約 — 全 Capability 共通、変更不可) ==
00-convention-contract.md に *逐一* 従うこと。以下は FORBIDDEN 相当の拘束であり、
AI が独自決定してはならない (MUST_DECIDE_AND_DOCUMENT ではない):

  - identity は uuid.UUID で表現する。str に変換・保持しない (C-IDENTITY)
  - OccupySize / PixelSize は src/shared/value_objects.py に 1 定義し、両 Capability が import する。
    局所複製・再定義は禁止 (C-SHARED-PLACEMENT)
  - OccupySize/PixelSize は frozen dataclass、bool を int として拒否、値 >= 1 (C-VALUE-SEMANTICS)
  - Result は src/shared/result.py の Ok / Err を両 Capability が import する。
    Failure 等の別名を作らない (C-RESULT)
  - モジュールレイアウトは src/ layout。shared / grid_composition / image_variant_management (C-LAYOUT)
  - UseCase コンテナは GridCompositionUseCases / ImageVariantManagementUseCases と命名する (C-UC-CONTAINER)
  - 境界の存在確認は src/shared/ports.py の ImageCopyExistencePort
    (exists(copy_id: uuid.UUID) -> bool、Result でラップしない) を両側が共有する (C-BOUNDARY-IFACE)
  - 横断 MUST_DECIDE の固定値 (C-TIMESTAMP=UTC tz-aware / C-REPO-NOTFOUND=None /
    C-ENUM=enum.Enum / C-EVENTBUS=synchronous) に従う

== NON-GOALS (禁止) ==
- 既存実装・過去 experiments の参照 (上記「参照禁止」)
- Rule ID / UseCase ID / Event 名 / 失敗理由名の変更・追加
- Decision ownership 違反 (特に IMAGE_VARIANT の UC-02 に cascade 削除を持たせない)
- R-08 (ManualCropOverridesAutoCrop) を IMAGE_VARIANT で適用すること (宣言のみ)
- 契約に反する物理表現 (str identity / 型の二重定義 / Result ラップした境界 等)
- 境界に手書きアダプタを足して契約の不備を隠すこと
  (もし契約だけでは結線できない箇所があれば、アダプタを書かず unclear として実装ノートに報告)

== ALLOWED (AI が自由、報告不要) ==
- 言語 (ただし契約の型語彙が自然に表せるもの。Python 3.11+ を推奨)
- 各 Capability 内部のクラス分割・命名 (契約と用語集に反しない限り)
- 画像 decoder (PIL 等。テストで mock 可能なこと)
- ロギング、内部最適化

== MUST_DECIDE_AND_DOCUMENT (AI が決めてよいが実装ノートに明示) ==
契約が固定していない、かつ Capability 固有の決定のみ。例:
  - 画像 decoder の選定 / hash 計算の実装 / ImageBlobStorage スタブ方針
  - AutoCropSettings / ManualCropFraction の集約値オブジェクト型
  - 各 Capability 内部の Repository スタブ実装
※ 契約 (§1, §2) が固定した項目はここに書かない (= 独自決定不可)。

== OUTPUT FORMAT ==
出力先ディレクトリ: experiments/phase2-cocompose-impl/

1. ソースコード (src/ layout)
   - src/shared/value_objects.py (OccupySize, PixelSize)
   - src/shared/result.py (Ok, Err)
   - src/shared/ports.py (ImageCopyExistencePort)
   - src/grid_composition/ (Domain, UseCases=GridCompositionUseCases, Repo スタブ, Event)
   - src/image_variant_management/ (Domain, UseCases=ImageVariantManagementUseCases,
     Repo/Blob スタブ, Event)

2. テストコード
   - 各 Capability の 30-design.md §6.1 必須テストカテゴリを網羅
   - Anchor tests AT-01..AT-10 を両 Capability 分、test_at_01_* 形式で実装
   - 1000-step random walk (両 Capability、property-based、必須)
   - *compose 統合テスト* (最重要):
       test_compose_place_existing_copy_succeeds:
         IMAGE_VARIANT で ImageCopy を作成 → GRID UC-05 がその CopyId を配置成功
       test_compose_place_unknown_copy_returns_unknown_copy_id:
         未作成の CopyId を GRID UC-05 に渡すと UnknownCopyId
       これらを *手書きアダプタなし* (ImageVariantManagementUseCases を
       ImageCopyExistencePort としてそのまま GRID に渡す) で書くこと

3. compose.py (experiments/phase2-cocompose-impl/compose.py)
   - 両 UseCases を生成し、ImageVariantManagementUseCases を GRID に Port として注入
   - 「存在する copy を配置成功 / 不在は UnknownCopyId」を print で示す動作デモ
   - アダプタクラスを *書かない*

4. 実装ノート (experiments/phase2-cocompose-impl/IMPLEMENTATION_NOTES.md)
   - Decision ownership 自己監査 (両 Capability)
   - unclear / suspected_overreach
   - MUST_DECIDE_AND_DOCUMENT (Capability 固有のみ、各 >= 3 件)
   - *契約遵守の自己申告*: C-IDENTITY..C-EVENTBUS の各項目を満たしたか、満たせなかった項目は理由
   - *アダプタ行数の自己申告*: 境界結線に書いたアダプタ行数 (目標 0)。0 でない場合は何が契約で
     不足していたかを具体的に記述
   - Anchor tests 合格状況 (両 Capability)

5. README.md (experiments/phase2-cocompose-impl/README.md)
   - ビルド / テスト実行方法、言語選定理由

== CONFIDENCE POLICY ==
- 契約 (00) と Capability BOM (20/21) が矛盾したら実装を止めて unclear に記録 (契約が物理、BOM が意味)
- 契約だけでは境界が結線できない箇所は *アダプタで埋めず* unclear として報告する
  (これは契約の不備の発見であり、本実験の価値ある出力)
- 推測で進めない。「綺麗にする」最適化はしない

== POST-IMPLEMENTATION SELF-AUDIT ==
1. 両 Capability の各 Rule の保証コードが 1 箇所か (GRID UC-07 post-swap check は例外)
2. IMAGE_VARIANT の R-08 が適用されていないか (共存のみ)、UC-02 が cascade を持たないか
3. shared/value_objects.py の OccupySize が両 Capability で同一型か (is 比較を 1 テストで確認)
4. 境界結線のアダプタ行数 (0 を目標、実数を報告)
5. Anchor tests AT-01..AT-10 (両 Capability) 全パスか
6. compose 統合テスト 2 件がパスするか
7. 契約 C-IDENTITY..C-EVENTBUS の遵守を 1 項目ずつ確認

最後に pytest を実行し、全テストの合格を確認してから完了報告すること。
報告には「総テスト数 / 合格数」「アダプタ行数」「契約未充足項目 (あれば)」を必ず含める。
```

---

## B. このプロンプトが step 1 と違う点

| 観点 | step 1 (Addendum E) | step 2 (本プロンプト) |
| --- | --- | --- |
| 生成方法 | 2 実装を *独立生成* し事後合成 | *同時生成* (最初から共有契約下) |
| 契約 | なし (各自由裁量) | 00-convention-contract.md を横断拘束 |
| 境界 | 事後にアダプタ手書き (必須だった) | Port 共有でアダプタ 0 行を目指す |
| 成功判定 | coexist 可 / compose 不可 を観測 | *アダプタ 0 行で compose 可* を実証 |

---

## C. 関連

- 契約: `00-convention-contract.md`
- 方法論側規範: `../methodology-extensions/21-codebase-convention-contract.md`
- step 1: `../../experiments/phase2-composition-test/`
- 評価記録: `90-feasibility-notes.md` Addendum F (本試行の結果)
