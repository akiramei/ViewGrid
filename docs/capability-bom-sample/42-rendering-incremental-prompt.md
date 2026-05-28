# 42 — Incremental 追加プロンプト (RENDERING_EXPORT を n=2 実装に追加、n=3 検証)

> **Version: v0.1** (候補 E ステップ 3 / n=3 スケール検証)
> committed 済みの n=2 実装 (`experiments/phase2-cocompose-impl/`) を **既存の契約準拠コードベース** とみなし、
> その上に RENDERING_EXPORT を **1 Capability だけ Incremental 追加** する。
> 検証する命題: **契約 (n=2 で 0 アダプタ) は、消費側 Capability を 1 つ足したときも 0 アダプタでスケールするか。**

## A. 完全版プロンプト (subagent に渡す)

```text
あなたは Capability BOM Audit 方法論に従って動作するソフトウェア実装者である。
既存の契約準拠コードベース (GRID_COMPOSITION + IMAGE_VARIANT_MANAGEMENT) に、
RENDERING_EXPORT を Incremental に追加し、3 Capability を 0 アダプタで結線する。

== INPUT DOCUMENTS (これら *のみ* を正準入力とする) ==
横断契約 (v0.2、最優先・変更不可):
  0. docs/capability-bom-sample/00-convention-contract.md   (特に §1.8 C-CONSUMER-PORTS)
追加する Capability (RENDERING_EXPORT, focused):
  1. docs/capability-bom-sample/rendering-export/10-requirements.md
  2. docs/capability-bom-sample/rendering-export/20-capability-bom.md
  3. docs/capability-bom-sample/rendering-export/21-rendering-export.yaml
  4. docs/capability-bom-sample/rendering-export/30-design.md
既存コードベース (read してよい。これを土台に拡張する):
  5. experiments/phase2-cocompose-impl/   (committed 済みの n=2 実装。これが「既存の契約準拠コード」)

YAML と Markdown が矛盾する場合は YAML が正。矛盾は実装ノートに明示すること。

== 厳守: 参照禁止 (科学的統制) ==
以下を *絶対に読まない* こと (読むと本実験が無効になる):
  - src/ , tests/ , tools/ (ViewGrid 既存実装)
  - experiments/phase2-impl/ , experiments/phase2-v02-impl/ ,
    experiments/phase2-image-variant-impl/ , experiments/phase2-composition-test/
読んでよいのは上記 INPUT DOCUMENTS と experiments/phase2-cocompose-impl/ のみ。

== 出力先 ==
experiments/phase2-n3-incremental-impl/ に出力する。
手順:
  1. experiments/phase2-cocompose-impl/ の中身 (src/ tests/ compose.py conftest 等) を
     experiments/phase2-n3-incremental-impl/ に *そのままコピー* する (これが既存コードベース)。
  2. その上に RENDERING_EXPORT を追加する (下記)。
  3. 既存の GRID/IMGVAR のテストは *壊さない*。RENDERING 追加後に全テストが green であること。

== GOAL ==
RENDERING_EXPORT (UC-01..UC-03, R-01..R-04) を実装し、GRID/IMGVAR を read で結線する。
最重要の成功条件 (00-convention-contract.md §4.2):
  - RENDERING ↔ GRID / RENDERING ↔ IMGVAR の結線に *手書きアダプタ 0 行*
  - RENDERING は GRID の Placement / IMGVAR の ImageCopy を *import しない* (shared 中立 DTO のみ)
  - producer (GRID/IMGVAR) に足す結線コードは *native projection のみ* (standalone アダプタ 0)
  - committed n=2 の全テストが green のまま (RENDERING 追加で壊れない)
  - R-08 (ManualCropOverridesAutoCrop) を RENDERING が適用 (manual 優先)

== CODEBASE_CONVENTION_CONTRACT (横断規約契約 v0.2 — 変更不可) ==
00-convention-contract.md に逐一従う。特に §1.8 C-CONSUMER-PORTS:

  新規追加するファイル:
    - src/shared/render_contracts.py: 中立 DTO PlacementView / GridLayout / CopyRenderSpec
      (producer の enum/domain を持ち込まない。rotation/scaling_mode/alignment は str)
    - src/shared/ports.py に追記: GridLayoutPort / CopyRenderSpecPort (Protocol)

  producer 側 (既存 UseCases に native projection を追加。これは許可。アダプタは禁止):
    - GridCompositionUseCases に get_grid_layout(grid_id: uuid.UUID) -> GridLayout | None
      (GridCanvas + list_placements を GridLayout/PlacementView に写像。
       grid 不在は None。※ 既存に grid 取得の内部手段が無ければ最小限の getter を足してよい)
    - ImageVariantManagementUseCases に get_copy_render_spec(copy_id: uuid.UUID) -> CopyRenderSpec | None
      (ImageCopy を CopyRenderSpec に写像。enum は .value で str 化。copy 不在は None)

  consumer 側 (新規 Capability):
    - src/rendering_export/ : RenderingExportUseCases (C-UC-CONTAINER 命名) ほか
    - GridLayoutPort / CopyRenderSpecPort に依存して描画モデルを構築
    - R-08 適用点は UC-02 ResolveEffectiveCrop (manual 優先 → auto → none)
    - identity=uuid.UUID / Result=Ok,Err / not-found=None / timestamp=UTC / enum=enum.Enum (契約踏襲)

== NON-GOALS (禁止) ==
- 上記「参照禁止」の違反
- RENDERING が Placement / ImageCopy を変更すること (read 専用)
- RENDERING が GRID R-01/R-02 を再判定、crop 値 (R-06/R-07) を再検証すること
- RENDERING が grid_composition / image_variant_management の domain を import すること
- 境界に standalone アダプタクラスを書くこと
  (もし契約だけで結線できない箇所があれば、アダプタを書かず unclear として報告)
- 既存 n=2 の GRID/IMGVAR の Rule / UseCase / 失敗理由名を変更すること
  (projection メソッドの *追加* は可。既存の意味の変更は不可)

== MUST_DECIDE_AND_DOCUMENT (RENDERING 固有のみ) ==
- ピクセル丸め方針 (floor 等) / RenderDescriptor の dict スキーマ詳細 / EffectiveCrop の表現型
※ 契約が固定した項目はここに書かない。

== OUTPUT FORMAT ==
1. experiments/phase2-n3-incremental-impl/ : n=2 をコピー + 下記追加
   - src/shared/render_contracts.py (新規)
   - src/shared/ports.py (GridLayoutPort/CopyRenderSpecPort 追記)
   - src/grid_composition/use_cases.py (get_grid_layout 追加 = native projection)
   - src/image_variant_management/use_cases.py (get_copy_render_spec 追加 = native projection)
   - src/rendering_export/ (RenderingExportUseCases, domain=RenderModel/RenderItem/EffectiveCrop, events)
2. テスト (既存 n=2 テストはコピーしてそのまま green に保つ + 下記追加)
   - RENDERING の Rule 単体 / UC happy・failure / Event / AT-01..AT-08 / 1000-step random walk
   - 境界 import チェック: rendering_export が grid_composition/image_variant_management を import して
     いないことを検査するテスト (例: ソースを読んで `import grid_composition` 不在を assert)
   - render 統合テスト: GRID に copy を配置 → RenderingExportUseCases.build_render_model(grid_id) が
     z 順で items を返し、manual/auto/none の crop が R-08 通り解決される
3. compose 拡張: experiments/phase2-n3-incremental-impl/compose.py を更新し、3 Capability を結線:
     grid = GridCompositionUseCases(..., image_copy_existence=imgvar)
     render = RenderingExportUseCases(grid_layout=grid, copy_render_spec=imgvar)   # *アダプタなし*
   存在する copy を配置 → render モデルを print。アダプタ行数 0 を表示。
4. IMPLEMENTATION_NOTES_N3.md (新規、既存ノートは上書きしない)
   - RENDERING の Decision ownership 自己監査
   - *アダプタ行数の自己申告* (目標 0)
   - *RENDERING の domain 非結合の自己申告* (grid_composition/image_variant_management を import していない)
   - *producer に足したものの種別* (get_grid_layout / get_copy_render_spec が native projection であり
     standalone アダプタでないこと。既存 Rule/UC/失敗理由を変更していないこと)
   - *既存 n=2 テストの非回帰* (コピーした既存テストが全 green)
   - R-08 を UC-02 でどう適用したか (manual 優先)
   - RENDERING 固有 MUST_DECIDE_AND_DOCUMENT (>=3)
   - unclear / suspected_overreach
5. README_N3.md (ビルド/テスト実行方法)

== CONFIDENCE POLICY ==
- 契約 (00) と RENDERING BOM (20/21) が矛盾したら実装を止めて unclear に記録
- 契約だけで結線できない箇所は *アダプタで埋めず* unclear として報告 (契約の不備の発見が価値)
- 「綺麗にする」最適化はしない。manual と auto の合成のような独自解釈をしない (R-02 厳守)

== POST-IMPLEMENTATION SELF-AUDIT ==
1. RENDERING の各 Rule (R-01..R-04) が 1 箇所で保証されているか
2. RENDERING が GRID/IMGVAR の domain 型を import していないか (grep で確認)
3. 境界結線のアダプタ行数 (0 を目標、実数を報告)
4. producer 追加が native projection のみか (standalone アダプタ 0、既存意味の不変更)
5. 既存 n=2 テストが全 green か (コピー後に実行)
6. RENDERING の AT-01..AT-08 が全パスか
7. render 統合テスト + 1000-step random walk がパスか
8. R-08 が UC-02 で適用され、AT-02 (manual+auto→manual) がパスするか

最後に pytest を実行し全テスト合格を確認してから完了報告。
報告には「総テスト数/合格数」「うち既存 n=2 由来のテスト数と合否」「consumer 結線アダプタ行数」
「producer 追加メソッドの一覧と native/adapter の別」「RENDERING の domain import 有無」
「契約未充足項目 (あれば)」を必ず含めること。docs の要約は不要、実験結果のみ報告せよ。
```

## B. n=2 (ステップ 2) との違い

| 観点 | n=2 (Addendum F) | n=3 (本プロンプト) |
| --- | --- | --- |
| 生成方法 | 2 Capability を白紙から同時生成 | 既存 n=2 に RENDERING を **Incremental 追加** |
| 既存コード参照 | 全面禁止 | **phase2-cocompose-impl は read 可** (土台) |
| 境界の向き | producer→consumer 1 本 (exists) | consumer が 2 producer を read (2 本) |
| 新たな問い | アダプタ 0 で compose 可か | **producer retrofit が native projection で済むか / 既存非回帰 / R-08 適用** |

## C. 関連
- 契約 v0.2: `00-convention-contract.md` (§1.8 C-CONSUMER-PORTS)
- RENDERING サンプル: `rendering-export/`
- n=2 実装: `../../experiments/phase2-cocompose-impl/`
- 評価記録: `90-feasibility-notes.md` Addendum G
