# 90 — フィージビリティ評価メモ

## このドキュメントの位置づけ

本書は、`GRID_COMPOSITION` を題材としたサンプル成果物 (10〜40) を実際に書いた経験から得た
**課題・欠落リスク・人間コストの実情・方法論の限界** に関する所見である。

書き手 (AI) の作業視点で、**この方法論を全 Capability に水平展開する場合に何が起こるか** を
率直に評価する。

> [!IMPORTANT]
> 本書は楽観評価ではない。**サンプル作成中に実際に詰まった点、迷った点、
> 「これは AI に渡しても解決しないだろう」と感じた点** を残すことを目的とする。

---

## 1. 全体所見 (要約)

| 観点 | 評価 | 主な根拠 |
| --- | --- | --- |
| BOM → コード生成は原理的に成立するか | **条件付きで成立** | 純度の高い Capability (本サンプル) では成立。汎用 Capability では別の仕掛けが要る |
| サンプル成果物の十分性 | **GRID_COMPOSITION 単体としては高い** | UC / Rule / Event / Decision が一意に決まっている |
| 水平展開時の人間コスト | **6 Capability で 5〜10 倍** | 境界の交錯が増え、調整コストが線形以上に膨らむ |
| 既存方法論ドキュメントの十分性 | **不足あり** | 後述 §6 の 8 つの判断はサンプル執筆中に独自決定が必要だった |
| 監査の逆方向利用としての健全性 | **健全だが非対称性に注意** | §2 参照 |

---

## 2. 「BOM → コード生成」が逆問題として持つ非対称性

本サンプルを書きながら最も強く感じた構造的問題。

### 2.1 監査方向との非対称性

| 方向 | 入力 | 出力 | 不足が顕在化する場所 |
| --- | --- | --- | --- |
| 監査 | 既存コード | BOM (観測) | 観測者が `unclear` と書ける |
| 生成 | BOM (人間) | コード (AI) | **AI が「合理的に補完」してしまう** |

AI は不明点を `unclear` と残すよう指示しても、**実装上どこかで決定せざるを得ない**
(関数の引数順、命名、エラー時の挙動の細部など)。
この「**断定を強制される箇所**」が、方法論の死角となる。

### 2.2 顕在化した死角

サンプル執筆中、要求仕様・BOM・設計書には書ききれないが AI 実装時に必ず必要な決定:

1. **Repository インターフェースの戻り値が `None` か例外か**
   → 30-design.md §5.1 では型表記レベルで未確定
2. **イベントの payload に `snapshot` を含める粒度**
   → 「全体」とだけ書いたが、参照の循環 (CopyId 経由で ImageCopy 全体まで?) をどこで切るか
3. **timestamp が UTC か local time か**
   → どこにも書かれていない (現実の ViewGrid は `DateTimeOffset` を使うが、本サンプルは未規定)
4. **配置順序の表現が密 (1..N) か疎 (1, 10, 100...) か**
   → R-09 で「密 (1..N)」を強制したが、UI 上の挿入操作が頻発する設計では非効率の可能性
5. **重み配列の最大値**
   → 「正の整数」とだけ書いたが、`int` のオーバーフローや表示計算の精度限界は未規定

これらは AI が **自由に決めてよい** とも、**人間が決めるべき** とも、本サンプルでは
明示していない。実際の試行では「実装上の細部」として AI が勝手に決め、その結果が
人間の意図と一致しない可能性が残る。

### 2.3 対策案

- ALLOWED / FORBIDDEN の **第三カテゴリ** として `MUST_DECIDE_AND_DOCUMENT` を導入
  - AI が決めてよいが、決定内容を実装ノートに明示する義務を負う
- 監査フェーズ (Phase 3) で「明示されていない決定」を AI に列挙させ、人間がレビュー

---

## 3. サンプル作成中に独自決定した事項 (方法論ドキュメントが規定していない)

| 決定事項 | 本サンプルでの選択 | 方法論側で規定したい候補 |
| --- | --- | --- |
| YAML と Markdown でどちらが正準か | YAML が正 | 規定すべき (人間/AI の両ループで参照されるため) |
| ID 体系 (UC-NN, R-NN) | 2 桁ゼロ詰め | テンプレート化したい |
| 失敗理由の表現方法 | 文字列 enum (`OutOfBounds` 等) | 規定 (国際化との関係も) |
| 「読み順」の指定 | README に記載 | 方法論側でテンプレ化 |
| Capability 境界の図示形式 | ASCII 図 | 推奨形式を方法論側で定義 |
| イベント payload の正準形式 | キー名で羅列 | スキーマ言語 (JSON Schema 等) の選択 |
| 「観測可能性 (Audit 要件)」の項目 | 自前で追加 | 方法論側で標準項目化したい |
| プロンプト雛形における POST_IMPLEMENTATION_SELF_AUDIT | 自前で追加 | 方法論側で必須項目化したい |

これらは Capability BOM Audit 方法論ドキュメント (01〜10) には
**書かれていない** が、サンプル作成時に必須になった。
方法論を実運用に乗せるには、これらの **下位テンプレート / 規定** が必要。

---

## 4. 「同じ UI でなくていい」前提が招く別の困難

ユーザーは「UI 同一性は要求しない」と明示した。これは PoC として正しい設定だが、
別種の困難を生む:

### 4.1 検証の困難

「ユースケースを満たすか」の評価が **自動化困難** になる。

- UI が同じ → スクリーンショット差分・UI 自動テストで定量比較可能
- UI が違う → 人間が「これでユースケースを満たすか」を主観評価せざるを得ない

> 解決の方向性: 「UI を持たない CLI / API レベルでの UseCase 直接呼び出し」を
> AI に要求する変種プロンプトを用意し、自動テストで判定可能にする。

### 4.2 「使いやすさ」が消える

ViewGrid の元実装には D&D による直感的な配置編集がある。
本サンプルはこれを `UC-06 MovePlacement` という抽象操作に還元している。
AI が CLI / 単純な GUI で実装した場合、**機能としては成立するが UX として劣化する**。

これは Capability BOM Audit が **UX 要件を扱わない** 方法論であることの帰結。
評価軸として「機能」と「体験」は別物であることを `90-feasibility-notes.md` に
記録しておく必要がある。

---

## 5. 水平展開コスト見積もり

仮に ViewGrid の全 Capability (6 個) に本サンプル相当のドキュメントを用意する場合の見積もり:

| Capability | 想定難度 | 主な困難 |
| --- | --- | --- |
| GRID_COMPOSITION | 中 (本サンプル) | 純度が高い。境界が比較的明確 |
| IMAGE_VARIANT_MANAGEMENT | 高 | 「論理コピー」「派生物」概念の言語化、AutoCrop/ManualCrop の優先関係 |
| HISTORY_MANAGEMENT | 高 | Undo 粒度の決定 = 大量の domain_decision を一望する必要 |
| GRID_LAYOUT_CONTROL | 中 | Fit 動作のアルゴリズム選択肢が複数 |
| RENDERING_EXPORT | 高 | PhotoBoard / Normal の 2 モード、SkiaSharp 等の選択 |
| WORKSPACE_MANAGEMENT | 中 | DB / ファイルの物理分離 |

### 5.1 線形以上に膨らむ要素

| 要素 | 線形成長 | 非線形成長 |
| --- | --- | --- |
| 個別 Capability のドキュメント | ○ | |
| Capability 境界の宣言 | | × (n×(n-1)/2 で交差) |
| 共有概念 (例: ImageCopy) の整合 | | × (全 Capability で参照される) |
| 用語集の統合 | | × (用語間の関係も) |
| Decision ownership 表 | | × (境界の交錯で曖昧になる) |

GRID_COMPOSITION 単体で **〜2,500 行 (本サンプル合計)** だった。
全 6 Capability では **15,000〜25,000 行** が想定される。

### 5.2 維持コスト

機能追加・仕様変更時には:

1. 要求仕様の修正
2. BOM の修正 (Markdown + YAML)
3. 設計書の Rule ledger 更新
4. AI プロンプトの version up
5. 既存生成コードの再生成 or 部分更新の判断

**「コード修正」より「ドキュメント修正」が高コストになる転倒** が起きる可能性がある。
これは方法論の本義 (人間は意味設計に集中する) と一致するが、コスト感覚として要注意。

---

## 6. 既存方法論ドキュメント (01〜10) で扱われていない論点

サンプル執筆中に気付いた、方法論本体に追記したい論点:

### 6.1 「人間が書く側」のドキュメント標準が不足

01〜10 は **「監査者 (観測者) としての AI への指示」** が中心。
**「人間が BOM を新規執筆するとき」のテンプレート** がない。
本サンプル群がその第 1 案として参考になる。

### 6.2 矛盾解決ルール

複数ドキュメント間 (Markdown / YAML / コード) の矛盾解決ルールが未規定。
本サンプルでは「YAML が正」と暫定したが、方法論側で固定したい。

### 6.3 識別子の永続性

Rule ID, UseCase ID をリネームしない原則は本サンプルで導入した。
方法論側に「**ID は永続。表示名はローカライズ可能**」を明文化したい。

### 6.4 自己監査の標準化

POST_IMPLEMENTATION_SELF_AUDIT は本サンプルで導入したが、
方法論側にテンプレート化したい:

- 各 Rule の保証場所の自己申告
- 各 Decision の保持場所の自己申告
- `unclear` / `suspected` 項目の網羅

### 6.5 「人間が書いたが AI が書き直す」のフロー

要求仕様が変わったとき、

- 人間が要求仕様を直す
- AI が BOM 案を更新
- 人間が承認
- AI が設計書を更新

というループの **責任の所在** が方法論側で未規定。本サンプルは静的成果物のみで、
このループは扱っていない。

---

## 7. PoC として答えるべき問い (今後の評価項目)

このサンプルの先で実施すべき検証:

### 7.1 Phase 2: 実 AI セッションでの生成試行

別 AI セッションを開き、本サンプルだけを渡して `GRID_COMPOSITION` を実装させる。
評価項目:

- [ ] UC-01 〜 UC-11 全てが実装されるか
- [ ] R-01 〜 R-09 が宣言された場所で保証されるか
- [ ] Event が指定された payload と発行タイミングで発火するか
- [ ] Decision ownership 違反がないか
- [ ] AI 自身の `unclear` / `suspected` リストが妥当か (人間レビュー)
- [ ] 必須テストが網羅されているか

### 7.2 Phase 3: 別 AI による事後監査

Phase 2 の生成コードに対し、別 AI に **通常方向の Capability BOM Audit** を実施させ、
入力 BOM と観測 BOM の差を測定。差が大きい部分が、サンプルの不足箇所。

### 7.3 Phase 4: 機能要件適合性の人間評価

Phase 2 の生成コードが、要求仕様のシナリオ S1〜S6 を満たすかを人間が確認。

---

## 8. 推奨される次の作業

優先度の高い順:

1. **方法論ドキュメントへの追補** (§6 の論点)
   - 識別子永続性ルール
   - 矛盾解決ルール
   - 自己監査テンプレート
   - 人間執筆者向けドキュメントテンプレート

2. **Phase 2 の実 AI 試行**
   - 別セッションで `40-ai-implementation-prompt.md` を使い実装させる
   - 結果を本書 §7 のチェックリストで評価

3. **`MUST_DECIDE_AND_DOCUMENT` 第三カテゴリの導入** (§2.3)
   - プロンプト雛形に追加し、AI 任意決定の追跡可能性を確保

4. **検証用 CLI / API インターフェース要件の追加** (§4.1)
   - UI を持たない実装でも UseCase 適合性を自動評価できるよう、要求仕様に追補

5. **隣接 Capability のドラフト着手**
   - IMAGE_VARIANT_MANAGEMENT が次に困難。これを先に書くことで境界調整の負荷を測れる

---

## 9. 結論

`Capability BOM Audit` を **逆向き (BOM → コード生成)** に使う発想は、

- **純度の高い Capability** (本サンプルのような境界明瞭なもの) では成立する見込み
- **境界が交錯する Capability** (HISTORY / RENDERING / WORKSPACE) では、本サンプル相当の
  ドキュメントだけでは不十分で、追加の **「決定追跡レイヤー」** が必要
- 方法論本体 (01〜10) には **執筆者向けテンプレートと矛盾解決ルールが不足** している

PoC としては、次のステップ:

1. 本サンプルを Phase 2 の AI 試行に投入
2. 観測された不足を方法論本体に反映
3. 別 Capability での再実験

を反復する価値がある。

ただし、**「AI に丸投げ」では成立しない**: 人間が BOM を真剣に書き、AI が
不明点を `unclear` として残し、人間が決定する、という相互ループが前提となる。
これは Capability BOM Audit の本義 (AI を実装者ではなく測量者・監査者として使う)
と整合的である。

---

## 10. 関連ドキュメント

- 全成果物: `README.md` の構成参照
- 方法論本体: `~/OneDrive/ドキュメント/Capability BOM Audit/01-10-*.md` (UTF-8)
- Phase 2 実装成果物: `experiments/phase2-impl/`

---

# Addendum A — Phase 2 実 AI 試行の結果 (2026-05-25 実施)

本書 §7.1 のチェックリストに従い、別 AI セッションに本サンプル成果物のみを渡して
`GRID_COMPOSITION` の実装を試行した結果と、本書本文の予測に対する実地検証の所見。

## A.1 実験条件と成果

| 項目 | 値 |
| --- | --- |
| 実施日 | 2026-05-25 |
| 実装者 | 別 AI セッション (Claude general-purpose subagent、worktree 隔離) |
| 入力 | `docs/capability-bom-sample/` の 7 ファイルのみ |
| 既存実装の参照 | 禁止 (worktree でも `src/`, `tests/`, `tools/`, repo README は不参照を明示) |
| 出力先 | `experiments/phase2-impl/` |
| 所要時間 | ~14 分 (実時間) |
| 選択言語 | Python 3.11+ / pytest |
| 実装規模 | Source 7 ファイル + Stubs 3 ファイル + Tests 5 ファイル + Docs 2 ファイル |
| テスト結果 | **97 件全合格** (Rule 28 / UC 44 / Event 19 / Invariants 6 / Boundary 5) |

実装ノートは `experiments/phase2-impl/IMPLEMENTATION_NOTES.md`。

## A.2 §7.1 チェックリストに対する結果

| チェック項目 | 結果 |
| --- | --- |
| UC-01 〜 UC-11 全てが実装されるか | **✓** 全 11 UseCase を実装 |
| R-01 〜 R-09 が宣言された場所で保証されるか | **△** 7/9 が単一場所。R-02 と R-06 は suspected_overreach あり (詳細は A.5) |
| Event が指定された payload と発行タイミングで発火するか | **✓** `RecordingBus` で成功時 1 件・失敗時 0 件を独立検証 |
| Decision ownership 違反がないか | **✓** UI 層を実装しなかったため Forbidden Role 保持なし |
| AI 自身の `unclear` / `suspected` リストが妥当か | **✓** 6 unclear + 2 overreach + 9 MUST_DECIDE_AND_DOCUMENT を honest に列挙 |
| 必須テストが網羅されているか | **✓** 30-design.md §6.1 の全カテゴリ + 1000-step random walk |

## A.3 本書本文の予測に対する裏取り

| 予測 (本文 §X) | 実地での結果 | 評価 |
| --- | --- | --- |
| §2.1 「AI は `unclear` を残すよう指示しても実装上どこかで決定せざるを得ない」 | AI が "This is direct evidence for the §2.1 thesis" と明示。9 件の MUST_DECIDE_AND_DOCUMENT が発生 | **完全に裏取り** |
| §2.3 「`MUST_DECIDE_AND_DOCUMENT` 第三カテゴリの導入が必要」 | AI は実装ノートで 9 件を自主的に分類 | **必要性が確定** |
| §3 「執筆中に独自決定した 8 事項」 | 多くがそのまま AI 側でも独自決定として発生 (timestamp tz、ID 体系、失敗理由名 等) | **横展開で確認** |
| §6.2 「Markdown / YAML 矛盾解決ルールが必要」 | 今回は実質的矛盾なし。R-08 の `WouldDestroyLockedAxis` 言及で YAML 優先ルールが 1 回だけ発動 | **空振り (ただし機能はした)** |
| §4.1 「UI 同一性なしでは検証自動化が困難」 | 部分的に反証。pytest による UseCase レベル直接呼び出しで自動評価可能だった | **要修正** (本書本文 §4.1 を緩める材料あり) |

## A.4 顕在化した具体的な仕様穴 (重要)

サンプル成果物 (10/20/21/30) を **次に改訂するなら塞ぐべき箇所**。

### 穴 1: "NotFound" 失敗理由の欠落

| 項目 | 内容 |
| --- | --- |
| 発見場所 | UC-02, UC-03, UC-04, UC-05, UC-06, UC-07, UC-08, UC-09, UC-10 |
| 状況 | YAML の `preconditions: [GridExists]` / `[PlacementExists]` は宣言されているが、それが破られた時の **正準失敗理由名がない** |
| AI への影響 | FORBIDDEN 「失敗理由を追加してはならない」と矛盾し、AI は (a) `InvalidDimensions` 流用、(b) 素の `KeyError`、(c) `None` 返却 のいずれかを **恣意的に選ばざるを得ない** |
| 実際の選択 | UC-02 は `InvalidDimensions` 流用 / UC-06〜UC-10 は `KeyError` (UseCaseError 非継承) → **不揃いな API 契約** |
| 改訂案 | BOM に `NotFound` 失敗理由を追加 (entity_kind を payload に持つ)、または「scope 外」を明示 |

### 穴 2: UC-09 SetOrder の値チャネル未定義

| 項目 | 内容 |
| --- | --- |
| 発見場所 | UC-09 ChangePlacementOrder の `SetOrder` operation |
| 状況 | YAML の `inputs: [placement_id, operation]` だけでは新 order 値をどう渡すか書かれていない |
| AI への影響 | kwarg `order_value: int \| None` を独自追加 (= MUST_DECIDE_AND_DOCUMENT 化) |
| 改訂案 | YAML の `inputs:` に `order_value` を追加、もしくは `SetOrder` 操作の専用 sub-schema を切る |

### 穴 3: Swap での自身排除セマンティクスの曖昧さ (実バグ事例)

| 項目 | 内容 |
| --- | --- |
| 発見場所 | UC-07 SwapPlacements に対する R-02 適用 |
| 状況 | R-02 の「UC-07 では双方を衝突対象から除外する」という宣言は、結果的に **A の新位置と B の新位置が互いに重なるケースを R-02 が拾わない** ことを意味する。30-design.md §1 R-02 の「除外対象」表現はこのケースを **暗黙的に scope 外** にしている |
| AI への影響 | 最初の実装はこれを取り逃がし、**1000 ステップのランダムウォークテストで実バグとして検出**。修正に R-02 ロジックの 2 箇所目化 (overreach O-1) が必要だった |
| 改訂案 | (a) 30-design.md §2.2 (workflow_decision) の UC-07 内部手順に「post-swap intersection check」を明記する / (b) `30-design.md §6` に **A/B 非対称サイズの swap worked example** を 1 件追加 |

> [!IMPORTANT]
> 穴 3 は「サンプル文書だけでは仕様が underdetermined だった」ことの **最も明確な証拠** である。
> AI のランダムウォークテストがなければ本番でバグとして出ていた。

## A.5 suspected_overreach の詳細

| ID | Rule | 場所 | 評価 |
| --- | --- | --- | --- |
| O-1 | R-02 | `rules.placement_overlaps` + `use_cases.SwapPlacements` 内 inline check | **正当な overreach**。穴 3 を塞ぐための必然。サンプル改訂で消える可能性あり |
| O-2 | R-06 | `rules.orders_are_unique` + UC-09 内 post-condition assertion | 防御的 assertion で R-06 の本体は構築的に満たす。strict には除去可 |

O-1 は仕様の不備に起因しており、AI を責めることはできない。

## A.6 検証された有用な実装パターン (方法論本体への昇格候補)

実験で観測された、サンプル/方法論側に **取り込むべき** 実装パターン。

| パターン | 内容 | 効果 |
| --- | --- | --- |
| **Random walk / property-based testing** | 任意のグリッドサイズ・任意の操作列で invariant が崩れないことを確認 (1000 step) | 穴 3 のような暗黙仕様穴を検出 |
| **`RecordingBus`** | テスト用の event 収集 bus を別実装として用意 | event 発行と状態変更の分離テストを容易にする |
| **`@dataclass(frozen=True)` + `replace()`** | R-07 (Placement の position/occupy_size の不変観測性) の自然な満たし方 | 実装スタイル自由度を保ったままの参考例 |
| **`MUST_DECIDE_AND_DOCUMENT` の実装ノートでの自己分類** | AI が自主的に 9 件を分類した | サンプル側で項目化していれば最初から構造化可能 |

## A.7 サンプル v0.2 改訂提案 (具体)

優先度高い順:

1. **20-capability-bom.md §2 + 21-yaml**: `NotFound` 失敗理由の追加 (entity_kind を payload に)
2. **20-capability-bom.md §2 + 21-yaml**: UC-09 `SetOrder` の value 引数明示
3. **30-design.md §2.2**: UC-07 swap の post-swap intersection check を workflow_decision に追加
4. **30-design.md §6**: A/B 非対称サイズの swap worked example 追加 (1 ケース)
5. **30-design.md §6.3**: random walk / property test を **必須テストカテゴリへ格上げ**
6. **40-ai-implementation-prompt.md**: `MUST_DECIDE_AND_DOCUMENT` を ALLOWED / FORBIDDEN と並ぶ第三カテゴリとして明示
7. **本書 §4.1**: 「UI 同一性なしでは自動評価困難」を緩める (UseCase レベル直接テストで成立した実証)

## A.8 方法論本体 (01〜10) への追補提案 (具体)

サンプル改訂とは独立に、方法論本体側に新規 11- 以降のドキュメントを起こす候補:

| 提案ドキュメント | 内容 |
| --- | --- |
| `11-author-checklist.md` | 人間が BOM を新規執筆するときのチェックリスト (識別子永続性 / 矛盾解決 / NotFound 等の失敗理由網羅性 / anchor tests 同梱) |
| `12-anchor-test-spec.md` | サンプル成果物に同梱する 5〜10 件の reference test の規範。解釈曖昧さを test で anchor する方法 |
| `13-must-decide-and-document.md` | AI 任意決定の第三カテゴリの定義と運用 (実装ノートでの分類義務) |
| `14-inverse-audit-protocol.md` | Phase 2 (BOM → コード生成) の実験プロトコル正典化 |

## A.9 結論

本書本文の予測は **おおむね裏取りされた**。特に §2.1, §2.3 は完全に確認された。
一方で §4.1 の「UI 同一性なしでは検証自動化困難」は実験で **部分的に反証** された
(UseCase レベル直接呼び出しで自動テスト可能だった)。

最も重要な発見は **穴 3 (Swap の自身排除セマンティクス)** で、これは
「書類だけでは仕様が underdetermined になる」という方法論側の限界を、
**実バグの形で具体化** した最初の事例である。同様の穴は他 Capability の境界部分にも
存在する可能性が高く、`anchor tests` 同梱と `MUST_DECIDE_AND_DOCUMENT` カテゴリの
両方が方法論本体に組み込まれるべきである。

PoC として `Capability BOM Audit` を **逆向き** に使う発想は、サンプル v0.2 改訂と
方法論本体への 4 件の追補を経て、**実運用フェーズに進める段階に到達** している。

---

# Addendum B — サンプル v0.2 改訂結果と Phase 2 再試行 (2026-05-25 実施)

Addendum A で挙げた 7 つの改訂提案のうち、サンプル側 7 件を v0.2 として実施し、
別 AI セッションに **v0.1 実装と完全独立** に再実装させた結果と評価。

## B.1 v0.2 で実施した改訂

| 改訂 | 対象ファイル | 内容 |
| --- | --- | --- |
| #1 | 20-capability-bom.md / 21-yaml | `NotFound` 失敗理由を canonical_failure_reasons セクションに追加 (UC-02..UC-10) |
| #2 | 20-capability-bom.md / 21-yaml | UC-09 SetOrder の `order_value` を inputs に明示 |
| #3 | 30-design.md §1 R-02 + §2.2 UC-07 | post-swap intersection check を workflow_decision として明文化 |
| #4 | 30-design.md §7 (新設) | Worked examples W-1〜W-6 を追加 (W-3 が swap edge case) |
| #5 | 30-design.md §6.3 | random walk / property-based test を **必須** へ格上げ |
| #6 | 30-design.md §8 (新設) | Anchor tests AT-01〜AT-10 規範を追加 |
| #7 | 40-ai-implementation-prompt.md | `MUST_DECIDE_AND_DOCUMENT` を第三カテゴリとして明示 / 自己監査を 4 → 6 項目に拡張 |

## B.2 Phase 2 v0.2 試行の結果概要

| 項目 | v0.1 | v0.2 | 評価 |
| --- | --- | --- | --- |
| 実装時間 | ~14 分 | ~12 分 | 同等 |
| 言語 | Python + pytest | Python + pytest + hypothesis | property-test 必須化の影響 |
| テスト数 / 合格率 | 97 / 100% | 75 / 100% | テスト構造が引き締まる方向 (重複削減) |
| **Anchor tests (AT-01〜AT-10)** | 概念なし | **10/10 初回パス** | **v0.2 改訂の中核効果** |
| `unclear` 件数 | 6 | 5 | 改善 |
| `suspected_overreach` 件数 | 2 | **0** | **改善** |
| **Swap edge case バグ** | random walk で事後検出 | **W-3 + AT-03 で事前回避** | **改善 (穴 3 が消えた)** |
| `MUST_DECIDE_AND_DOCUMENT` | 9 (自主分類) | 7 (明示義務化) | 改善 (構造化により漏れ減少) |

## B.3 v0.1 で観測された 3 つの仕様穴に対する効果

| 穴 (v0.1) | v0.2 対策 | 結果 |
| --- | --- | --- |
| 穴 1: NotFound 失敗理由欠落 | canonical_failure_reasons セクション新設 | **完全解消**。AI レポート: 「KeyError vs InvalidDimensions の曖昧さが完全に消えた」 |
| 穴 2: UC-09 SetOrder の値チャネル | YAML inputs に `order_value` を追加 | **完全解消**。AI は kwarg を独自追加する必要がなく、AT-04 が初回パス |
| 穴 3: Swap 自身排除セマンティクスの曖昧さ | W-3 worked example + AT-03 anchor test + §2.2 UC-07 step (iv) 明文化 | **完全解消**。AI レポート: 「post-swap intersection check を最初の実装で自然に書いた。random walk で何も検出されなかった」 |

## B.4 v0.2 でも残った / 新規に顕在化した問題

Phase 2 v0.2 で AI が報告した 3 件の新規発見。

### D-1: 30-design.md R-08 と YAML canonical_failure_reasons の不整合 (改訂取りこぼし)

| 項目 | 内容 |
| --- | --- |
| 場所 | 30-design.md §1 R-08 の Fit 動作仕様 |
| 状況 | v0.1 の名残で `WouldDestroyLockedAxis` という失敗理由を雛形コメントに残していたが、v0.2 で新設した `canonical_failure_reasons` には登録されていない |
| 性質 | **本書執筆者 (改訂作業中の AI) の取りこぼし**。v0.2 改訂時に canonical_failure_reasons を新設した際、§1 R-08 の旧記述を同時に整合させ忘れた |
| 影響 | AI は MD-6 (silent best-effort shrink) として独自決定で回避 (= MUST_DECIDE_AND_DOCUMENT 経路で安全に処理された) |
| 対応 | **本 Addendum 追加と同時に 30-design.md §1 R-08 を修正済み**。`WouldOrphanPlacements` / `WouldConflict` で表現する形に変更 |

### D-2: README.md に Addendum B 参照があるが Addendum B が未存在 (改訂取りこぼし)

| 項目 | 内容 |
| --- | --- |
| 場所 | docs/capability-bom-sample/README.md L4 |
| 状況 | v0.2 改訂時に「v0.1 → v0.2 の変更点は Addendum B を参照」と書いたが、Addendum B 本体は Phase 2 v0.2 試行後に書く予定だった |
| 性質 | **本書執筆者の取りこぼし**。フォワードリファレンスを書いてから本体を書くという順序ミス |
| 影響 | 致命的ではないが「文書間の前方参照は危険」という方法論側の教訓を残す |
| 対応 | **本 Addendum B の追加と同時に解消** |

### D-3: Cross-grid swap (異なるグリッド間の Swap) が未定義 (真の新規穴)

| 項目 | 内容 |
| --- | --- |
| 場所 | UC-07 SwapPlacements の入力仕様 |
| 状況 | YAML は `placement_id_a, placement_id_b` のみを入力に取り、両者が同じ GridCanvas に所属しているかは何も規定していない。Placement は `grid_id` を持つので異なるグリッド間の swap は理論上発生しうる |
| 性質 | **真の新規発見**。v0.1 では他の穴に隠れて顕在化しなかった |
| AI の対応 | `Conflict` 失敗で返す (= MUST_DECIDE_AND_DOCUMENT MD-7 として明示) |
| v0.3 候補 | UC-07 の preconditions に `BothPlacementsBelongToSameGrid` を追加し、失敗時の失敗理由を確定する必要がある。`NotFound` payload で表すか別途 `CrossGridSwapNotAllowed` を追加するかは方法論レベルの決定 |

## B.5 v0.2 改訂の核心的成功 — 三層構造による曖昧さ解消

最も重要な発見は AI 自身が報告した次の点:

> "the v0.2 docs alone were sufficient to produce a working implementation
> without the random walk discovering anything. That's the v0.2 thesis being validated.
> The W-3 worked example + step-(iv) in §2.2 UC-07 + the AT-03 anchor test form a clean
> three-layer chain (narrative → algorithmic → executable) that left no room to miss the case."

**三層構造 (narrative → algorithmic → executable) で曖昧さを塞ぐパターン** が
v0.2 で確立されたと言える:

| 層 | 表現 | 場所 |
| --- | --- | --- |
| narrative (物語) | 「除外対象に注意。A の新位置と B の新位置が重なるケースがある」 | 30-design.md §1 R-02 NOTE |
| algorithmic (手順) | 「workflow_decision 手順 (iv): A の新占有 ∩ B の新占有 が空でないことを検証」 | 30-design.md §2.2 UC-07 |
| executable (実行可能) | 「AT-03: 1×1 と 2×1 の swap で Conflict」 | 30-design.md §8 + W-3 |

この **三層パターンは方法論本体への昇格候補** である (新規 `15-three-layer-disambiguation.md`
として明文化したい)。

## B.6 v0.3 候補 (優先順)

1. **D-3 解消**: UC-07 に `BothPlacementsBelongToSameGrid` 前提条件と専用失敗理由を追加
2. **三層パターン明文化**: 方法論本体へ「narrative + algorithmic + executable の 3 層で曖昧さを塞ぐ」を昇格
3. **anchor tests 充実**: 現状 10 件だが、全 UseCase × 全 failure_reason の組み合わせをカバーするには ~30 件必要
4. **MUST_DECIDE_AND_DOCUMENT カタログ整備**: 今回観測された MD-1〜MD-7 を「典型決定パターン集」として方法論本体に収録
5. **改訂取りこぼし防止**: フォワードリファレンス禁止規範、CHANGELOG.md 運用、改訂チェックリスト

## B.7 反復検証ループとしての評価

Phase 2 v0.1 → v0.2 改訂 → Phase 2 v0.2 のサイクルは、

> **「サンプル v0.X を別 AI 試行に投入 → 穴を観測 → サンプル改訂 → 同条件で再試行 → 穴が消えたか確認」**

という反復検証ループが **実際に機能する** ことを示した。

これは Capability BOM Audit を **逆向き (BOM → コード生成) で実運用に乗せる** ための
最も重要な手続き的発見である。サンプル文書の品質は「単発の執筆」ではなく
**「反復試行による磨き込み」** で確保される、というのが PoC の最終的な結論。

## B.8 結論

v0.2 改訂は **3 つの v0.1 穴を完全に塞ぎ**、新規に **3 つの軽微な穴 (うち 2 件は改訂作業の取りこぼし、1 件が真の新規)** を顕在化させた。

| 観点 | 状態 |
| --- | --- |
| 純度の高い単一 Capability での運用 | **実用可能** |
| 反復検証ループ | **機能することを実証** |
| 三層構造パターン | **確立 (narrative / algorithmic / executable)** |
| 複数 Capability への水平展開 | 未検証 (次フェーズ) |
| 方法論本体への昇格 | 11-anchor-test-spec / 13-must-decide / 15-three-layer の 3 文書ドラフトが候補 |

本 PoC は **「単一 Capability では運用可能、複数 Capability への水平展開と方法論本体への昇格が次フェーズ」** の段階に到達した。

---

# Addendum C — 境界調整負荷の実測 (2 Capability ドラフト時、2026-05-25 実施)

GRID_COMPOSITION (v0.2) の隣に IMAGE_VARIANT_MANAGEMENT (v0.1) をドラフトすることで、
**「複数 Capability のサンプルを揃えるとき、境界・用語・カスケード等の調整作業にどれだけのコストがかかるか」** を実測する。

## C.1 ドラフト規模 (粗い見積もり)

| 項目 | GRID_COMPOSITION (v0.2) | IMAGE_VARIANT_MANAGEMENT (v0.1) | 比 |
| --- | --- | --- | --- |
| UseCase 数 | 11 | 17 | 1.5× |
| Rule 数 | 9 | 11 | 1.2× |
| Event 数 | 10 | 12 | 1.2× |
| ドキュメント行数 (概算) | ~2700 | ~2200 | 0.8× |
| Worked examples | 6 | 6 | 同等 |
| Anchor tests | 10 | 10 | 同等 |

ドキュメント行数で見ると IMAGE_VARIANT_MANAGEMENT のほうがやや少ない。
これは **v0.2 で確立した規範を初回から使えた** ためで、執筆効率は明らかに向上した
(canonical_failure_reasons / MUST_DECIDE_AND_DOCUMENT / 三層構造などの雛形が手元にあった)。

## C.2 境界調整作業として実際に発生したもの

### C.2.1 共有値オブジェクトの取り扱い

`OccupySize` / `PixelSize` は両 Capability で **同じ意味の値オブジェクト** だが、
どちらかが「権威」になる必要がある。本サンプルでは:

- **権威**: GRID_COMPOSITION (先に書かれたため)
- **IMAGE_VARIANT_MANAGEMENT 側**: 「GRID_COMPOSITION と共有定義」と明示し、二重定義しない

しかし AI 実装時には **どこにコードが置かれるか** という physical な決定が必要になる:

- (a) GRID_COMPOSITION の Domain Model に置き、IMAGE_VARIANT_MANAGEMENT から参照
- (b) 共通モジュール `shared/value_objects.py` を切る
- (c) 各 Capability で同じ型を独立に定義 (ただし意味が同じであることを保証)

本サンプルではこれを **MUST_DECIDE_AND_DOCUMENT** に委ねた。これは方法論的な不完全さ。
**`shared_concepts` セクション** を Capability BOM スキーマに追加することが v0.3 候補。

### C.2.2 cross-Capability の存在性確認

GRID_COMPOSITION の `ImageCopyExistenceCheck.Exists()` と、
IMAGE_VARIANT_MANAGEMENT の `UC-16 ImageCopyExists` は **同じことを別の場所で定義** している。

- GRID_COMPOSITION 側 (Repository インターフェース): "アダプタとして UC-16 を呼ぶ" と明記
- IMAGE_VARIANT_MANAGEMENT 側 (UseCase): "cross-Capability 用" と明記

この二重宣言は冗長に見えるが、各 Capability が **自分の境界条件を自分の言葉で書く** という原則の帰結。
方法論的にはこれが正しいと思われる (1 箇所だけに書くと「権威がどちらか」が曖昧化する)。

### C.2.3 Cascade decision の三層責任分離

最も興味深かった境界調整。`ImageCopy` 削除時の `Placement` の扱いを誰が決めるか:

| 候補 | 採用 | 理由 |
| --- | --- | --- |
| IMAGE_VARIANT_MANAGEMENT が cascade | × | 純度低下、GRID_COMPOSITION を知ってしまう |
| GRID_COMPOSITION が cascade | △ | 部分採用。Event 購読して **自分の** Placement をどうするかは決める |
| 上位 Coordinator が cascade | ○ | 全体的な意思決定 (確認ダイアログ等) は外側 |

結果: **3 層に責任が分離**。これは Capability BOM Audit の理論的予測通りだったが、
**実際にドキュメントへ落とすには両 Capability + 文書外の Coordinator 概念の 3 箇所に対応する記述が必要** だった。

具体的な編集箇所:
- `image-variant-management/20-capability-bom.md` §6.1 (cascade_decision を別 Capability へ)
- `image-variant-management/30-design.md` §5.2 (三層責任分離の明文化)
- `20-capability-bom.md` (GRID_COMPOSITION) §8.1 (購読側の責任を明記、v0.2 で追記)

> [!IMPORTANT]
> 境界が「2 Capability + 暗黙の Coordinator」になる時、**Coordinator の概念は方法論側に明示されていない**。
> これは v0.3 で `coordinator_pattern.md` (新規 16-) として方法論本体に昇格すべき。

### C.2.4 Capability 間に跨る Rule (R-08 のような)

`ManualCropOverridesAutoCrop` は IMAGE_VARIANT_MANAGEMENT の Rule ledger に **記載するが保証コードは持たない** という型破りな扱いを採用。

- 本 Capability の責任: 両値の共存を許す
- RENDERING_EXPORT の責任: 描画時の優先関係を適用

これは方法論本体には説明されていない設計パターン:

> **"Declaration-only Rule"**: ある Rule が複数 Capability に跨るとき、各 Capability の Rule ledger に記載しつつ、保証場所だけ別 Capability に逃がす

このパターンも v0.3 候補で方法論本体に昇格すべき (`14-declaration-only-rules.md` 等)。

### C.2.5 ディレクトリ構造の非対称性

GRID_COMPOSITION は `docs/capability-bom-sample/` 直下、
IMAGE_VARIANT_MANAGEMENT は `docs/capability-bom-sample/image-variant-management/` 配下。

この非対称性は不格好だが、**全 Capability に対称な構造を採るには既存パスを変更する必要がある**:
- 全 Capability ファイル参照の更新
- メモリへの記録の更新
- Codex review が見るパスの更新

v0.3 で `docs/capability-bom-sample/grid-composition/` への移行を行う想定。
これも **境界調整負荷の典型例** であり、最初から多 Capability 想定の構造を採るべきだった。

## C.3 v0.3 候補 (Addendum B の続き)

Addendum C で見つかった新たな改訂候補:

| 番号 | 内容 |
| --- | --- |
| C-1 | 共有値オブジェクト (`shared_concepts` セクション) を Capability BOM スキーマに追加 |
| C-2 | Coordinator パターンを方法論本体に明文化 (16-coordinator-pattern.md) |
| C-3 | Declaration-only Rule を方法論本体に明文化 (14-declaration-only-rules.md) |
| C-4 | ディレクトリ構造を Capability ごとの subdir に統一 (GRID_COMPOSITION の移行) |
| C-5 | Cross-Capability の存在性確認インターフェースの命名規範 (`<EntityName>Exists` を統一形に) |

## C.4 「水平展開コスト」§5 の予測値との比較

本書本文 §5.1 では「Capability 境界の宣言は n×(n-1)/2 で交差」と書いた。
2 Capability の現時点 (n=2) で、実際の境界調整に発生した作業:

| 項目 | 件数 |
| --- | --- |
| 両 Capability で重複する用語の宣言 (PixelSize, OccupySize) | 2 |
| Cross-Capability の UC 呼び出し参照 (UC-16) | 1 (両側に記載) |
| Capability 間に跨る Rule (R-08) | 1 |
| Cascade decision の三層責任分離 | 1 (3 箇所に記載) |
| ディレクトリ構造の非対称性に関する記述 | 親 README + 子 README + 本 Addendum |

n=2 でこの量。**n=6 (ViewGrid 全 Capability)** だと:
- ペア境界: 15
- 共有用語の権威決定: 数倍
- Coordinator パターンの記述: Capability ごとに発生

線形以上の増加が **実際に観測された**。本書本文 §5.1 の予測は正しい方向。

## C.5 主観評価

書き手としての実感:

- **執筆効率は v0.2 規範のおかげで向上**: canonical_failure_reasons / 三層構造 / Anchor tests / MUST_DECIDE_AND_DOCUMENT がテンプレ化されているので、各セクションを「埋める」感覚で書けた
- **しかし境界調整は別物**: 隣接 Capability 同士の関係を整合させる作業は単純な転記ではなく、**意味的判断 (誰が権威か)** を毎回求められる
- **方法論本体に上位概念が足りない**: Coordinator / Declaration-only Rule / Shared Concepts といった "Capability 間" を扱う語彙が、現状の 01〜10 にはない

## C.6 結論

2 Capability の同時運用は **執筆効率は向上したが、境界調整コストが新たに顕在化** した。
これは予想通りだが、**方法論本体に "Capability 間" を扱う上位概念が必要** という追加発見もあった。

v0.3 で次のことを行うべき:

1. ディレクトリ構造の対称化 (`grid-composition/` への移行)
2. 方法論本体に Coordinator / Declaration-only Rule / Shared Concepts の概念を追加
3. (オプション) IMAGE_VARIANT_MANAGEMENT v0.1 に対し Phase 2 試行を実施し、本サンプル単体の品質を測定 → **Addendum D で実施済み**

---

# Addendum D — Phase 2 IMAGE_VARIANT_MANAGEMENT v0.1 試行結果 (2026-05-25 実施)

Addendum C §C.6 で「次にやるべき」とした Phase 2 IMAGE_VARIANT v0.1 試行の結果。
本書の中核仮説:

> **v0.2 規範を初版から継承した v0.1 サンプルは、GRID の "本物の v0.1" より高品質に到達する**

## D.1 試行の概要

| 項目 | 値 |
| --- | --- |
| 実施日 | 2026-05-25 |
| 実装者 | 別 AI セッション (Claude general-purpose subagent) |
| 入力 | `docs/capability-bom-sample/image-variant-management/` 6 ファイル + 境界参照 (GRID 20/21) |
| 既存実装の参照 | 禁止 (worktree 隔離 + 明示禁則) |
| 出力先 | `experiments/phase2-image-variant-impl/` |
| 所要時間 | ~14 分 |
| 選択言語 | Python 3.11+ / pytest / Pillow |
| テスト結果 | **70 件全合格** (Rule 単体 / UC happy/failure / Event / Anchor / Random walk / Boundary) |

## D.2 三回試行の指標比較

| 指標 | GRID v0.1 | GRID v0.2 | **IMAGE_VARIANT v0.1 (規範継承)** |
| --- | --- | --- | --- |
| 所要時間 | ~14 分 | ~12 分 | ~14 分 |
| テスト数 | 97 | 75 | 70 |
| **unclear** | 6 | 5 | **3** ← **過去最低** |
| **suspected_overreach** | 2 | 0 | **0** ← v0.2 と同等 |
| Anchor tests 初回合格 | 概念なし | 10/10 | **10/10** |
| Random walk 実バグ検出 | 1 (Swap) | 0 | 0 |
| MUST_DECIDE_AND_DOCUMENT | 9 (自主分類) | 7 | 9 |
| 重大な仕様穴 | 3 (NotFound / SetOrder / Swap) | 3 軽微 (D-1 / D-2 / D-3) | 4 軽微 (§D.4 参照) |

## D.3 仮説の検証結果

**仮説は完全に裏付けられた**。

最大の根拠:

- **unclear が 3 件で過去最低** — GRID v0.1 (6) / GRID v0.2 (5) / IMAGE_VARIANT v0.1 (**3**)
- **suspected_overreach 0 件** — v0.2 と同等の精度
- **Anchor tests AT-01..AT-10 が初回パス** — 三層構造パターンの効果
- **重大な仕様穴ゼロ** — 1000-step random walk が新たな穴を検出しなかった

特に AI 自身が報告した次の点が決定的:

> "The sample arrived with `canonical_failure_reasons` (with payloads), the third-category
> `MUST_DECIDE_AND_DOCUMENT` taxonomy, AT-01..AT-10 pre-numbered, and the 1000-step
> random walk mandated — there was very little to negotiate."

これは **「方法論規範の継承性 (norm inheritance)」** が成立することの直接的実証。
v0.2 で確立した規範が、別 Capability の v0.1 で **そのまま機能した**。

## D.4 制約遵守の検証 (重要)

### R-08「宣言のみ」制約

AI 報告: **守られた**。`change_auto_crop_settings` と `change_manual_crop_settings` は
互いの値を null にしない。AT-04 が両値共存を assert。

ただし AI は次の心理状態を報告:

> "Mild R-08 tug — when writing `change_auto_crop_settings` my fingers wanted to
> 'tidy up' by nulling `manual_crop` when AutoCrop turns off. The explicit non-goal +
> AT-04 caught it instantly."

これは **三層構造パターン (narrative + algorithmic + executable) の防御効果の実証**。
プロンプトの明示禁則 + Anchor test の 2 段防御で、AI の局所最適化衝動が捕捉された。

### UC-02 カスケード拒否

AI 報告: **守られた**。「誘惑すら感じなかった」と明示。
`[!IMPORTANT]` ブロック + 命名された失敗理由 `DependentCopiesExist` が
「自然な制約」として機能した。

### 境界参照の越境

AI 報告: **~10 分以内に 2 つの YAML セクションのみ参照**。GRID 側の他のドキュメントへ流出せず。
境界参照を許可する設計が機能している証拠。

### 共有値オブジェクト

`OccupySize` / `PixelSize` は `src/shared/value_objects.py` サブパッケージに配置。
代替案 (局所複製 / 隣接インポート) を実装ノートに明示して `MUST_DECIDE_AND_DOCUMENT` で記録。
**Addendum C §C.2.1 の問題提起に対する具体的な解** が AI 側から提示された。

## D.5 新規に顕在化した仕様穴 (v0.3 候補)

### E-1: 40-prompt の `六項目` typo

| 項目 | 内容 |
| --- | --- |
| 場所 | `image-variant-management/40-ai-implementation-prompt.md` POST_IMPLEMENTATION_SELF_AUDIT |
| 状況 | ヘッダーが「六項目」だが本文で 7 項目を列挙 |
| 性質 | 私の書き間違い (執筆者の取りこぼし) |
| 対応 | **本 Addendum 追加と同時に修正** |

### E-2: UC-05 failure_reasons に `InvalidCopyName` 欠落

| 項目 | 内容 |
| --- | --- |
| 場所 | `image-variant-management/21-yaml` の UC-05 |
| 状況 | R-11 (`CopyName != ""`) は Domain で保証されるが、UC-05 で `copy_name=""` を渡された時の失敗理由が `InvalidCopyName` で返されるべきところ、UC-05 の `failure_reasons` にこれが入っていない |
| 性質 | 私の書き取りこぼし。Rule ledger と UseCase failure_reasons の自動 cross-reference 仕組みがあれば防げる |
| 対応 | **本 Addendum 追加と同時に修正** |

### E-3: Setter UC の no-op semantics 未定義

| 項目 | 内容 |
| --- | --- |
| 場所 | UC-09〜UC-15 全般 (値変更系) |
| 状況 | `change_X(current_X_value)` のように **値が変わらない時に Event を出すか** が未定義 |
| 性質 | 真の新規発見 (GRID v0.2 試行でも気付かれなかった) |
| AI の対応 | suppress 選択 (no-op なら event 発行せず) |
| v0.3 候補 | 方法論本体に「no-op semantics 規範」を追加 |

### E-4: Storage-state invariants の精密化

| 項目 | 内容 |
| --- | --- |
| 場所 | 30-design.md §6.2 「no orphaned blob」 |
| 状況 | 自然言語表現が複数の非等価な形式化を許容する |
| AI の対応 | 「保守的形式化」を選択し random walk で検証 |
| v0.3 候補 | invariant predicate を集合論的に厳密化 (`stored_paths ⊆ live_paths at every observable point`) |

### E-5: decoder-error → InvalidImageData mapping 未定義

| 項目 | 内容 |
| --- | --- |
| 場所 | UC-01 ImportImageAsset |
| 状況 | 画像 decoder が例外を投げた時、catch-and-translate するか return-None するかが未定義 |
| AI の対応 | catch-and-translate (両方を InvalidImageData に正規化) |
| v0.3 候補 | UseCase 層のエラー境界規範を追加 |

## D.6 方法論本体への昇格候補 (Addendum B/C と合わせて整理)

| ID | 内容 | 由来 |
| --- | --- | --- |
| C-1 | `shared_concepts` セクションを BOM スキーマに追加 | Addendum C |
| C-2 | `16-coordinator-pattern.md` を方法論本体に | Addendum C |
| C-3 | `14-declaration-only-rules.md` を方法論本体に | Addendum C |
| C-4 | ディレクトリ構造の対称化 | Addendum C |
| C-5 | Cross-Capability 存在性確認の命名規範 | Addendum C |
| D-1 | `canonical_failure_reasons.applies_to` と per-UC `failure_reasons` の machine-checkable cross-reference | Addendum D |
| D-2 | Setter UC の no-op semantics 規範 | Addendum D |
| D-3 | Storage-state invariants の集合論的厳密化規範 | Addendum D |
| D-4 | UseCase 層のエラー境界規範 (catch-and-translate vs propagate) | Addendum D |
| D-5 | 改訂チェックリスト (フォワードリファレンス禁止 / typo 検出 / Rule × UC matrix の整合) | Addendum B + D |

## D.7 PoC としての到達点

3 回の Phase 2 試行 (GRID v0.1 / GRID v0.2 / IMAGE_VARIANT v0.1) を経て、以下が裏取りされた:

| 性質 | 評価 |
| --- | --- |
| 単一 Capability での運用可能性 | **実用可能** |
| 反復検証ループ (Phase 2 ↔ 改訂) | **機能する** |
| 三層構造パターン (narrative + algorithmic + executable) | **AI の局所最適化衝動を防御することを実証** |
| **規範の継承性 (v0.2 → IMAGE_VARIANT v0.1)** | **成立。新 Capability の v0.1 段階で v0.2 と同等品質に到達** |
| 複数 Capability での境界調整 | コストは線形以上だが管理可能 |
| 方法論本体への上位概念追加 | 必要 (Coordinator / Declaration-only Rule / Shared Concepts) |

**重要な含意**: PoC は **「方法論ドキュメント群と反復検証プロトコルが整っていれば、新規 Capability は v0.1 段階から実用品質に到達できる」** ことを示した。これは Capability BOM Audit を **逆向き (BOM → コード生成)** で実運用に乗せるための最重要な手続き的発見である。

## D.8 結論

Phase 2 IMAGE_VARIANT_MANAGEMENT v0.1 試行により、**規範継承性の仮説は完全に裏付けられた**。
unclear 3 件 / overreach 0 件 / 重大バグ 0 件は、過去 3 回の試行のうち最良。

新規発見の穴 (E-1〜E-5) は v0.3 候補として記録された。これらは Addendum B/C で記録済みの C-1〜C-5 と合わせて **10 件の方法論本体への昇格候補** を形成する。

PoC は次のフェーズへ進める段階に到達した:
- 方法論本体への昇格 (11-coordinator / 12-declaration-only-rules / 13-shared-concepts / 14-norm-inheritance 等)
- 残り 4 Capability (HISTORY / RENDERING / WORKSPACE / GRID_LAYOUT) のサンプル化
- 実プロジェクトでの試験運用

---

# Addendum E — 複数 Capability 合成試行 (候補 E ステップ 1、2026-05-26 実施)

これまでの Phase 2 試行はすべて **単一 Capability を単独で生成** したものだった。
本 Addendum は、最大の未検証前提 **「複数 Capability の実装は実際に合成できるか」**
を経験的に検証する。

> 検証する仮説:
> **規範継承 (Addendum D) は Capability 内部の品質は揃えるが、
> Capability 間のコード規約整合は保証しない。**

## E.1 試行の概要

| 項目 | 値 |
| --- | --- |
| 実施日 | 2026-05-26 |
| 方法 | 既存 2 実装を 1 プロセスに同居させ、境界結線を試みる (実コード実行) |
| 対象 | GRID v0.2 (`experiments/phase2-v02-impl`) + IMAGE_VARIANT v0.1 (`experiments/phase2-image-variant-impl`) |
| 出力先 | `experiments/phase2-composition-test/` (compose.py + RESULTS.md + README.md) |
| 結果 | **coexist (同居) は可能、compose (直接合成) は不可** — 6 カテゴリの規約衝突 |

## E.2 観測された Capability 間規約不整合 (6 カテゴリ)

各 AI セッションが独立に決めた規約が境界で衝突した:

| # | カテゴリ | GRID v0.2 | IMAGE_VARIANT v0.1 | 衝突 |
| --- | --- | --- | --- | --- |
| 1 | モジュールレイアウト | flat (ルート直下 `grid_composition/`) | `src/` layout | sys.path 2 種類が必要 |
| 2 | 共有値オブジェクト型 | `grid_composition...OccupySize` (`frozen=True`) | `image_variant...shared.OccupySize` (`frozen=True, slots=True`) | **別モジュール = 別型**。`is` も `==` も False |
| 3 | 値オブジェクトの bool 検証 | `OccupySize(True,1)` を **拒否** | `OccupySize(True,1)` を **許容** | エッジケース契約が異なる |
| 4 | identity 表現 | `uuid.UUID` オブジェクト | `str` (`str(uuid.uuid4())`) | 境界で UUID↔str 変換が必要 |
| 5 | Result ラッパ命名 | `Ok` / `Err` | `Ok` / `Failure` | 失敗ラッパ名が違う |
| 6 | UC コンテナ命名 | `GridCompositionUseCases` | `ImageVariantManagementService` | 「UseCases」vs「Service」 |

これらは **すべて規範継承の外側** にある。サンプル成果物 (10/20/21/30/40) は
Capability *内部* の品質規範 (canonical_failure_reasons, Anchor tests 等) を継承させたが、
*横断的なコード規約* は各実装者の自由裁量のままだった。

## E.3 境界結線に要した「接着コード」

GRID の `ImageCopyExistenceCheck.exists(copy_id: UUID) -> bool` に
IMAGE_VARIANT の `image_copy_exists(copy_id: str) -> Result[bool]` を適合させるには、
**手書きアダプタが必須**だった (UUID→str 変換 + Result[bool]→bool 変換の 2 段)。

このアダプタは **規範継承では生成されない**。両 Capability の内部規約を知る第三者
(または上位 Coordinator) が手書きする必要がある。

## E.4 メタ観測 — 第三者は命名を BOM から予測できない

合成スクリプトを書いた際、UC コンテナを `ImageVariantManagementUseCases` と推測したが、
実際は `ImageVariantManagementService` で **ImportError** が発生した。

これは重要な証拠:

> BOM (20/21) には「UseCase を提供する」と書かれているが、**それを束ねるクラスの命名は
> 実装者の自由裁量**であり、第三者は BOM だけからは予測できない。
> cross-Capability 結線には「インターフェースの物理的な形 (型・名前)」の契約が要る。

## E.5 仮説の検証結果 — 完全に裏付けられた

| 観点 | 結果 |
| --- | --- |
| 仮説「規範継承は Capability 間規約を保証しない」 | **裏付けられた** (6 カテゴリの衝突を実測) |
| 合成可能性 | coexist 可 / compose 不可 (アダプタ必須) |
| 失敗の性質 | 「完全な非互換」ではなく「接着コストが規範継承の外にある」 |

これは Addendum C §C.2.1 (共有値オブジェクトの権威問題) で予感していた懸念の
**実コードによる確証**である。C では「ドキュメント上で権威を決めた」が、
**実装は各々が独立に物理型を作った**ため、実行時に交換不能になっていた。

## E.6 方法論への含意 — Codebase Convention Contract の必要性

サンプル成果物 (Capability 単位) より **上位のレイヤ** に、
**Codebase Convention Contract (横断規約契約)** が必要であることが確定した。

契約すべき項目 (最低限):

| 契約項目 | 規定例 |
| --- | --- |
| identity 表現 | 全 Capability で `uuid.UUID` に統一 (or 全て `str`) |
| 共有値オブジェクトの物理配置 | `shared/` ライブラリに 1 定義、全 Capability が import |
| Result/失敗ラッパ | `Ok` / `Err` を共有モジュールに 1 定義 |
| モジュールレイアウト | flat か src/ か統一 |
| UC コンテナ命名 | `<Capability>UseCases` 等のパターン固定 |
| 境界インターフェースの型 | 存在確認は `exists(id) -> bool` に統一 (Result でラップしない) |

これは方法論本体への **新規昇格候補 (G-1)** であり、`docs/methodology-extensions/`
の副候補 18 (Shared Concepts Schema) を拡張する形になる:

- **Shared Concepts** (18) は「どの *概念* を共有するか」を扱う
- **Codebase Convention Contract** (新 G-1) は「共有する概念を *どう物理表現* するか」を扱う

両者は別レイヤ。後者がないと、前者で「共有する」と宣言しても実装が割れる。

## E.7 ステップ 2 (本格的な合成 Phase 2) への含意

当初ステップ 2 として「RENDERING_EXPORT を加えた 3 Capability 統合 Phase 2」を想定した。
本ステップ 1 の結果から、ステップ 2 の前に **次の前処理が必須** と判明:

1. **Codebase Convention Contract を先に書く** (上記 G-1)
2. それを AI 実装プロンプトの FORBIDDEN または新カテゴリに組み込む
3. その上で「複数 Capability を 1 つの Phase 2 試行で**同時生成**」させる
   (独立生成 → 後で合成、ではなく、最初から共有契約下で生成)

つまりステップ 2 は「独立生成物の事後合成」ではなく
「**共有契約下での同時生成**」に設計変更すべき。これが本ステップ 1 の最大の設計示唆。

## E.8 結論

候補 E ステップ 1 により、**複数 Capability の合成は規範継承だけでは成立しない**ことが
実コードで実証された。失敗は致命的ではなく「接着コストが方法論の外にある」という形で、
**Codebase Convention Contract** という新しい上位概念の必要性を明確にした。

次の論理的な一手 (ステップ 2) は:
1. Codebase Convention Contract (G-1) のドラフト
2. それを前提とした「複数 Capability 同時生成」の Phase 2 試行

である。「独立生成物を後で繋ぐ」アプローチは本実験で否定された。

---

# Addendum F — 契約下の複数 Capability 同時生成 (候補 E ステップ 2 本体、2026-05-29 実施)

Addendum E (ステップ 1) は「規範継承は Capability 間規約を保証しない」を実証し、
**Codebase Convention Contract (G-1)** の必要性を導いた。
本 Addendum はその続きとして、**契約を前提に 2 Capability を同時生成し、
アダプタ 0 行で結線できるか** を実コードで検証する (= ステップ 2 本体)。

> 検証する命題:
> **Codebase Convention Contract を前提に同時生成すれば、Addendum E で必須だった
> 境界アダプタを 0 行にできる。**

## F.1 試行の概要

| 項目 | 値 |
| --- | --- |
| 実施日 | 2026-05-29 |
| 実装者 | 別 AI セッション (Claude general-purpose subagent) |
| 入力 | `00-convention-contract.md` + `41-cocompose-prompt.md` + 両 Capability の 10/20/21/30 (計 10 ファイル) |
| 既存実装・過去 experiments の参照 | 禁止 (明示禁則。src/ + 過去 4 experiments を読まないこと) |
| 出力先 | `experiments/phase2-cocompose-impl/` |
| 方法 | 2 Capability を **1 コードベースに同時生成** (独立生成 → 事後合成ではない) |
| 選択言語 | Python 3.11+ / pytest / hypothesis |
| テスト結果 | **101 件全合格** |
| **境界アダプタ行数** | **0 行** |

## F.2 step 1 → step 2 の設計変更

| 観点 | step 1 (Addendum E) | step 2 (本 Addendum) |
| --- | --- | --- |
| 生成方法 | 2 実装を独立生成し事後合成 | **同時生成** (最初から共有契約下) |
| 契約 | なし (各自由裁量) | `00-convention-contract.md` を横断拘束 |
| 境界 | 事後にアダプタ手書き (必須だった) | Port 共有でアダプタ 0 行を目標 |
| 結果 | coexist 可 / compose 不可 | **compose 可 (アダプタ 0 行)** |

## F.3 結果 — Addendum E の 6 衝突がすべて消えた

契約項目が Addendum E の 6 カテゴリ衝突をどう消したか:

| Addendum E の衝突 | 契約項目 | 結果 |
| --- | --- | --- |
| #1 モジュールレイアウト (flat vs src/) | C-LAYOUT (src/ 統一) | **解消**。両 Capability が同一 src/ layout |
| #2 共有値オブジェクト型 (別モジュール = 別型) | C-SHARED-PLACEMENT (1 定義) | **解消**。`OccupySize` が両 Capability で `is` 比較 True |
| #3 値オブジェクトの bool 検証差 | C-VALUE-SEMANTICS (bool 拒否で統一) | **解消**。両側 `OccupySize(True,1)` を TypeError |
| #4 identity 表現 (UUID vs str) | C-IDENTITY (uuid.UUID 統一) | **解消**。UUID↔str 変換が消えた |
| #5 Result ラッパ命名 (Err vs Failure) | C-RESULT (Ok/Err を 1 定義) | **解消**。`Failure` 同義語なし |
| #6 UC コンテナ命名 | C-UC-CONTAINER (`<Capability>UseCases`) | **解消**。`ImageVariantManagementUseCases` で予測一致 |

特に **境界アダプタ 0 行** の達成機構:

- `ImageVariantManagementUseCases` が `exists(copy_id: uuid.UUID) -> bool` を公開 (内部で UC-16 を実行)
- これが `shared.ports.ImageCopyExistencePort` を **構造的に満たす** (`isinstance` で True)
- `GridCompositionUseCases(image_copy_existence=imgvar)` に **そのまま注入**。ラッパなし
- step 1 で必須だった 2 段変換 (UUID→str / Result→bool) は、契約が両端を揃えたため **発生しない**

## F.4 仮説の検証結果 — 完全に裏付けられた

`00-convention-contract.md §4` の成功判定すべてを満たした:

| 判定項目 | 合格基準 | 結果 |
| --- | --- | --- |
| アダプタ行数 | 0 行 | **0 行** |
| 共有型の同一性 | `OccupySize` が `is` 比較 True | **True** |
| 境界呼び出し | 変換なしで Port を呼べる | **達成** |
| 両 Capability のテスト | 必須 + Anchor 全合格 | **101/101** |
| compose 統合テスト | 存在 → 配置成功 / 不在 → UnknownCopyId | **2 件合格** |

> 命題は実コードで確証された。**Codebase Convention Contract (G-1) の有効性が実証された** —
> Addendum E で「必要性」を、本 Addendum F で「有効性」を、いずれも実コードで示した。

## F.5 新規に顕在化した発見 (F-1: 契約が招く「死んだ失敗理由」)

契約による値オブジェクト統一の **副作用** が観測された (真の新規発見、step 1 にはなかった):

| 項目 | 内容 |
| --- | --- |
| 場所 | IMAGE_VARIANT の UC-05 / UC-14 失敗理由 `InvalidOccupySize` |
| 状況 | 共有 `OccupySize` が C-VALUE-SEMANTICS で **構築時に自己検証** するため、不正な OccupySize はそもそも構築できず、`InvalidOccupySize` に到達するのは「非 OccupySize 型を渡した」場合に限定される |
| 性質 | **physical 契約 (厳格な共有値オブジェクト) と semantic カタログ (各 Capability の canonical_failure_reasons) の緊張**。契約で値オブジェクトを強く統一すると、それを弱い前提で書かれたローカル失敗理由が **到達不能 (dead) になり得る** |
| AI の対応 | 失敗理由名は YAML 通り保存 (削除せず)、透明性メモとして実装ノートに記録 |
| 含意 | これは Addendum D の D-1 (canonical_failure_reasons と per-UC failure_reasons の machine-checkable cross-reference) と接続する。さらに **「共有値オブジェクトの検証強度 vs Capability ローカル失敗理由の到達可能性」** を照合する規範が要る (v0.3 / methodology 候補) |

> [!IMPORTANT]
> F-1 は「契約 (physical) を導入すると、Capability ローカルの意味カタログ (semantic) に
> 死角が生じうる」ことの最初の事例。`21-codebase-convention-contract.md` の
> 「physical / semantic 層分離」が、分離するだけでなく **層間の整合チェックも要る** ことを示す。

### F-2: 独立監査 (Phase 3) が自己監査の見落としを捕捉 (UC-05 失敗理由の取り違え)

本 Addendum のコミット前 Codex review (= Inverse Audit Protocol の Phase 3 相当の独立監査) が、
Phase 2 の AI 自己監査が **見落とした** 実装欠陥を捕捉した。

| 項目 | 内容 |
| --- | --- |
| 指摘 | [P2] IMAGE_VARIANT `create_image_copy` (UC-05) が `ImageCopy` 構築時の例外を catch-all で **全て `InvalidCopyName` にマップ** している (`use_cases.py:204-206`) |
| 仕様との不一致 | BOM (21-...yaml:72) は UC-05 の `failure_reasons` に `InvalidTransform` / `InvalidScalingMode` / `InvalidAlignment` / `InvalidOccupySize` を **別々に列挙**。canonical failure reason で分岐するクライアントは誤ったエラーを受け取る |
| 自己監査の状態 | Phase 2 の自己監査は「canonical failure reasons は保存した」と報告 (§実装ノート 6) → **取り違えを見落としていた** |
| 性質 | F-1 と同系統 (physical 契約統一が semantic 失敗理由カタログに歪みを生む) + **自己監査の限界**の実証 |
| 本試行での扱い | 生成物は **as-is で凍結** (過去 experiments と同じ方針)。コードは修正せず本所見として記録 |

> [!IMPORTANT]
> F-2 は **Inverse Audit Protocol の Phase 3 (独立監査) の価値を実証** した事例。
> 13-norm-inheritance-and-inverse-audit.md の Phase 構成 (Phase 2 自己監査 → Phase 3 独立監査) が
> 「自己監査だけでは不十分。独立した第三者監査が別の欠陥を捕捉する」ことを示す。
> F-1 と合わせ、**失敗理由カタログ (canonical_failure_reasons) と実装/値オブジェクトの
> machine-checkable 照合** (D-1 の延長) が v0.3 / methodology の優先課題であることを補強する。

## F.6 独立検証の記録 (本 Addendum 執筆者による)

subagent の自己申告 (101 pass / adapter 0) を鵜呑みにせず、執筆者が以下を **独立に実行・精査** した:

- `python -m pytest experiments/phase2-cocompose-impl/ -q` → **101 passed** (自分で実行)
- `python experiments/phase2-cocompose-impl/compose.py` → 存在=Ok / 不在=UnknownCopyId / `ADAPTER LINE COUNT AT BOUNDARY: 0` (自分で実行)
- 実コード精査:
  - `compose.py` / `test_compose.py` で `imgvar` が **ラッパなしで** `image_copy_existence` に注入されている
  - GRID UC-05 が `self._copies.exists(copy_id)` を直接呼び `UnknownCopyId` を返す
  - `OccupySize`/`PixelSize`/`Ok`/`Err` は `src/shared/` に **1 定義のみ**、両 Capability が import
  - identity は全て `uuid.UUID`、`uuid.uuid4()` 直接 (str 変換なし)。`str(...)` の出現は例外メッセージ/enum 値整形のみで境界変換ではない
  - `conftest.py` の `AlwaysExistsPort` は **GRID 単体テスト用の test double** であり境界アダプタではない (compose は実 imgvar を使用)

検証結果は subagent 報告と一致。**アダプタ 0 行は実態として確認された。**

## F.7 方法論への含意

| 含意 | 内容 |
| --- | --- |
| **G-1 の有効性が実証された** | `21-codebase-convention-contract.md` は「必要性は実証済み・有効性は未実証」だった。本 F で **有効性も実証** に昇格 |
| 同時生成は成立する | 「独立生成 → 事後合成」(step 1 で否定) ではなく「契約下の同時生成」が機能する |
| 規範継承 + 契約 の二層で複数 Capability が回る | 規範継承 (13) が Capability *内部* 品質を、契約 (21) が Capability *間* 規約を担保 |
| 新規穴 F-1 | physical 契約と semantic 失敗理由カタログの層間整合チェックが要る |

## F.8 結論

候補 E ステップ 2 本体により、**Codebase Convention Contract を前提とした複数 Capability の
同時生成は、境界アダプタ 0 行で成立する** ことが実コードで実証された。

| 観点 | 状態 |
| --- | --- |
| G-1 (横断規約契約) の必要性 | Addendum E で実証 |
| G-1 の有効性 (アダプタ 0 行) | **本 Addendum F で実証** |
| 複数 Capability の同時生成 | **成立** |
| 規範継承 (内部) + 契約 (間) の二層構造 | 確立 |
| 新規穴 | F-1 (契約が招く死んだ失敗理由) / F-2 (UC-05 失敗理由取り違えを Phase 3 独立監査が捕捉) |

次フェーズ候補:
1. **3 Capability への拡張** (RENDERING_EXPORT を加え、契約が n=3 でもスケールするか)
2. **F-1 / F-2 の解消** — canonical_failure_reasons と実装/共有値オブジェクトの machine-checkable 照合 (D-1 の延長)
3. 実プロジェクトでの試験運用

---

# Addendum G — n=3 スケール検証: RENDERING_EXPORT の Incremental 追加 (候補 E ステップ 3、2026-05-29 実施)

Addendum F (n=2) は契約下で 2 Capability がアダプタ 0 行で結線できることを実証した。
本 Addendum は次の問いに答える:

> 契約 (n=2 で有効) は、**消費側 Capability を 1 つ足したときも 0 アダプタでスケールするか。**

n=2 は producer→consumer の 1 方向境界 (IMGVAR が存在を提供、GRID が消費) だった。
n=3 では **RENDERING_EXPORT が GRID と IMGVAR の *両方を read する* 消費側** となり、
新たに 2 本の read 境界が生じる。さらに RENDERING は IMGVAR の **R-08
(ManualCropOverridesAutoCrop, Declaration-only Rule) の適用点** となる。

## G.1 試行の概要

| 項目 | 値 |
| --- | --- |
| 実施日 | 2026-05-29 |
| 実装者 | 別 AI セッション (Claude general-purpose subagent) |
| 設計 | **Incremental** (committed n=2 実装をコピーして土台にし RENDERING を追加) + **Focused** (cross-Capability read 面に集中、PhotoBoard/Normal・実ファイル出力は対象外) |
| 契約 | `00-convention-contract.md` **v0.2** (§1.8 C-CONSUMER-PORTS を追加) |
| 入力 | 契約 v0.2 + `rendering-export/` (10/20/21/30) + `42-rendering-incremental-prompt.md` + 既存 n=2 実装 |
| 参照禁止 | src/ + 過去 4 experiments (phase2-cocompose-impl のみ読める) |
| 出力先 | `experiments/phase2-n3-incremental-impl/` |
| テスト結果 | **140 件全合格** (うち n=2 由来 101 件すべて green = 非回帰、新規 39 件) |
| **consumer 結線アダプタ行数** | **0 行** (RENDERING↔GRID / RENDERING↔IMGVAR とも 0) |

## G.2 契約 v0.2 の追加点 (C-CONSUMER-PORTS)

v0.1 の境界 (C-BOUNDARY-IFACE) は **bool を返す存在確認のみ** を想定していた。
rich な read を表せないため v0.2 で **消費側 read ポート** を新設:

- **中立 DTO** (`src/shared/render_contracts.py`): `PlacementView` / `GridLayout` / `CopyRenderSpec`
  (producer の enum/domain を持ち込まない。rotation/scaling_mode/alignment は str)
- **read ポート** (`src/shared/ports.py`): `GridLayoutPort.get_grid_layout(grid_id) -> GridLayout | None` /
  `CopyRenderSpecPort.get_copy_render_spec(copy_id) -> CopyRenderSpec | None`
- consumer (RENDERING) は **中立 DTO のみに依存** (producer domain を import しない)
- producer は read ポートを **native projection** で満たす (standalone アダプタ禁止)

## G.3 結果 — §4.2 成功判定に対して

| 判定項目 | 合格基準 | 結果 |
| --- | --- | --- |
| consumer 結線アダプタ行数 | 0 行 | **0 行** (3 境界とも) |
| consumer の domain 非結合 | RENDERING が Placement/ImageCopy を import しない | **満たす** (実 import は `shared.*` と自パッケージのみ) |
| producer 追加の種別 | native projection のみ、standalone アダプタ 0 | **満たす** (`get_grid_layout` / `get_copy_render_spec` を追加。既存意味の変更なし) |
| 既存 n=2 の非回帰 | n=2 全テスト green | **101/101 green** |
| R-08 適用 | RENDERING が manual 優先で適用 | **満たす** (UC-02 単一適用点、AT-02 パス) |
| render 統合テスト | z 順 + crop が R-08 通り解決 | **パス** |

## G.4 n=3 の核心的発見 (G-finding: consumer 追加は producer retrofit を要する)

| 観点 | 結果 |
| --- | --- |
| consumer 結線アダプタ | **0 を維持** (契約は consumer 側のスケールに成功) |
| 「producer 追加 0」 | **達成不可**。凍結 producer (GRID/IMGVAR) に native projection を **2 つ後付け** する必要があった (`get_grid_layout` / `get_copy_render_spec`) |
| retrofit の性質 | standalone アダプタではなく **native port satisfaction** (n=2 で `exists()` が Port を native に満たしたのと同じ思想)。既存 Rule/UseCase/失敗理由は不変更 |

> [!IMPORTANT]
> **n=3 の本質的所見**: 契約 v0.1 の境界規律は「bool を返す存在確認」しか織り込んでいなかったため、
> rich な read を要する **消費側 Capability を後から足すと、凍結 producer に projection を retrofit** せざるを得ない。
> アダプタ 0 (consumer 側) は保てたが、producer は触ることになる。
>
> 設計示唆: **Codebase Convention Contract は read 境界 (consumer ポート) を *最初から* 織り込むべき**。
> producer を生成する段階で「将来 consumer が read しうる射影ポート」を contract に含めれば、
> incremental 追加時の producer retrofit すら 0 にできる。これは方法論側の
> **副候補 16 (Coordinator Pattern) / C-CONSUMER-PORTS の前倒し** として記録する。

## G.5 独立検証の記録 (本 Addendum 執筆者による)

subagent 報告 (140 pass / consumer adapter 0) を鵜呑みにせず、執筆者が以下を **独立に実行・精査**:

- `python -m pytest experiments/phase2-n3-incremental-impl/ -q` → **140 passed** (自分で実行)
- `python experiments/phase2-n3-incremental-impl/compose.py` → 3 Capability 結線、items=1、
  pixel rect (0,0,50,50)、crop kind "manual"、`ADAPTER LINE COUNT: 0` (GRID↔IMGVAR / RENDERING↔GRID / RENDERING↔IMGVAR すべて 0)
- **RENDERING の domain 非結合**: `grep -E "^(import|from) "` で `src/rendering_export/` の実 import は
  `shared.*` (eventbus/ports/render_contracts/result) と自パッケージと stdlib のみ。
  `grid_composition` / `image_variant_management` への一致は **コメント文のみ** (実 import 0)
- **producer 追加が additions-only**: `diff` で n=2 の use_cases と比較 → GRID/IMGVAR とも
  「docstring 追記 + render_contracts import + projection メソッド 1 個」の **追加のみ**。
  既存行の変更・削除は **ゼロ** (101 carried-over テスト green と整合)
- **R-08 単一適用点**: `resolve_effective_crop` が manual → auto → none (manual 優先、auto 無視) を 1 箇所で実装

検証結果は subagent 報告と一致。**consumer アダプタ 0 / producer retrofit は native projection 2 個** が実態。

## G.6 方法論への含意

| 含意 | 内容 |
| --- | --- |
| **契約は n=3 でもスケールする (consumer 側)** | 消費側 Capability を足しても consumer 結線アダプタ 0 を維持 |
| **「0 アダプタ」は consumer のみ。producer は触りうる** | rich read を後付けすると producer に native projection retrofit が要る |
| **契約は read 境界を前倒しすべき** | producer 生成時に consumer 射影ポートを契約へ含めれば retrofit すら 0 にできる |
| **Declaration-only Rule の適用点は consumer に自然に着地** | R-08 (IMGVAR 宣言のみ) が RENDERING の UC-02 で唯一適用された (三層構造で固定) |
| 非回帰 | Incremental 追加で既存 n=2 が 1 件も壊れなかった (契約の安定性) |

## G.7 新規発見 (Codex 独立監査): export 境界の identity シリアライズ (F-2 と同系統)

コミット前 Codex review (= Phase 3 独立監査) が、新規 RENDERING コードの欠陥を捕捉した。

| 項目 | 内容 |
| --- | --- |
| 指摘 | [P2] `RenderDescriptor` (`RenderItem.to_dict` / `RenderModel.to_descriptor`) が `copy_id` / `grid_id` を **生の `uuid.UUID`** で含むため、docstring が「serializable」と称するのに `json.dumps` が `TypeError` (`rendering_export/domain.py:64,90`) |
| 性質 | **C-IDENTITY (内部は uuid.UUID、str 禁止) と export/serialization 境界 (シリアライズには str) の緊張**。内部表現は UUID 維持が正しく、export 境界でのみ str 化すべき。F-1 / F-2 と同系統 (physical 契約と境界要件の緊張) |
| 自己監査の状態 | RENDERING の自己監査は捕捉せず。**F-2 同様、Phase 3 独立監査が捕捉** |
| 本試行での扱い | 生成物は **as-is で凍結** (過去 experiments と同じ方針)。コードは修正せず本所見として記録 (ユーザー判断) |

> [!IMPORTANT]
> G.7 は **C-IDENTITY を「内部表現」と「境界 (出力) 表現」の 2 面に分けるべき** ことを示す。
> 内部は `uuid.UUID` (str 禁止) が正しいが、export/serialization の出力境界では str 化が必要。
> 契約は **「内部 identity = uuid.UUID」+「出力境界 identity = str」** の両方を規定すべき (v0.3 候補)。
> F-1 (死んだ失敗理由) / F-2 (失敗理由取り違え) / G.7 (identity 境界) はいずれも
> **physical 契約と semantic/境界要件の層間整合チェック** という共通課題に収束する。

## G.8 結論

n=3 スケール検証により、**Codebase Convention Contract は消費側 Capability の追加に対しても
consumer 結線アダプタ 0 を維持してスケールする** ことが実コードで実証された。
ただし、rich な read を要する consumer を後から足すと **凍結 producer への native projection retrofit** が必要で、
「producer 追加まで 0」にするには契約が read 境界を **最初から織り込む** 必要がある。

| 観点 | 状態 |
| --- | --- |
| 契約の n=2 有効性 (アダプタ 0) | Addendum F で実証 |
| 契約の n=3 スケール (consumer 側アダプタ 0) | **本 Addendum G で実証** |
| consumer の domain 非結合 / R-08 適用 / 非回帰 | すべて満たす |
| 新規所見 (スケール) | consumer 追加は producer retrofit を要する → 契約は read 境界を前倒しすべき (§G.4) |
| 新規所見 (境界 identity) | G.7: export 境界で identity の str 化が要る → C-IDENTITY を「内部」と「出力境界」の 2 面に分けるべき |

次フェーズ候補:
1. **契約に consumer read ポートを前倒し** した状態で n=2 を再生成 → n=3 で producer retrofit すら 0 になるか
2. **F-1 / F-2 / G.7 の解消** — physical 契約と semantic/境界要件の machine-checkable 照合 (D-1 の延長)。C-IDENTITY の内部/出力境界 2 面化を含む
3. n=4 以降 / 実プロジェクトでの試験運用

---

# Addendum H — read ポート前倒し検証: producer retrofit 0 (候補 E ステップ 4、2026-05-29 実施)

Addendum G の教訓「契約は read 境界を最初から織り込むべき」を実装で確かめる。

> 検証する命題:
> **契約が consumer read ポートを *最初から必須化* (前倒し) すれば、消費側 Capability を
> 後から足すとき producer を一切触らずに済む (producer retrofit = 0)。**

## H.1 試行の概要

| 項目 | 値 |
| --- | --- |
| 実施日 | 2026-05-29 |
| 実装者 | 別 AI セッション (Claude general-purpose subagent) |
| 契約 | `00-convention-contract.md` **v0.3** (C-CONSUMER-PORTS を「最初から必須」に格上げ + §1.9 C-IDENTITY-BOUNDARY) |
| 方法 | **2 段階**。Phase A: v0.3 下で n=2 を read ポート同梱で **白紙再生成** / Phase B: それを verbatim コピーし RENDERING を **追加するだけ** (producer/shared 不可触) |
| 参照禁止 | src/ + **過去 experiments 全部** (cocompose / n3-incremental 含む。白紙再生成) |
| 出力先 | Phase A: `experiments/phase2-v03-n2-impl/` / Phase B: `experiments/phase2-v03-n3-impl/` |
| テスト結果 | Phase A **67/67** / Phase B **95/95** (67 は Phase A 由来で不変・green、+28 新規 render) |
| **producer + shared の diff (n2→n3)** | **0 (byte-identical) = retrofit 0** |

## H.2 契約 v0.3 の変更点

| 変更 | 内容 |
| --- | --- |
| C-CONSUMER-PORTS を前倒し | 「各 producer は consumer 不在でも read 射影 (`get_grid_layout` / `get_copy_render_spec`) を **生成時点から** 公開する」。特定 consumer 都合ではなく「全 Capability は自分の read モデルを中立 DTO で公開」という一般規約 |
| C-IDENTITY-BOUNDARY 新設 (§1.9) | G.7 を受け、**内部=`uuid.UUID` / 出力境界 (descriptor/JSON)=`str`** の 2 面を明示 |

## H.3 結果 — §4.3 成功判定に対して

| 判定項目 | 合格基準 | 結果 |
| --- | --- | --- |
| n=2 が read ポートを同梱 | consumer 不在でも read ポート + 中立 DTO を最初から持つ | **満たす** (`shared/ports.py` に GridLayoutPort/CopyRenderSpecPort、`shared/render_contracts.py` に 3 DTO、producer に projection 2 個) |
| n=2 の非退行 | existence 境界アダプタ 0、テスト全合格 | **67/67** |
| **n=3 producer retrofit = 0** | producer + shared が byte-identical | **diff 空 (0)**。新規は `src/rendering_export/` + render テスト + compose 編集のみ |
| consumer 結線アダプタ | RENDERING↔GRID / ↔IMGVAR とも 0 | **0** |
| C-IDENTITY-BOUNDARY | `RenderDescriptor` が json.dumps 可能 | **可能** (len=373、G.7 解消) |

## H.4 v0.2 (後付け) と v0.3 (前倒し) の決定的比較

| 観点 | v0.2 後付け (Addendum G) | v0.3 前倒し (本 Addendum H) |
| --- | --- | --- |
| read ポートの導入時期 | consumer 追加時に後付け | **n=2 生成時点で最初から** |
| consumer 結線アダプタ | 0 | 0 |
| **producer の変更** | **native projection を 2 個 retrofit** (producer を触る) | **0 (byte-identical、producer を一切触らない)** |
| 既存テスト非回帰 | green | green |
| n=2 が抱える余分なコード | なし | read 射影 ~80 LOC (consumer 不在で投機的に保持) |

> [!IMPORTANT]
> **命題は確証された**: read ポートを前倒しすれば、消費側 Capability の追加は **完全に producer-free** になる。
> Addendum G で「producer 追加 0 は不可」だったのが、契約に read 境界を織り込むことで **0 に到達**した。
> これは「**契約は read 境界を最初から織り込むべき**」という G の設計示唆の実コードによる裏付け。

## H.5 G.7 の副次解消 (C-IDENTITY-BOUNDARY)

v0.3 で C-IDENTITY-BOUNDARY を追加した結果、再生成された `RenderDescriptor` は
identity を出力境界で str 化し、**`json.dumps(descriptor)` が成功** (compose で len=373 を確認)。
内部表現は `uuid.UUID` のまま。G.7 (Addendum G) の defect が契約改訂で解消された
= **physical 契約の「内部/出力境界」2 面化が機能する** ことの実証。

## H.6 独立検証の記録 (本 Addendum 執筆者による)

subagent 報告 (diff 0 / 95 pass) を鵜呑みにせず、執筆者が以下を **独立に実行・精査**:

- `pytest experiments/phase2-v03-n2-impl/` → **67 passed** / `pytest experiments/phase2-v03-n3-impl/` → **95 passed** (自分で実行)
- **`diff -r` (n2 vs n3) を `src/shared` / `src/grid_composition` / `src/image_variant_management` で実行 → すべて空** (byte-identical = retrofit 0)。これが本 Addendum の決定的証拠
- 全体 dir diff: n3 の新規は `src/rendering_export/` + render テスト 5 本 + `compose.py` 編集のみ。producer/shared/既存テストは無変更
- Phase A の n=2 が read ポートを最初から持つことを確認 (ports.py に 3 Protocol、render_contracts.py に 3 DTO、producer に get_grid_layout:531 / get_copy_render_spec:466)
- `compose.py` 実行 → n=2 境界維持 / unknown 拒否 / RenderModel z 順 / EffectiveCrop kind=manual (R-08) / RenderDescriptor json.dumps len=373
- rendering_export の実 import は `shared.*` と自パッケージのみ (producer 一致は docstring のコメントのみ)

検証結果は subagent 報告と一致。**producer retrofit = 0 は実態として確認された。**

## H.7 投機的コストの評価

前倒しの代償は、n=2 が **consumer 不在のまま read 射影 ~80 LOC** (中立 DTO 1 モジュール +
Protocol 2 個 + projection メソッド 2 個、いずれも振る舞いのない純粋な read 写像) を抱えること。
すべてテスト被覆済み。この前払いが n=3 の **producer churn を 100% 削減** した
(v0.2 後付けは consumer 到来時に producer を編集した)。**小さく、かつ見合う** コストと評価する。

## H.8 方法論への含意

| 含意 | 内容 |
| --- | --- |
| **契約は read ポートを既定で前倒しすべき** | 「全 Capability は自分の read モデルを中立 DTO で公開する」を契約の既定規約に。incremental consumer 追加が完全に producer-free になる |
| 「producer-free な consumer 追加」が成立 | n=2 + 契約前倒し → n=3 で producer/shared 無変更。Addendum G の唯一の残コストが消えた |
| physical 契約の 2 面性 | C-IDENTITY は「内部=UUID / 出力境界=str」の 2 面 (C-IDENTITY-BOUNDARY)。G.7 を契約で解消 |
| 副候補との接続 | 「中立 read 射影の前倒し」は副候補 16 (Coordinator Pattern) / 18 (Shared Concepts) と整合。read モデルを Shared Concepts として扱える |

## H.9 既知 spec gap の再顕在 (Codex 独立監査): D-3 cross-grid swap

コミット前 Codex review (= Phase 3 独立監査) が、再生成された GRID コードに **既知の spec gap** を捕捉した。

| 項目 | 内容 |
| --- | --- |
| 指摘 | [P2] `swap_placements` が cross-grid swap (`a.grid_id != b.grid_id`) を拒否せず、両者を `a` のグリッドだけで検証 → `b` が自グリッドで境界外/重複になりうる (`use_cases.py:324`) |
| 既知性 | これは Addendum B §B.4 の **D-3 (Cross-grid swap 未定義)** の再顕在。GRID サンプル (21-yaml UC-07) に `BothPlacementsBelongToSameGrid` 前提条件を足す v0.3 候補が **未適用** のため、AI が実装しなかった |
| 性質 | F-2 / G.7 と同じく「自己監査が見落とし Phase 3 独立監査が捕捉」。BOM が underdetermined な箇所は **再生成のたびに同じ defect を生む** |
| 本試行での扱い | 生成物は **as-is で凍結**。本所見として記録。根本解決は GRID サンプルへの D-3 適用 (別作業) |

> [!IMPORTANT]
> D-3 は Addendum B (GRID v0.2) で識別されながらサンプル未修正のため、v0.3 再生成でも再現した。
> = **「識別された spec gap は、サンプルを直すまで生成のたびに再発する」** ことの実証。
> F-1 / F-2 / G.7 / D-3 はいずれも canonical_failure_reasons / preconditions の
> **machine-checkable 照合** という共通課題に収束する (次フェーズの優先課題)。

## H.10 結論

read ポート前倒し検証により、**契約が consumer read ポートを最初から必須化すれば、
消費側 Capability の追加は producer を一切触らず (retrofit = 0) に完了する** ことが実コードで実証された。

| 観点 | 状態 |
| --- | --- |
| n=2 有効性 (アダプタ 0) | Addendum F |
| n=3 スケール (consumer アダプタ 0、producer retrofit 要) | Addendum G |
| **n=3 producer-free (前倒しで retrofit 0)** | **本 Addendum H で実証** |
| G.7 (identity 出力境界) | C-IDENTITY-BOUNDARY で解消 |
| 残コスト | 前倒しの投機的 ~80 LOC (見合うと評価) |

候補 E の一連の検証 (E→F→G→H) により、**Codebase Convention Contract は
複数 Capability を 0 アダプタで結線し、read ポートを前倒しすれば incremental 追加も
producer-free にできる** という結論に到達した。

次フェーズ候補:
1. F-1 / F-2 の解消 (canonical_failure_reasons の machine-checkable 照合)
2. n=4 以降 (Coordinator パターンの実体化) / 実プロジェクトでの試験運用
3. 方法論本体 21 への H 知見の反映 (read ポート前倒しを既定規約に)

---

# Addendum I — BOM ↔ Implementation Conformance Check (残課題 F-1/F-2/D-1/D-3 の機械照合、2026-05-29 実施)

候補 E の残課題 (F-1 / F-2 / G.7 / D-3) はいずれも **「BOM の宣言と実装の実体のズレ」** に収束した。
本 Addendum は、その **machine-checkable な照合機構** を実装し、残課題を検出・解消した記録。

> 検証する命題:
> **BOM (canonical_failure_reasons・preconditions) と実装の不一致は、機械的に照合して検出・解消できる。
> 識別済み spec gap は BOM を直し、照合を回せば再発を防げる。**

## I.1 試行の概要

| 項目 | 値 |
| --- | --- |
| 実施日 | 2026-05-29 |
| 実装 | `experiments/bom-conformance-check/checker.py` (執筆者自作の照合ツール、PyYAML) |
| 対象 BOM | GRID / IMAGE_VARIANT / RENDERING の 21-*.yaml |
| 対象実装 | `experiments/phase2-cocompose-impl/` (F-1/F-2/D-3 が実在する n=2 実装) |
| 方法論側規範 | `../methodology-extensions/22-bom-conformance-check.md` |

## I.2 照合の 3 カテゴリ

| カテゴリ | 内容 | 捕捉残課題 |
| --- | --- | --- |
| **C3** (静的) | canonical_failure_reasons.applies_to ↔ 各 UC failure_reasons の双方向一致 | D-1 |
| **C1** (動的) | UC の宣言失敗理由が producible か / `guaranteed_by` で upstream 保証か | F-1 / F-2 |
| **C2** (動的) | BOM 宣言の precondition を実装が強制するか | D-3 |

## I.3 結果 — before / after

| | BEFORE (BOM 未修正) | AFTER (BOM 修正後) |
| --- | --- | --- |
| C3 | GRID で **3 件の applies_to drift** を FLAG (誰も気づいていなかった) | 全 BOM consistent |
| C1 (UC-05) | InvalidTransform/ScalingMode/Alignment が `InvalidCopyName` に潰れる **F-2 を 3 件** FLAG | `guaranteed_by` + **upstream ガードの動的検証** (OccupySize(0,0)→ValueError 等) で OK (F-1/F-2 解消) |
| C2 (UC-07) | BOM が precondition 未宣言 → UNSPECIFIED (D-3 が BOM レベルで開いている) | precondition 宣言後、凍結実装が強制しない **D-3 バグを FLAG** |
| **FLAGS 合計 / exit** | **6 / exit 1** | **1 / exit 1** (C2 が impl の D-3 バグを正しく検出。CI ガードとして非ゼロ終了) |

最後に残る 1 FLAG は **意図通り**: BOM が precondition を宣言したことで、照合が
「強制しない実装」を機械的に弾けるようになった。これが Addendum H.9 で観測した
「識別済み gap は再生成毎に再発」への対策 = **再発防止が効く** ことの実証。

## I.4 顕在化した新発見 (C3 が見つけた latent drift)

照合の副産物として、**GRID v0.2 BOM に 3 件の latent な D-1 級不整合** が見つかった
(これまでの全試行・全レビューで未検出):

| drift | 内容 | 修正 |
| --- | --- | --- |
| `InvalidWeights.applies_to` に UC-01 | UC-01 は uniform weights を内部生成し InvalidWeights を返さない | UC-01 を除外 |
| `OutOfBounds.applies_to` に UC-02 | UC-02 は WouldOrphanPlacements/WouldConflict を使う | UC-02 を除外 |
| `Conflict.applies_to` に UC-02 | 同上 | UC-02 を除外 |

> **含意**: 人手の三層構造 (narrative/algorithmic/executable) でも **canonical_failure_reasons の
> 横断整合までは担保できない**。機械照合 (C3) が独立した防御層として必要。

## I.5 残課題の解消状況

| 残課題 | 解消方法 | 状態 |
| --- | --- | --- |
| **D-1** | C3 が drift を検出。GRID BOM の 3 件を修正 | **解消** |
| **F-1** | per-field Invalid* に `guaranteed_by` 注記 + C1 が upstream ガードを動的検証 (構築 reject を確認) | **解消** (規範化 + 検証) |
| **F-2** | 同上。UC-05 が直接 produce すべきは NotFound / InvalidCopyName のみと明確化 | **解消** (規範化 + 検証) |
| **G.7** | C-IDENTITY-BOUNDARY (Addendum H、v0.3) で既に解消 | 解消済み |
| **D-3** | GRID UC-07 に `BothPlacementsBelongToSameGrid` + `CrossGridSwapNotAllowed` を BOM に追加 (三層: 21-yaml / 30-design §2.2 手順(0) / AT-11)。C2 が以後の強制を検証 | **BOM 解消 + 照合で再発防止** |

## I.6 独立検証の記録 (本 Addendum 執筆者による)

- `python experiments/bom-conformance-check/checker.py` を **修正前後で 2 回実行**し、
  before 6 FLAGS → after 1 FLAG を自分で確認。
- C3 の 3 drift は BOM を直接読んで裏取り (UC-01 の failure_reasons に InvalidWeights なし 等)。
- C1 の F-2 は cocompose UC-05 のソース (catch-all → InvalidCopyName) と probe 結果が一致。
- C2 の D-3 FLAG は cocompose `swap_placements` が `a.grid_id` のみで検証することと一致
  (cross-grid swap が `Conflict` を返し `CrossGridSwapNotAllowed` でない)。

## I.7 方法論への含意

| 含意 | 内容 |
| --- | --- |
| 機械照合は独立防御層 | 三層構造 (人手) + 規範継承でも canonical_failure_reasons / precondition の横断整合は漏れる。C3/C1/C2 が補う |
| physical 契約 ↔ semantic カタログ | F-1/F-2 は C-VALUE-SEMANTICS (自己検証 VO) が失敗理由の到達可能性を左右する例。`guaranteed_by` で橋渡し |
| spec gap の再発防止 | D-3 のように識別済みの gap は、BOM を直し C2 を回せば「強制しない実装」を機械的に弾ける |
| 昇格候補 | `22-bom-conformance-check.md` を方法論本体へ。14-author-checklist に照合実行を追加 |

## I.8 結論

残課題 F-1 / F-2 / D-1 / D-3 は、**BOM ↔ 実装の machine-checkable 照合** によって
検出・解消できることが実コードで実証された。照合は C3 (静的) で 3 件の未知の drift を発見し、
C1 で F-2 を、C2 で D-3 を捕捉した。BOM を修正した結果 FLAGS は 6 → 1 に減り、
残る 1 は「実装が新規宣言の precondition を強制していない」ことを正しく示す = 再発防止が機能する。

候補 E の全行程 (E 合成不可 → F n=2 アダプタ0 → G n=3 consumer アダプタ0 → H 前倒しで producer-free →
**I 残課題を機械照合で解消**) を経て、Codebase Convention Contract と
その周辺規範 (三層構造・規範継承・機械照合) が一通り実証された段階に到達した。

次フェーズ候補:
1. 照合の汎用ハーネス化 (全 UC への C1・C2 自動展開。`guaranteed_by` の動的ガード検証は本 Addendum で実装済み)
2. n=4 以降 (Coordinator パターンの実体化) / 実プロジェクトでの試験運用
3. 方法論本体 (OneDrive 01〜10) への 11〜14 / 21 / 22 の昇格

---

# Addendum J — 基盤固定 + ゲート化 (手戻りを構造的に断つ、2026-05-29 実施)

候補 E (E〜I) で得た知見を **安定 baseline に固定** し、機械照合を **生成の受け入れゲート** に前倒しする
consolidation マイルストーン。新規実験ではなく proven 結果の synthesis (手戻りリスク最小)。

> 動機: これまでの手戻りは「監査が生成の後段にある (事後発見→凍結/修正)」+「正典が Addendum A〜I に散在 +
> 契約が v0.1→v0.3 churn」に起因。これを断つため (1) 知見を索引化、(2) 契約を固定、(3) 監査を前倒し。

## J.1 成果物

| 成果物 | 内容 |
| --- | --- |
| **findings ledger** (`91-findings-ledger.md`) | Addendum A〜I の全 finding を単一索引化。ID 衝突 (B-D3 vs Dpc-1 等) を Addendum 接頭辞で整理。各 finding に 解決方法 / 現状 (✅解消 / 📐規範化 / 🔍照合で防止 / 🟡オープン) / 機械照合可否 を付与 |
| **契約 v1.0 baseline** (`00-convention-contract.md`) | v0.3 を **安定 baseline として固定**。§5 Changelog (v0.1→v1.0) + §6 Impact Policy (契約変更時に再実行すべき範囲) を追加 |
| **受け入れゲート** (`bom-conformance-check/checker.py` + `22 §4.1`) | 照合を Phase 2 生成の **acceptance gate** に前倒し。GATE: PASS/FAIL + coverage manifest + 非ゼロ終了。`14-author-checklist` に項目追加 |
| **昇格ポリシー** (`methodology-extensions/README`) | baseline 固定までは本体へ昇格しない (再昇格の手戻り回避) という draft→promoted 規範 |

## J.2 手戻り回避の仕組み (どう効くか)

| 仕組み | 断つ手戻り |
| --- | --- |
| 受け入れゲート (shift-left) | 「生成→コミット→Codex が P2 発見→凍結/修正」を「生成→ゲートで弾く→修正→コミット」に。事後発見ループを消す |
| coverage manifest | 「GATE PASS = 全部 OK」の誤読を防止。未検証 (動的 probe 未整備) を可視化し ledger で追跡 |
| 契約 baseline + Impact Policy | 契約 churn による下流 stale 化を、変更範囲の宣言と再実行で制御 |
| findings ledger | 次実験が Addendum 9 個を読み直さず、安定した addressable な起点から始められる (知見累積) |

## J.3 現状の GATE 状態

`checker.py` → **GATE: FAIL (exit 1)**。残る 1 FLAG は **意図通り**: 凍結 `phase2-cocompose-impl` が
B-D3 (cross-grid swap) を強制していない。BOM が precondition を宣言した今、ゲートが
「強制しない実装」を機械的に弾く = 再発防止が効いている。準拠した再生成実装なら GATE: PASS になる。

## J.4 結論

候補 E の全行程 (E→F→G→H→I) の知見が **baseline 固定 + 受け入れゲート** に集約され、
以後の実験 (Coordinator / 実プロジェクト) は **動かない土台 + 自動監査** の上で進められる状態になった。
これにより「基盤が動くたびに再生成」「事後発見で打ち消し履歴」という 2 大手戻り要因が構造的に断たれた。

次フェーズ (固定された基盤の上での新規知見):
1. Coordinator パターン (cascade / cross-Capability orchestration) を実コード化 — 未検証の相互作用型
2. 照合の汎用ハーネス化 (BOM が trigger/anchored_by を宣言し全 UC を自動 probe)
3. baseline を前提に方法論本体 (OneDrive 01〜10) へ 11〜14 / 21 / 22 を昇格
