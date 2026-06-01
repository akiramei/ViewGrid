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

## 反映 (裁定後・実施済)
1. ✅ ユーザー裁定 (3 件とも推奨採用、2026-06-01)。
2. ✅ `deliberate_decisions` を該当 BOM へ反映: IMAGE_VARIANT=D2a/D2b、GRID=D-PV。
3. ✅ PV-2 anchor `ConflictingPlacementId_Is_First_In_Collection_Order_AsBuilt` のコメントを「caller 安定順 (PlacementOrder) が契約」へ更新 (挙動=不変、意図注記のみ)。d3 §7 の D2a/D2b/D-PV を裁定済へ。
4. → 次は (4) 工程管理層: この `deliberate_decisions` 群が「人間決定に分類する」入力になる。
