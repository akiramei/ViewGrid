# F-P13 — Software Manufacturing Control v0.1 (change classification + required gates)

> 実施 2026-06-01。工程管理層の **入口** だけを、抽象論でなく **実証済み部品**から立ち上げる。
> **スコープ限定**: `change classification` + `required gates` のみ。traceability / metrics は最小セクション、
> release criteria は v0.2、RENDERING_EXPORT BOM はその後。over-build 回避 (F-P9 と同じ規律)。
> **実例は実証済みのみ**: IO-1 / IO-3 / F-P10 / F-P12 / F-P11 / PV-3 / D2a-D2b-D-PV。

## 0. なぜ change classification + gates から
これまでの成果は 3 系統に整理できる (いずれも実コードで実証済):
```
1. research → production 改善          : IO-1 (crop 優先の単一源化) / IO-3 (Fork が Regions 継承)
2. generation → gap 発見 → overlay/gate : F-P9/F-P10/F-P11/F-P12 (generation_overlay / generation_gate)
3. incidental → human decision → deliberate : D2a (ToEven) / D2b (軸正 precondition) / D-PV (PlacementOrder 昇順)
```
工程管理層はこれらを **「毎回の開発工程」に接続**する層。その入口は次の連鎖で、最初の 2 つを固めれば残りは後から自然に生える:
```
変更を分類する → 分類ごとに必要な gate を決める → traceability / metrics / release criteria が後続
```
∴ v0.1 は **change_types + required_gates** に絞る。各 change_type には *既に実例がある*ので空中戦にならない。

---

## 1. change_types (分類 + required gates)
```yaml
# v0.1 = 直近で実際に回した 5 種のみ。各 type は実例 (実証済) を持つ。
# 既知だが v0.1 で詳細化しない type は §9 に列挙 (silent に落とさない)。
change_types:

  semantic_bugfix:               # 既存挙動が契約に反する = 直す
    examples: [ "IO-3: Fork (CloneWithNewId) が Regions を継承しない latent bug" ]
    required_gates:
      - current_behavior_evidence   # file:line で「何が起きているか」を裏取り
      - design_judgment             # 「直すのが正しいか」を BOM/永続性/契約から判断 (AI 単独で決めない領域は human へ)
      - production_fix
      - anchor_test                 # 旧挙動で FAIL する決定的 falsifier を確認 (§4)
      - full_test_suite             # 本番構成 (analyzers ON) で全 pass
      - affected_bom_update         # 該当 finding を mitigated/resolved へ
      - independent_review          # Codex review (push 前、§6)

  drift_elimination:             # 同一意味が複数実装 = 唯一源へ寄せる
    examples: [ "IO-1: crop 優先規則 (R-08) の 3+1 重実装を Core CropFraction.ResolveEffective へ単一源化" ]
    required_gates:
      - identify_duplicate_semantics  # 重複サイトを全数列挙 (grep/読解、漏れ厳禁)
      - choose_canonical_source       # 依存方向に整合する単一源 (Core 純関数等) を選ぶ
      - delegate_all_sites            # 全サイトを単一源へ委譲
      - regression_tests              # 挙動保存を既存スイートで確認 + 単一源の oracle 昇格
      - affected_bom_update           # finding を mitigated、canonical_source を記録
      - independent_review

  generation_micro_pilot:        # as-built からの blind 再生成で意味等価/gap を検証
    examples: [ "F-P10: crop resolver blind 生成", "F-P12: enriched spec で ToEven 収束" ]
    required_gates:
      - frozen_spec                   # 実装コード片を含まない凍結入力
      - blind_generation              # 独立生成器・リポジトリ非参照 (tool use 0 = 真の blind)
      - generated_artifact_saved      # generated/*.gen.cs を記録 (非コンパイル)
      - swap_validation               # 実 src へ swap → テスト → revert (src 不変)
      - oracle_tests                  # 意味等価の gate。coverage_gaps を自己申告
      - gap_classification            # 発散を semantic (→overlay) か style (→gate) に分類 (PV-2)
      - result_doc                    # *-RESULT.md (主張は oracle カバレッジ範囲内に限定)

  deliberate_decision:           # 偶発挙動を人間裁定で deliberate 固定
    examples: [ "D2a ToEven", "D2b 軸正 precondition", "D-PV PlacementOrder 昇順 conflict" ]
    required_gates:
      - current_behavior_evidence     # file:line で現挙動を裏取り (推測でなく)
      - options_and_impact            # 選択肢 + テスト/UX/互換性への影響
      - human_decision                # ユーザー裁定 (AskUserQuestion 等)。AI は提案のみ
      - bom_deliberate_decisions_update  # 該当 BOM の deliberate_decisions へ + provenance: as-built-incidental → deliberate
      - anchor_if_observable          # 挙動が観測可能なら固定 anchor (例: PV conflict / midpoint oracle)

  schema_feedback:               # 実験で得た知見をスキーマ/方法論へ還元
    examples: [ "F-P11: generation_overlay 化", "PV-3: overlay/gate 二層分離" ]
    required_gates:
      - source_experiment_reference   # どの実験 (F-P*/PV-*) の知見かを明示
      - schema_update                 # 提案 doc/overlay/gate を追記 (P4: 追記のみ・churn 最小)
      - no_overgeneralization_review  # 1 例過適合を疑う敵対的レビュー (Codex)。実 BOM 注入は別決定 (D1)
```

---

## 2. gate 語彙 (checkable な定義)
各 gate は「満たした証拠」を残して初めて通過。曖昧な自己申告にしない。

| gate | 定義 / 通過証拠 |
| --- | --- |
| current_behavior_evidence | 現挙動を **file:line** で引用。推測語 ("おそらく") 禁止。 |
| design_judgment | 永続性・契約・BOM・依存方向から「変更が正しいか」を論証。AI 単独で決められない判断は human_decision へ昇格。 |
| anchor_test | **旧挙動で FAIL** する決定的 falsifier を実際に走らせて確認 (analyzers-off でも可) + 実コードで PASS。 |
| full_test_suite / regression_tests | 本番構成 (analyzers ON) で全 pass。件数を記録 (例: Application 466→467)。 |
| affected_bom_update | 該当 BOM の finding を `mitigated`/`resolved` に、`anchor`/`canonical_source`/`resolution` を記載。 |
| independent_review | **Codex review を push 前に実行** ([[feedback-codex-review-before-commit]])。approve を確認。 |
| identify_duplicate_semantics | 重複サイトを **全数** 列挙 (grep + 読解)。「単体再生成は跨ぎ drift を是正しない」を意識 (d3 io1_caveat)。 |
| choose_canonical_source | 依存方向に反しない単一源を選ぶ (Infra→Application 不可、Core 純関数は共有可)。寄せない判断も明記。 |
| frozen_spec | 実装コード片を含まない意味記述のみ。生成器が参照する唯一入力。 |
| blind_generation | 独立生成器・**tool use 0 = リポジトリ非参照**。 |
| swap_validation | 生成物を実 src へ swap → テスト → `git checkout` で revert (src 不変を git status で確認)。 |
| gap_classification | 発散を **semantic (silent、oracle 被覆で overlay 補填)** か **style (loud、analyzer gate が機械担保)** に分類 (PV-2/§5)。 |
| options_and_impact | 選択肢ごとに テスト結果/UX/将来互換性への影響を明示。 |
| human_decision | ユーザー裁定。AI は options+推奨を出すのみ (decision ownership)。 |
| bom_deliberate_decisions_update | `provenance: as-built-incidental → deliberate`、rationale、do_not を記載。 |
| no_overgeneralization_review | 1 例過適合を疑う (例: 2 例目で overlay 形が安定するか)。実 BOM 注入は採否判断 (D1) を分離。 |

---

## 3. human decision が必要な条件
次のいずれかなら **human_decision gate 必須** (AI が「より自然/一貫」と判断して変えてはいけない):
- 挙動が **偶発** (`as-built-incidental`) で、変えると **テスト結果・UX・将来互換性**に影響する (例: D2a 丸めモード)。
- 複数の正解があり「どれを *選ぶ* か」が設計判断 (例: D2b throw vs precondition vs normalize / D-PV 同定規則)。
- **意味の所有権**が問われる (BOM の decision_ownership / does_not_own を侵すか)。
- spec の自然語が **多義** で、機械的語彙へ確定する必要がある (例: 「四捨五入」→ ToEven/AwayFromZero)。
→ 該当しない (純粋なバグ修正・記法整形・既決契約の適用) は AI が deliberate に進めてよい。

## 4. anchor test が必要な条件
- `rules.fragile: true` の不変条件 (F-P3/5/6/7 の原則: fragile = 決定的 anchor を持つべき優先リスト)。
- production finding を修正したとき (IO-3: 旧挙動 FAIL の falsifier)。
- deliberate decision の挙動が **観測可能** なとき (D-PV: conflict 同定 / D2a: midpoint oracle)。
- **隣接する同名/類似テストが盲点を覆い隠す**恐れがあるとき (F-P5/F-P6: sub-invariant 分解で偽陽性を炙る)。
→ anchor は「旧挙動/deviation で **必ず FAIL**、実コードで PASS」を満たして初めて gate 通過。

## 5. generation_overlay / generation_gate が必要な条件
- **generation_overlay (生成入力=意味)**: 対象を blind 再生成するとき、oracle が踏まない意味細部 (丸め/精度/判定順序/error channel/conflict 同定) は overlay に補填しないと **silent に発散** (F-P10 の丸め)。
- **generation_gate (受入検査=style/compilation/oracle)**: 生成物の style/analyzer 適合・コンパイル・oracle 通過。style は overlay で言語化せず gate が **機械担保** (PV-2: IDE0161/CA1510/IDE0005 を analyzer gate が build 時に loud に捕捉)。
- 切り分け規則: **意味は overlay (oracle 被覆で収束) / style は gate (analyzer・formatter が担保)**。rule が既に持つ意味は overlay が *参照*、欠落次元のみ overlay が新規保持 (over-build 回避)。

## 6. production code 変更時の review / push 手順 (実運用フロー)
IO-1 / IO-3 で実際に回した手順を工程として固定:
```
1. current_behavior_evidence   : 現挙動を file:line で確認
2. design_judgment / human     : 変更が正しいか論証。AI 単独不可なら human_decision (§3)
3. production_fix
4. anchor_test (§4)            : 旧挙動で FAIL を確認 (一時 revert で falsifier 検証) → fix 復元
5. full_test_suite            : 本番構成 (analyzers ON) で全 pass。件数記録
6. affected_bom_update        : finding を mitigated/resolved + anchor/canonical 記録。YAML 検証
7. independent_review         : Codex review (push 前)。approve 確認
8. commit                     : trunk (main) へ。メッセージに finding/anchor/件数/設計判断
9. push                       : ユーザー指示時。基線を origin と同期
10. memory_update             : finding + push 状態 + 次の一手
```
- branch 方針: 本リポジトリは trunk-based (main 直コミット)。
- 破壊的操作 (swap 等) は検証後 `git checkout` で revert し、src 不変を git status で確認。

---

## 7. traceability (v0.1 最小: finding → decision → test → commit)
巨大な追跡システムにしない。v0.1 は 1 finding = 1 trace 行で十分。
```yaml
trace:
  - { finding: IO-1, decision: "crop 優先を Core ResolveEffective へ単一源化", test: CropFractionResolveEffectiveTests, commit: 2bc4aa9, status: mitigated, type: drift_elimination }
  - { finding: IO-3, decision: "Fork は Regions を継承 (新 Id+新 FK の独立複製)", test: Fork_Copies_ProtectedRegions_With_New_Ids_And_Fk, commit: d4be7c4, status: mitigated, type: semantic_bugfix }
  - { finding: F-P10, decision: "crop resolver は blind 生成で drop-in 等価 (丸め gap 顕在化)", test: "ImageCropResolverTests + CropFractionTests", commit: 24ca4de, status: validated, type: generation_micro_pilot }
  - { finding: F-P12, decision: "enriched spec (ToEven) で in-range 収束", test: ToPixelBbox_Midpoint_Rounds_ToEven_AsBuilt, commit: 30ddf86, status: validated, type: generation_micro_pilot }
  - { finding: F-P11, decision: "欠落次元を generation_overlay 化", test: "(schema 提案)", commit: 3d5b18d, status: proposed, type: schema_feedback }
  - { finding: PV-3, decision: "overlay (意味) / gate (style) の二層分離", test: "(schema 提案)", commit: 2b2ed55, status: proposed, type: schema_feedback }
  - { finding: D2a, decision: "ToPixelBbox 丸め = ToEven を preserve+document", test: ToPixelBbox_Midpoint_Rounds_ToEven_AsBuilt, commit: d21fe4e, status: deliberate, type: deliberate_decision }
  - { finding: D2b, decision: "ToPixelBbox 軸正 precondition (document-only)", test: "(precondition、到達不能パス)", commit: d21fe4e, status: deliberate, type: deliberate_decision }
  - { finding: D-PV, decision: "conflict 同定 = caller 安定順 (PlacementOrder 昇順)、validator は入力順保存", test: ConflictingPlacementId_Is_First_In_Collection_Order_AsBuilt, commit: d21fe4e, status: deliberate, type: deliberate_decision }
```

## 8. metrics (v0.1 最小: 3 つ、現時点の実績)
```yaml
metrics:
  fragile_invariants_anchored:        # fragile 不変条件のうち決定的 anchor を持つ割合
    value: "2/2 (GRID: AR-02, AR-07)。AR-02=2 anchor (F-P3/F-P7) / AR-07=3 anchor (F-P5/F-P6 ほか)"
  production_findings_mitigated:      # production code を直した finding 数
    value: 2
    items: [ IO-1, IO-3 ]
  incidental_behaviors_deliberated:   # 偶発挙動を deliberate 固定した数
    value: 3
    items: [ D2a, D2b, D-PV ]
```
→ いずれも現実績から即数えられる (空中戦でない)。自動集計は v0.2。

---

## 9. v0.1 でカバーしない (既知の境界 — silent に落とさない)
v0.1 の change_types は **直近で回した 5 種**に限定。次は実例があるが詳細化を v0.2 へ送る (gate は既存 RESULT に存在):
- **invariant_hardening** (F-P3/F-P5/F-P6/F-P7): fragile 不変条件に決定的 anchor を足す (production 変更なし)。gate は maintenance-task-1 RESULT に。
- **as_built_bom_authoring** (F-P8 / 候補c): 新 Capability の as-built BOM 化 (両側突合)。gate は IMAGE_VARIANT BOM 作成手順に。
- **maintenance_audit** (F-P1/F-P2): AI が AI の保守を BOM で監査 (検出・sign-off・汎化)。gate は maintenance-task-1/2 RESULT に。
- v0.2 で追加予定: `traceability` 拡張 / `metrics` 自動集計 / `release_criteria` / RENDERING_EXPORT BOM。

## 10. 使い方 (decision flow)
変更に着手する前に:
```
(1) この変更はどの change_type か? (§1。複数なら主たるものを選び、跨ぎは両方の gate を満たす)
(2) §3: human decision が要るか? → 要れば AI は提案のみで止まり裁定を仰ぐ
(3) §4: anchor test が要るか? → 要れば falsifier を確認
(4) §5: 生成を含むなら overlay/gate のどちらで担保するか
(5) production code を触るなら §6 の 10 ステップ
(6) §7 に trace 行を 1 つ足す
```

## next
- v0.2: release_criteria + traceability/metrics の拡張 + §9 の 3 type を取り込む。
- 3 つ目の Capability (RENDERING_EXPORT BOM) に入るとき、本分類で「as-built 拡張 / production finding / generation candidate / deliberate decision」を仕分けながら進める。
