# 43 — 前倒しプロンプト (read ポートを最初から組み込んだ n=2 再生成 → n=3 producer-free 追加)

> **Version: v0.1** (候補 E ステップ 4 / read ポート前倒し検証、Addendum H)
> 契約 v0.3 (C-CONSUMER-PORTS を「最初から必須」に格上げ) の下で **n=2 を再生成** し、
> その上に RENDERING を Incremental 追加して **producer retrofit が 0 になるか** を検証する。

## このプロンプトの位置づけ

Addendum G の教訓「契約は read 境界を最初から織り込むべき」を実装で確かめる。

- **Phase A**: 契約 v0.3 の下で n=2 (GRID + IMGVAR) を **白紙から再生成**。
  read ポート (`GridLayoutPort` / `CopyRenderSpecPort`)・中立 DTO・producer の projection
  (`get_grid_layout` / `get_copy_render_spec`) を **consumer 不在でも最初から** 組み込む。
- **Phase B**: Phase A の成果物を **verbatim コピー** し、RENDERING_EXPORT を **追加するだけ**。
  producer (GRID/IMGVAR) と shared には **一切手を触れない**。
- **検証**: Phase A と Phase B で producer + shared が **byte-identical (diff 0 = retrofit 0)** か。

## A. 完全版プロンプト (subagent に渡す)

```text
あなたは Capability BOM Audit 方法論に従って動作するソフトウェア実装者である。
契約 v0.3 (read ポートを最初から必須化) の下で 2 段階の作業を行う:
  Phase A: n=2 (GRID + IMGVAR) を read ポート同梱で *白紙から* 生成
  Phase B: その n=2 をコピーし、RENDERING_EXPORT を *producer を一切触らず* に追加
最終目的: 「read ポートを前倒しすれば n=3 で producer retrofit が 0 になる」かを実証する。

== INPUT DOCUMENTS (これら *のみ* を正準入力とする) ==
横断契約 (v0.3、最優先・変更不可):
  0. docs/capability-bom-audit/samples/00-convention-contract.md
     (特に §1.8 C-CONSUMER-PORTS = 最初から必須 / §1.9 C-IDENTITY-BOUNDARY / §4.3)
GRID_COMPOSITION:
  1. docs/capability-bom-audit/samples/grid-composition/10-requirements.md
  2. docs/capability-bom-audit/samples/grid-composition/20-capability-bom.md
  3. docs/capability-bom-audit/samples/grid-composition/21-grid-composition.yaml
  4. docs/capability-bom-audit/samples/grid-composition/30-design.md
IMAGE_VARIANT_MANAGEMENT:
  5. docs/capability-bom-audit/samples/image-variant-management/10-requirements.md
  6. docs/capability-bom-audit/samples/image-variant-management/20-capability-bom.md
  7. docs/capability-bom-audit/samples/image-variant-management/21-image-variant-management.yaml
  8. docs/capability-bom-audit/samples/image-variant-management/30-design.md
RENDERING_EXPORT (focused、Phase B で実装):
  9. docs/capability-bom-audit/samples/rendering-export/10-requirements.md
 10. docs/capability-bom-audit/samples/rendering-export/20-capability-bom.md
 11. docs/capability-bom-audit/samples/rendering-export/21-rendering-export.yaml
 12. docs/capability-bom-audit/samples/rendering-export/30-design.md

YAML と Markdown が矛盾する場合は YAML が正。矛盾は実装ノートに明示。

== 厳守: 参照禁止 (科学的統制) ==
以下を *絶対に読まない / コピーしない* (読むと本実験が無効になる):
  - src/ , tests/ , tools/ (ViewGrid 既存実装)
  - experiments/ 配下の *既存の* 生成物すべて
    (phase2-impl / phase2-v02-impl / phase2-image-variant-impl /
     phase2-composition-test / phase2-cocompose-impl / phase2-n3-incremental-impl)
両 Phase とも *白紙から* 書く。過去生成物のコピーは禁止。

================================ PHASE A ================================
出力先: experiments/phase2-v03-n2-impl/

GRID_COMPOSITION + IMAGE_VARIANT_MANAGEMENT を契約 v0.3 の下で実装する。
n=2 (Addendum F) と同じ要領だが、*read ポートを最初から* 組み込む点が違う:

  必須 (契約 v0.3 §1.8、consumer がまだ無くても実装する):
    - src/shared/render_contracts.py : 中立 DTO PlacementView / GridLayout / CopyRenderSpec
    - src/shared/ports.py : ImageCopyExistencePort (n=2 既存境界) +
                            GridLayoutPort / CopyRenderSpecPort (read ポート、前倒し)
    - GridCompositionUseCases.get_grid_layout(grid_id) -> GridLayout | None
      (GridCanvas + 配置 -> 中立 GridLayout。grid 不在は None)
    - ImageVariantManagementUseCases.get_copy_render_spec(copy_id) -> CopyRenderSpec | None
      (ImageCopy -> 中立 CopyRenderSpec。enum は .value で str 化。copy 不在は None)

  既存 n=2 の境界 (変わらず):
    - GRID UC-05 が ImageCopyExistencePort.exists を呼ぶ。imgvar が native に満たす。アダプタ 0。

  契約踏襲: identity=uuid.UUID / Ok,Err / not-found=None / timestamp=UTC / enum=enum.Enum /
            UseCases 命名 / src layout。

  GRID 全 UC/Rule/Event、IMGVAR 全 UC/Rule/Event (R-08 は宣言のみ) を実装。
  テスト: 両 Capability の必須テスト + Anchor AT-01..AT-10 + 1000-step random walk +
          compose 統合 (存在 copy 配置成功 / 不在 UnknownCopyId)。
  さらに read ポートのテスト: get_grid_layout / get_copy_render_spec が中立 DTO を返すこと。
  Phase A 完了時に pytest 全合格を確認すること。

================================ PHASE B ================================
出力先: experiments/phase2-v03-n3-impl/

  手順:
    1. experiments/phase2-v03-n2-impl/ の *全内容を verbatim コピー* する。
    2. RENDERING_EXPORT を *追加するだけ*。以下のみを新規に置く:
         - src/rendering_export/ (RenderingExportUseCases, domain, events, failures)
         - tests/test_render_*.py (Rule / UC / Event / AT-01..AT-08 / random walk / 境界 import)
         - compose.py を 3 Capability に更新 (render = RenderingExportUseCases(
             grid_layout=grid, copy_render_spec=imgvar) ... アダプタなし)
    3. *producer (src/grid_composition, src/image_variant_management) と
       src/shared には一切手を触れない*。get_grid_layout / get_copy_render_spec は
       Phase A で既に存在しているのでそのまま使う。

  RENDERING 実装規範:
    - GridLayoutPort / CopyRenderSpecPort + shared 中立 DTO のみに依存。
      grid_composition / image_variant_management を import しない。
    - R-08 (ManualCropOverridesAutoCrop) を UC-02 で適用 (manual 優先 -> auto -> none)。
    - C-IDENTITY-BOUNDARY (契約 v0.3 §1.9): RenderDescriptor / to_dict の identity
      (copy_id / grid_id) は *str 化* する (json.dumps 可能にする)。内部表現は uuid.UUID のまま。

== 成功条件 (契約 v0.3 §4.3) ==
  - Phase A の n=2 が read ポートと中立 DTO を *最初から* 同梱
  - Phase B 後、producer + shared の全ファイルが Phase A と *byte-identical* (diff 0 = retrofit 0)。
    新規追加は src/rendering_export/ とそのテストのみ
  - RENDERING↔GRID / RENDERING↔IMGVAR の結線アダプタ 0
  - RENDERING が producer domain を import しない
  - R-08 が UC-02 で適用 (AT-02 パス)
  - RenderDescriptor が json.dumps 可能 (identity str 化、G.7 の解消)
  - Phase A / Phase B 両方で pytest 全合格

== NON-GOALS (禁止) ==
- 参照禁止の違反 / 過去生成物のコピー
- Phase B で producer / shared を変更すること (retrofit 0 が検証対象)
- 境界に standalone アダプタを書くこと (書かず unclear 報告)
- Rule/UC/Event/失敗理由名の変更・追加

== OUTPUT (実装ノート) ==
各 Phase の dir に IMPLEMENTATION_NOTES.md と README.md。
Phase B の IMPLEMENTATION_NOTES.md には特に:
  - producer + shared の diff が 0 だったことの自己申告 (どう確認したか)
  - 新規追加ファイル一覧 (rendering_export とテストのみであること)
  - RENDERING の domain 非結合 / R-08 適用 / RenderDescriptor の str 化
  - consumer 結線アダプタ行数 (0)
  - 前倒しの投機的コストの所感 (n=2 が consumer 不在で read 射影を持つこと)

== FINISH ==
  python -m pytest experiments/phase2-v03-n2-impl/ -q
  python -m pytest experiments/phase2-v03-n3-impl/ -q
  python experiments/phase2-v03-n3-impl/compose.py
  # producer + shared の diff が 0 であることを自分でも確認 (例: 各ファイルを比較)

報告 (450 語以内、実験結果のみ) に必ず含める:
  1. Phase A / Phase B の総テスト数 / 合格数
  2. Phase A の n=2 が read ポート + 中立 DTO を最初から持つか (該当ファイル名)
  3. Phase B 後の producer + shared の diff (0 か。0 でなければ差分の中身)
  4. Phase B の新規追加ファイル一覧 (rendering_export + テストのみか)
  5. consumer 結線アダプタ行数 / RENDERING の producer domain import 有無
  6. R-08 適用 (AT-02) と RenderDescriptor の json.dumps 可否
  7. 前倒しの投機的コストの所感
  8. 再現コマンド
docs の要約は不要。実験結果のみ報告せよ。
```

## B. 関連
- 契約 v0.3: `../00-convention-contract.md` (§1.8 前倒し / §1.9 C-IDENTITY-BOUNDARY / §4.3)
- 前段: Addendum F (n=2) / Addendum G (n=3 後付けで producer retrofit が要った)
- 評価記録: `../../evaluation/90-feasibility-notes.md` Addendum H
