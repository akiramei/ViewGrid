# d-3 — BOM 生成仕様スキーマ提案 (F-P10 で同定した欠落次元を BOM へ戻す)

> 実施 2026-06-01。Pattern 1 分解の最終工程 d-3。
> 入力: `GENERATION-GAP-REPORT.md` (F-P9, 8 次元 gap audit) + `crop-resolver-micro-pilot-RESULT.md` (F-P10, blind 再生成で残った本質ギャップ)。
> 問い: **F-P10 が顕在化させた「生成仕様としての欠落」を、as-built BOM スキーマへどう戻すか**。
> 成果物の性質: スキーマ *提案* (proposal)。実 BOM・実コードは変更しない。worked example の YAML はドキュメント内インラインに留め、恒久 BOM への注入は採否判断後。

---

## 0. 結論先出し

1. **欠落は 5 欄に集約できる**: `vo_method_contract` / `error_channel` / `ctor_guard` / `oracle_tests` / `generation_scope`。F-P9 の 8 次元中、構造的欠落 3 次元 (型メソッド意味 / 署名 / エラー規約) + 補助 2 次元 (oracle / scope) に対応。
2. **だが各 rule に 5 欄を撒くのは over-build** (F-P9 が「BOM 全面格上げ」を明確に否定)。提案は **`generation_overlay` という opt-in ブロック** = 「再生成対象に選んだ型/サービスにだけ」付ける薄い上載せ層。監査地図としての as-built BOM 本体は不変に保つ。
3. **新 finding (d-3 固有)**: 生成仕様には **provenance 軸 `deliberate | as-built-incidental | unresolved`** が要る。F-P10 の丸めギャップ (実装 ToEven / 生成 AwayFromZero) は **誰も選んでいない `Math.Round` 既定の偶発挙動**であり、忠実に as-built を書き写すと「偶然の銀行家丸め」を生成仕様として固定してしまう。provenance タグが **不可視の事故を可視の決定点へ昇格**させる = authoring 層の「AI は意味を所有しない / 未決定は捏造せず surface」原則の生成側への適用。**`as-built-incidental` の権威はモード依存** — as-built 再現モードでは権威 (偶発も忠実再現してよい) だが、**恒久 generation 契約モードでは非権威で block**。恒久契約にするには `deliberate` (人間 sign-off) へ昇格するか、決められないなら `unresolved` として block する (= 採用可能な確定契約は `deliberate` のみ。`unresolved` は block 状態であって契約ではない)。これにより「偶発を確定入力として凍結」を防ぐ (§4.1 で gating 定義。Codex review 反映)。
4. **検証経路 (主張は in-range に限定)**: スキーマが gap を閉じる証明は「丸めモードを `vo_method_contract` に明記 → 再生成が ToEven に収束するか」(= F-P11、別 go-ahead)。**収束は *oracle が踏む in-range 入力に限った等価* であって全域等価ではない**: F-P10 の発散は丸めだけでなく **負の width/height (無効寸法) でも起きる** — 実コードは `Math.Clamp(round, 0, width<0)` が throw、生成物は自前 Clamp が `max<min` を黙って正規化して値を返す。これは oracle が正の寸法しか使わないため隠れていた **第 2 の coverage 盲点**であり、「oracle 盲点が発散を隠す」という本提案の中心テーゼをむしろ補強する (§6)。
5. **二層分離 (PV-2 で得た拡張、§9)**: 生成仕様は **`generation_overlay` (生成入力 = 意味仕様) と `generation_gate` (受入検査 = style/compilation/oracle) の二層**に分かれる。crop は *silent な数値* gap で overlay 補填を要したが、2 例目の PlacementValidator (PV-2) の gap は *loud な style/analyzer* 違反 (file-scoped ns / CA1510 / 重複 using) で、overlay で人間が言語化するより **gate が機械的に担保**する領域だった。**意味 = overlay (oracle 被覆で収束) / style = gate (analyzer・formatter が担保)** という 2 種の安全網が Pattern 1 にはある。

---

## 1. 問題の再掲 — 監査地図は「何を壊すな」、生成仕様は「何を作れ」

現 as-built BOM (GRID / IMAGE_VARIANT) が答えるのは **監査の問い**: owns/does_not_own, rules(+fragile/anchor_test), decision_ownership, boundaries, as_built_divergences。これは「AI 保守が**壊してはいけない**意味」の地図であり、Pattern 2 で production 級と実証済 (F-P1〜P8)。

F-P10 が示したのは、同じ BOM を **生成の問い** に使うと 3 次元が構造的に欠ける、という事実:

| F-P9 が予測した欠落次元 | F-P10 で実際に起きたこと |
| --- | --- |
| **型のメソッド意味** | `ToPixelBbox` の丸めモードが spec に無く、生成器が `AwayFromZero` を選択 → 実装 `ToEven` と中間値で発散 |
| メソッド署名 | (補填で充足。生成物がドロップインでコンパイル) |
| エラー表現規約 | (補填で充足。`CropFraction?` null チャネル + 前提ガード ArgumentNullException を明示したら一致) |

→ 署名とエラー規約は「spec に書けば埋まる」軽微欠落。**残った本質ギャップは「数値メソッドの精密仕様」** = 型のメソッド意味。ここを BOM へ戻すのが d-3 の主眼。残り (oracle 宣言 / 生成範囲) は補助だが、再生成の *合否判定* と *物理境界* に必要なので併せて提案する。

---

## 2. 設計原則 — over-build を避ける (F-P9 の警告の遵守)

F-P9 結論: 「Pattern 1 は『BOM を生成仕様へ全面格上げ』より『対象を絞って欠落次元だけ補う micro-pilot』が低リスク」。authoring 層の `harden-bodyside-design` でも「過剰構築 (archetype 一括追加) を敵対的レビューが是正」した教訓がある。よって d-3 のスキーマ原則:

- **P1 (opt-in)**: 生成欄は **再生成対象に選んだ要素にだけ** 付く `generation_overlay`。全 rule/全 VO には付けない。as-built 本体 (監査地図) は touch しない。
- **P2 (provenance 必須)**: 各 overlay エントリは `provenance: deliberate | as-built-incidental | unresolved` を持つ。`as-built` を機械的に書き写すと偶発挙動が「仕様」に化けるため (§4)。
- **P3 (oracle 接地 + provenance gating)**: 生成欄の主張は **既存テストが固定しているか** を `oracle_tests.coverage_gaps` で自己申告する。テスト未固定の契約 (例: 丸めモード) は次の 3 状態のいずれかを持つ: (a) `deliberate` = 人間が sign-off した確定契約 (両モードで権威)、(b) `unresolved` = 未決定 (両モードで **block**、authoring 層 PROV ゲートと同型。契約ではなく block 状態)、(c) `as-built-incidental` = 現コードがそうしているだけの偶発挙動。**(c) の権威はモード依存** — as-built 再現モードでは権威だが、恒久 generation 契約モードでは非権威 (block) であり、確定契約にするには (a) `deliberate` へ昇格する以外にない (§4.1 で gating 定義)。これにより「テスト未固定の偶発を恒久契約の権威入力として凍結する」自己矛盾を排除。
- **P4 (no churn)**: スキーマ追加は *追記のみ*。既存欄の意味・既存 anchor_test・cross_capability 突合は不変。Step 5 (authoring 層) で再番号を延期したのと同じ churn 回避方針。

---

## 3. スキーマ提案 — `generation_overlay` ブロック (5 欄)

as-built BOM の `derived_runtime` の VO / `services` のサービスに、**任意の** `generation_overlay` を付けられるようにする。形:

```yaml
generation_overlay:
  target: <型名 or サービス名>
  generation_scope: { ... }      # 物理的な再生成範囲
  signature: [ ... ]             # public 表面 (署名)。※軽微欠落、I/F doc で代替可
  error_channel: { ... }         # 失敗をどのチャネルで返すか
  ctor_guard: { ... }            # 前提ガードを ctor に置くか結果メソッド入口のみか
  vo_method_contract: [ ... ]    # ★ 本命: メソッドの精密意味 (丸め/精度/tolerance/境界)
  oracle_tests: { ... }          # 意味等価判定の gate + coverage_gaps
```

### 3.1 `vo_method_contract` (★ 本命 — F-P10 の本質ギャップ)

VO/型の **各メソッドの振る舞い契約**を、フィールドと不変条件とは別に列挙する。現 BOM は「型 = フィールド + 不変条件」しか持たず、「型 = フィールド + **メソッド契約**」になっていない。

```yaml
vo_method_contract:
  - method: "ToPixelBbox(int width, int height) -> (int X,int Y,int W,int H)"
    semantics: "比率を整数ピクセル bbox へ展開。w/h の上限は残り (width-x / height-y) で画像外へはみ出さない。"
    numeric:
      rounding: ToEven            # ★ Math.Round 既定。midpoint (x.5) の挙動を一意化する
      clamp: "[0, axis] / w,h は [0, axis-origin]"
    provenance: as-built-incidental   # ← §4: 誰も選んでいない既定。生成では決定点として surface すべき
  - method: "IsFull(double tolerance = 1e-6) -> bool"
    semantics: "X,Y が 0 近傍 ∧ W,H が 1.0 近傍 (|v|<tol / |v-1|<tol) で true (= クロップ無効)"
    numeric: { tolerance: 1e-6, comparison: "strict <" }
    provenance: deliberate          # tolerance は明示引数 = 意図的
  - method: "From(AutoCropFraction|ManualCropFraction) -> CropFraction"
    semantics: "4 フィールドを恒等写像 (源を意識しない統一型へ)"
    provenance: deliberate
```

**なぜ本命か**: F-P10 で生成が *in-range で* 発散した点がここ。`rounding: ToEven` の一語があれば **in-range (oracle が踏む正寸法・非中間値) は収束**した — ただし全域 conformance には別途 無効寸法 (負の width/height) の precondition 明文化か挙動決定が要る (§6/D2)。「四捨五入」のような自然語は midpoint で多義になる — 数値メソッドは丸め/精度/比較演算子/境界を **機械的語彙** (ToEven/AwayFromZero, strict/inclusive) で固定する欄が要る。

### 3.2 `error_channel` (層ごとの失敗表現)

現 BOM は失敗表現が分散記述 (IR-10 で「plain data」、AR で ErrorOr、resolver で null)。生成器は「この層は失敗をどう返すか」を 1 箇所で読めないと誤チャネル (例: resolver を ErrorOr 化) を選ぶ。**層×型ごとに明示**:

```yaml
error_channel:
  result_failure: "CropFraction? の null"     # 失敗とクロップ無効の両義 (下記 dual_meaning)
  dual_meaning: "null = クロップ無効 OR AutoCrop 走査が解決不能 (resolver→null)。型では区別しない"
  precondition_violation: "ArgumentNullException (copy/asset null)"   # 結果チャネルとは別系統
  never: "ErrorOr / 例外で結果の失敗を表さない (前提ガードの例外を除く)"
```

ViewGrid 全体の層規約 (生成器が層を見分ける baseline。BOM 横断):
- **自己検証 VO** (OccupySize/PixelSize/CellPosition): ctor で `throw` (ArgumentException)。
- **plain-data VO** (CropFraction/ManualCropFraction/AutoCropSettings/RegionRect/ImageTransform): エラーチャネル無し (検証しない。IR-10/ID-9)。
- **UseCase**: `ErrorOr<T>` (失敗理由を canonical 名で)。
- **resolver/一部サービス**: 結果は `null`、前提違反のみ `ArgumentNullException`。

### 3.3 `ctor_guard` (前提ガードの所在)

F-P10 で生成器は ctor に防御的 null チェックを追加 (実装は primary ctor でチェック無し、`ResolveAsync` の ThrowIfNull のみ)。テスト非依存の軽微差だが、規約化しないと生成ごとに揺れる:

```yaml
ctor_guard:
  policy: "前提ガードは結果メソッド入口のみ (ResolveAsync の ArgumentNullException.ThrowIfNull)"
  ctor: "primary ctor、引数 null チェック無し"
  provenance: as-built-incidental   # primary ctor は自動 null チェックしない = これも偶発。低 stakes
  note: "生成器が ctor に防御チェックを足しても挙動等価 (テスト非依存)。揺れを止めるため規約化するだけ"
```

### 3.4 `oracle_tests` (意味等価判定の gate + その盲点)

現 BOM の anchor_test は *fragile 不変条件* の一部のみ。再生成の合否を決めるには「この対象の意味等価 gate となるテスト集合」を宣言し、**かつ何を固定していないか (coverage_gaps)** を自己申告する (F-P5/F-P6 の『隣接テストによるカバレッジ偽陽性』対策の延長):

```yaml
oracle_tests:
  precedence: "tests/ViewGrid.Application.Tests/.../ImageCropResolverTests.cs (5件: ManualCrop>AutoCrop>null, full 短絡, I/O 条件)"
  vo_contract: "tests/ViewGrid.Core.Tests/Entities/CropFractionTests.cs (8件: Full/IsFull/ToPixelBbox/From、F-P10 で追加)"
  coverage_gaps:
    - "ToPixelBbox の midpoint (x.5) 丸めモードは *未被覆* — CropFractionTests は中間値を避けた値で書かれている (→ 丸めギャップが swap で green だった理由)。"
    - "ToPixelBbox の *無効寸法* (負の width/height) も未被覆 — 実コードは Math.Clamp(round,0,負) が throw、生成物は max<min を黙って正規化して値を返す。oracle は正の寸法しか使わず、この発散も隠れる (Codex review で同定した第 2 盲点)。"
    - "→ oracle は precedence/写像/*in-range* clamp/IsFull は固定するが、midpoint 丸めと無効寸法時の挙動 (throw か正規化か) は固定しない。"
  full_conformance_requires: "midpoint property テスト + 無効寸法の precondition/挙動テスト or C# conformance harness (defer 中の既知ギャップ)"
```

### 3.5 `generation_scope` (物理的な再生成範囲)

owns は *論理* 境界。生成には「どのプロジェクトまで生成し、何をモック/所与にし、何を範囲外にするか」の *物理* 範囲が要る:

```yaml
generation_scope:
  generate: [ "Core/Entities/CropFraction.cs", "Application/Services/ImageCropResolver.cs" ]
  given_types: [ AutoCropFraction, ManualCropFraction, AutoCropSettings, ImageCopy, ImageAsset, IImageCropResolver, IAutoCropBboxResolver, IImageStorage ]
  mock_or_delegate: [ "IAutoCropBboxResolver (Infra Skia 走査)", "IImageStorage (パス解決)" ]
  out_of_scope:
    - "Infrastructure.Imaging (SkiaAutoCropBboxResolver/AutoCropCache) — 走査実体は Infra (IR-06)"
    - "CopyPropertiesViewModel.EffectiveCropPreview — UI 再実装 (IO-1)"
    - "SkiaGridImageRenderer.ComputeCropSourceRect — RENDERING 再実装 (IO-1)"
  io1_caveat: >
    ★ generation_scope が ImageCropResolver 単体に閉じる以上、忠実な再生成は IO-1 (crop 優先規則の 3 重実装)
    を *是正しない*。UI/Renderer 側の重複は範囲外として残る。IO-1 解消には別途「再生成 resolver を唯一源に
    寄せて UI/Renderer がそれを呼ぶ」consolidation が要る (= 代替タスク IO-1 是正)。
```

これは IO-1 と直結する重要な気づき: **再生成の物理境界を明示して初めて、「単体を再生成しても跨ぎ drift は残る」ことが仕様として可視化される**。

---

## 4. provenance 規律 — 生成仕様の新軸 (d-3 固有 finding)

**核心**: as-built を機械的に書き写すと、**誰も決めていない偶発挙動が「仕様」に化ける**。F-P10 の丸めはその実例:

- 実装 `ToPixelBbox` は `System.Math.Round(X*width)` を呼ぶ。`Math.Round(double)` の既定 midpoint は **ToEven (銀行家丸め)**。
- これは **作者が選んだのでなく `Math.Round` の既定**。コードレビューで「ToEven にしよう」と決めた形跡は無い (= 偶発)。
- 生成器は「四捨五入」を自然に `AwayFromZero` と解釈 — **人間の直感としてはむしろこちらが自然**。

ここで取りうる 2 つの誤りと、provenance の解:

| 書き方 | 帰結 |
| --- | --- |
| 丸めを記載しない | 生成が発散 (F-P10 で実際に起きた) |
| `rounding: ToEven` とだけ記載 (provenance 無し) | 偶然の銀行家丸めが「仕様」に固定。人間は「これは意図か?」を問えない |
| **`rounding: ToEven, provenance: as-built-incidental`** | 生成は ToEven に収束 (等価) **かつ** 「これは偶発、意図的に決め直す価値あり」を人間に surface |

→ provenance タグは authoring 層の中核原則 (「AI は意味を所有しない」「未決定は捏造せず止まる」) の **生成側への移植**。生成においては「忠実再現できる」ことと「決定として正しい」ことは別物で、`as-built-incidental` はその差を埋める。

**実務的含意**: `as-built-incidental` の項目は、生成 BOM をレビューする人間にとって **「ここを deliberate へ昇格するか unresolved へ落とすか」の TODO リスト** になる。丸めについて言えば — pixel bbox の丸めは ToEven (中心揃いで系統誤差小) が良いか AwayFromZero (直感的) が良いかは実は未決定の設計判断であり、d-3 はそれを *発見* した。

### 4.1 gating — `as-built-incidental` は権威であってはならない (Codex review 反映)

provenance タグを足すだけでは不十分で、**各状態が generation のどのモードで権威を持つか** を決めないと、「偶発挙動を `as-built-incidental` と正直にラベルしつつ、それを ToEven の確定入力として消費」= 提案自身が警告する凍結をそのまま起こす (Codex 指摘)。よって 2 つの生成モードと gating を定義:

| 生成モード | 目的 | 各 provenance の権威 |
| --- | --- | --- |
| **as-built 再現モード** | 既存挙動と *意味等価* な再生成 (F-P10/F-P11 がこれ) | `deliberate` ✅権威 / `as-built-incidental` ✅権威 (再現が目的なので偶発も忠実再現してよい) / `unresolved` ⛔block |
| **恒久 generation 契約モード** | 将来の正典的 generation-spec として固定 | `deliberate` ✅権威 / `as-built-incidental` ⛔**非権威** (block。`deliberate` か `unresolved` へ昇格必須) / `unresolved` ⛔block |

- **再現モード**では `as-built-incidental` を消費してよい — 目的が「今のコードと等価なものを作る」だから、偶発の ToEven も忠実に再現するのが*正しい*。F-P10/F-P11 はこのモード。
- **恒久契約モード**では `as-built-incidental` は block。「偶然 ToEven」を正典の generation-spec に固定するには、人間が「ToEven を選ぶ」(=`deliberate`) か「未決定」(=`unresolved`、ToEven/AwayFromZero を後で決める) と明示せねばならない。これは authoring 層の **PROV ゲート (proposal/unresolved を機械 block)** の生成側への直系移植。
- この 2 モード分離が **「再現は偶発を許す / 正典化は偶発を許さない」** を構造で強制し、§4 冒頭の自己矛盾を解消する。

---

## 5. Worked example — crop resolver の完全な generation_overlay

§3 の断片を統合した、IMAGE_VARIANT_MANAGEMENT BOM の `derived_runtime.CropFraction` と `services.ImageCropResolver` に **付けるとしたら** こうなる、という完全形 (インライン。実 BOM へはまだ注入しない):

```yaml
# IMAGE_VARIANT_MANAGEMENT.as-built.v0.1.yaml に *もし* d-3 overlay を入れるなら:
derived_runtime:
  - name: CropFraction
    # ... (既存 note は不変) ...
    generation_overlay:
      target: CropFraction (ViewGrid.Core.Entities, readonly record struct)
      generation_scope:
        generate: [ "Core/Entities/CropFraction.cs" ]
        given_types: [ AutoCropFraction, ManualCropFraction ]
      signature: [ "static CropFraction Full", "bool IsFull(double tolerance=1e-6)",
                   "(int,int,int,int) ToPixelBbox(int,int)", "static CropFraction From(AutoCropFraction)",
                   "static CropFraction From(ManualCropFraction)" ]
      error_channel: { kind: plain-data, validation: none }   # IR-10/ID-9
      ctor_guard: { policy: "record struct positional ctor、検証なし", provenance: deliberate }
      vo_method_contract:
        - method: "ToPixelBbox"
          numeric: { rounding: ToEven, clamp: "[0,axis] / w,h は [0,axis-origin]" }
          precondition: "width/height は正の整数 (呼出側=画像実寸が保証) — 負値時の挙動は無規定"
          invalid_dimension: { as_built: "Math.Clamp(round,0,負) が throw", generated: "max<min を正規化して値返却", provenance: as-built-incidental, note: "両者発散。oracle 未被覆 (第2盲点)" }
          rounding_provenance: as-built-incidental   # 恒久契約には deliberate/unresolved へ昇格必須 (§4.1)
        - { method: "IsFull", numeric: { tolerance: 1e-6, comparison: "strict <" }, provenance: deliberate }
        - { method: "From×2", semantics: "恒等写像", provenance: deliberate }
      oracle_tests:
        vo_contract: "tests/ViewGrid.Core.Tests/Entities/CropFractionTests.cs (8)"
        coverage_gaps: [ "ToPixelBbox midpoint(x.5) 丸めモード未被覆", "ToPixelBbox 無効寸法(負)時の throw vs 正規化 未被覆" ]

services:
  - name: ImageCropResolver
    # ... (既存 IR-04 への参照は不変) ...
    generation_overlay:
      target: ImageCropResolver (ViewGrid.Application.Services, sealed class : IImageCropResolver)
      generation_scope:
        generate: [ "Application/Services/ImageCropResolver.cs" ]
        mock_or_delegate: [ IAutoCropBboxResolver, IImageStorage ]
        out_of_scope: [ "CopyPropertiesViewModel.EffectiveCropPreview (IO-1)", "SkiaGridImageRenderer (IO-1)" ]
        io1_caveat: "単体再生成は IO-1 の 3 重実装 drift を是正しない"
      signature: [ "ctor(IAutoCropBboxResolver, IImageStorage)",
                   "Task<CropFraction?> ResolveAsync(ImageCopy, ImageAsset, CancellationToken=default)" ]
      error_channel:
        result_failure: "CropFraction? null"
        dual_meaning: "null = クロップ無効 OR AutoCrop 解決不能"
        precondition_violation: "ArgumentNullException (copy/asset)"
        never: "ErrorOr/例外で結果失敗を表さない"
      ctor_guard: { policy: "前提ガードは ResolveAsync 入口のみ (ThrowIfNull)", provenance: as-built-incidental }
      behavior_contract:   # precedence は IR-04 が既に所有。生成向けに短絡/IO条件を明示するのみ
        precedence: "ManualCrop 排他優先 → AutoCrop → null (IR-04 を参照)"
        short_circuit: "ManualCrop 非 null なら AutoCrop を一切参照しない (full→null でも落ちない)"
        io_condition: "AutoCrop 経路のみ imageStorage/autoCropResolver を呼ぶ"
      oracle_tests:
        precedence: "tests/ViewGrid.Application.Tests/.../ImageCropResolverTests.cs (5)"
        coverage_gaps: [ ]   # precedence/短絡/IO 条件は網羅
```

**注**: `behavior_contract` (precedence/短絡/IO 条件) は resolver では IR-04 が既に意味を所有しているので overlay では *参照* に留め重複させない。VO の `vo_method_contract` は IR にも I/F doc にも無かった次元なので overlay が新規に持つ。この **「rule が既に持つ意味は overlay が参照、欠落次元のみ overlay が新規保持」** の切り分けが over-build 回避の実装。

---

## 6. 検証経路 — スキーマが gap を閉じるとどう確かめるか

提案の主張は「`vo_method_contract.rounding: ToEven` を spec に入れれば再生成が *in-range で* 収束する」。これを *証明* するのは F-P11 (enriched spec での再生成、別 go-ahead) だが、**収束する根拠と、収束しない残余**を現データから正確に切り分ける (Codex review でこの切り分けを厳密化):

1. **in-range の発散は単一点** (`ToPixelBbox` の midpoint 丸め) に局在。oracle が踏む正の寸法・非中間値の入力では precedence/短絡/IO/null/IsFull/From/clamp が一致。その単一点の原因は spec 自然語「四捨五入」の多義性ただ一つで、`rounding: ToEven` は機械的に一意。∴ **in-range 等価への収束は構造的にほぼ確実** (残リスクは生成器が `MidpointRounding.ToEven` を正しく綴る翻訳忠実度のみ)。
2. **だが「全域等価」ではない**: 無効寸法 (負の width/height) では実コード=throw / 生成=正規化返却 という第 2 の発散が残る (§3.4 coverage_gaps)。IR-10/ID-9 が crop VO を plain-data (検証なし) とする以上、「上流が妥当寸法を保証」と暗黙前提して全域収束を主張することはできない。
3. ∴ §6 が主張できるのは **「in-range oracle 等価への収束」までで、全域 conformance ではない**。全域には (a) ToPixelBbox の precondition (正寸法) を明文化するか、(b) 無効寸法時の挙動 (throw か正規化か) を `deliberate` で決めるか、のいずれかが先に要る (= §7 D2)。

**完全な反証可能テスト** (F-P11 でやるなら): enriched spec → blind 再生成 → ① midpoint を踏む property test (`ToPixelBbox(2.5px 相当)` 等) ② 無効寸法 (負) の挙動テスト を新規 oracle に追加 → 生成物と実装が **両方その新 oracle を通る** ことを確認。これは 2 つの coverage_gap を埋める oracle 補強でもある (F-P10 の CropFractionTests 追加と同じ型)。**①は丸めを決めれば通り、②は precondition か挙動決定 (D2) を先にしないと『両方通る』が定義できない** — この非対称が「再現は偶発を許すが正典化は許さない」(§4.1) の具体例。

---

## 7. 監査 BOM への影響と採否判断 (人間の決定点)

`generation_overlay` を **実 BOM に注入するか**は churn を伴う設計判断。トレードオフ:

| 採用度 | 内容 | 利 | 害 |
| --- | --- | --- | --- |
| A. 注入しない | 提案ドキュメント止まり | 監査地図を完全に純粋に保つ | 生成の度に spec を別管理。BOM と乖離リスク |
| B. 対象限定注入 (推奨) | 再生成対象に選んだ要素にだけ overlay | 生成と監査が 1 ソース。opt-in で churn 最小 | BOM が二目的化 (監査+生成) し読み手の負荷増 |
| C. 全面格上げ | 全 rule/VO に生成欄 | 完全な生成仕様 | F-P9 が否定した over-build |

**推奨 = B**。ただし注入は「2 例目 (PlacementValidator 等) でも overlay 形が安定する」ことを確認してからが安全 (1 例ではスキーマが crop に過適合する恐れ)。よって d-3 の出口は **「スキーマ提案を確定し、実 BOM 注入は 2 例目で形を枯らした後」**。

人間に残す決定点 (authoring 層と同じく「AI は提案、人間が硬化」):
- D1: overlay を実 BOM に注入するか (A/B/C)。推奨 B、ただし 2 例目後。
- D2: `ToPixelBbox` の 2 つの未決定点を `as-built-incidental` のまま再現するだけにするか、`deliberate` へ昇格するか。これらは実コードの挙動を変えうる **本物の設計判断** で d-3 が発見した: (D2a) midpoint 丸めを ToEven のままにするか AwayFromZero へ*決める*か / (D2b) 無効寸法 (負の width/height) を throw のままにするか、明示 precondition で「正寸法のみ」と契約するか、正規化を*選ぶ*か。恒久 generation 契約にするには §4.1 の gating により D2a/D2b とも block 解除 (昇格) が必須。
- D3: `generation_overlay` を恒久スキーマとするなら canonical methodology (docs 11-22 系) への昇格は churn 方針に従い 2 例目+敵対的レビュー後。

---

## 8. d-3 が確定したこと / スコープ外

**確定 (d-3 成果)**:
- 欠落 5 次元を `generation_overlay` の 5 欄 (+ behavior_contract 参照) に構造化。over-build 回避のため opt-in 設計。
- **新 finding 1**: 生成仕様には provenance 軸 (`deliberate/as-built-incidental/unresolved`) が必須。偶発挙動 (丸め ToEven) を可視の決定点へ昇格させる。authoring 層の provenance 原則の生成側移植。
- **新 finding 2 (gating、Codex review で硬化)**: provenance タグだけでは不十分で、**生成モード (as-built 再現 / 恒久契約) ごとの権威**を定義しないと偶発を凍結する。`as-built-incidental` は再現モードでのみ権威、恒久契約モードでは block (deliberate/unresolved へ昇格必須) = PROV ゲートの生成側移植 (§4.1)。
- **新 finding 3 (第 2 の coverage 盲点、Codex review で同定)**: F-P10 の発散は丸めだけでなく **無効寸法 (負の width/height) でも起きる** (実=throw / 生成=正規化)。oracle が正寸法しか踏まないため隠れていた。→ 収束主張は **in-range 等価に限定**され、全域 conformance には precondition 明文化か挙動決定 (D2) が要る。「oracle 盲点が発散を隠す」テーゼ (F-P5/F-P6 系) を crop でも再確認。
- **新 finding 4 (overlay/gate 二層分離、PV-2=2 例目 PlacementValidator で発見、§9)**: 生成仕様は **生成入力 (`generation_overlay` = 意味仕様) と受入検査 (`generation_gate` = style/compilation/oracle) の二層**に分かれる。crop の本質 gap が *silent な数値発散* で overlay 補填を要したのに対し、PV-2 の gap は *loud な style/analyzer 違反* (IDE0161/CA1510/IDE0005) で **analyzer gate が build 時に決定的に捕捉**した。→ style は overlay で言語化せず gate が機械担保し、§3.4 の oracle も *生成入力* でなく *受入検査* = gate 側へ概念再配置。「意味=overlay / style=gate」が 2 種の安全網。
- crop resolver の完全な worked example (インライン YAML)。
- 検証経路と採否トレードオフ (推奨 B = 対象限定注入、2 例目後) を明示。

**スコープ外 (本提案でやらない)**:
- enriched spec での再生成収束の実証 (= F-P11、別 go-ahead)。
- 実 BOM への overlay 注入 (= 採否 D1 後)。
- 2 例目 (PlacementValidator) での overlay 形の枯らし。
- midpoint property test / C# conformance harness (defer 中の既知ギャップ)。
- 丸めモードの deliberate 化 (= D2、実コード挙動を変えうる人間決定)。

**next 候補の進捗** (d-3 提案後に実施):
- (i) ✅ **F-P12** = enriched spec で再生成し ToEven 収束を *in-range* で反証可能に実証 (midpoint oracle + 全スイート pass、commit 30ddf86)。
- (ii) ✅ **PV-2** = 2 例目 PlacementValidator で overlay 形を枯らした → **§9** (overlay/gate の二層分離を発見、commit 20f9202)。
- (iii) ✅ **IO-1 是正** = crop 優先規則を Core `CropFraction.ResolveEffective` へ単一源化、generation_scope.io1_caveat が示した跨ぎ drift を production で解消 (commit 2bc4aa9)。
- 残: D1 (overlay の実 BOM 注入)、D2a/D2b (丸め・無効寸法の deliberate 化)、IO-3 (Fork が Regions 落とす) の anchor 化。

---

## 9. PV-2 (PlacementValidator) による検証 — overlay / gate の二層分離 (新 finding 4)

> 実施 2026-06-01 (commit 20f9202)。d-3 next 候補 (ii)「2 例目で overlay 形を枯らす」を実施。
> 詳細: `placementvalidator-{micro-pilot-plan,spec,micro-pilot-RESULT}.md` + `generated/PlacementValidator.gen.cs`。

### 9.0 なぜ 2 例目が必要だったか
§7 D1 は「実 BOM 注入は **2 例目で overlay 形が安定することを確認してから**。1 例ではスキーマが crop に過適合する恐れ」とした。PV-2 = `PlacementValidator` (幾何境界 + overlap/conflict + self-exclusion + 判定順序 + 結果オブジェクト error channel + 自己検証 VO 入力) は crop (値変換 + 優先 + 丸め + plain-data VO) と **異質な系統**で、過適合チェックの対象として適切。

### 9.1 意味次元では overlay が汎化した (好結果)
blind 生成 (独立生成器・tool use 0 回 = リポジトリ未参照) → 実 src へ swap → analyzers/style/warnings off で build+test:
- **Core 186 + Application 466/1skip すべて pass** = 判定順序 (null→throw > 非正grid > 上限境界 > overlap > Valid) / self-exclusion / **conflict 同定 (反復順で先)** / row-major まで意味一致。
- **crop と違い silent な数値発散は皆無** — 整数セル演算で丸めが無く、F-P10 の killer gap (`ToPixelBbox` の midpoint) に相当するものがこの幾何コアには存在しない。
- → `behavior_contract` (判定順序 precedence) / `error_channel` (結果オブジェクト) / `ctor_guard` (自己検証 VO 入力) / `vo_method_contract` (OccupiedCells row-major) という overlay の欄は **crop の数値系から validator の幾何系へそのまま汎化**した。過適合は確認されず → D1 の前提条件 (2 例目で安定) を満たす。

### 9.2 だが新しい gap が *別の場所* に出た — style/analyzer 契約
意味は正しいのに、生 artifact は **本番構成 (analyzers ON = 実 CI) で build 失敗**する。原因は 3 つの style/analyzer 違反:

| 違反 | 生成物 | 本番 (実装) | gate の出所 |
| --- | --- | --- | --- |
| **IDE0161** | block namespace `namespace X { }` | file-scoped `namespace X;` | `.editorconfig` `csharp_style_namespace_declarations = file_scoped:warning` → `TreatWarningsAsErrors` で error |
| **CA1510** | `throw new ArgumentNullException(nameof(x))` | `ArgumentNullException.ThrowIfNull(x)` | `AnalysisLevel = latest-recommended` |
| **IDE0005 / CS8019** | 冗長な file `using System.Linq;` | global using (`Directory.Build.props`) のみ | `ImplicitUsings` + `EnforceCodeStyleInBuild` |

**crop の丸めギャップとの本質的対比** (= PV-2 の核心発見):

| 軸 | crop (F-P10) の gap | PV-2 の gap |
| --- | --- | --- |
| 種類 | **silent** な数値発散 (丸め ToEven vs AwayFromZero) | **loud** な style/analyzer 違反 (ns / CA1510 / using) |
| 検出 | oracle が踏まないと隠れる (coverage 盲点。swap が green だった) | analyzer gate が build 時に **必ず** 捕捉 (すり抜け不能) |
| 閉じ方 | overlay に意味次元を補填 (`vo_method_contract.rounding`) | gate (analyzer・formatter) が機械的に正規化 |
| 仕様の所在 | 生成 **入力** (overlay) で記述しないと発散 | 生成 **受入検査** (gate) が担保すれば足る |
| provenance | `as-built-incidental` (誰も決めていない `Math.Round` 既定) | 大半 `deliberate` (`.editorconfig`/`Directory.Build.props` で human が明示済) |

### 9.3 二層分離 — overlay は生成入力、gate は受入検査 (新 finding 4)
PV-2 が示したのは、生成仕様には **役割の異なる 2 層** があるという構造:

- **`generation_overlay` = 生成入力 (what to build)**: 意味・挙動・境界・error channel・ctor policy・numeric 契約。生成器がこれを *読んで* コードを書く。crop の丸めのように **oracle が踏まない意味細部はここで補填しないと silent に発散**する (§4 の provenance 規律が効くのもこの層)。
- **`generation_gate` = 受入検査 (what must pass)**: style/format/analyzer 適合・コンパイル成立・oracle 通過。生成器の出力を *機械的に検査* する。style は overlay で人間が言語化するより gate で機械正規化する方が確実 — **意味と違い oracle 盲点が無く loud に落ちる**から。

**なぜ混ぜないか**: 意味 (人間が言語化し provenance を付ける対象) と機械契約 (formatter/analyzer が自動判定する対象) を overlay に混在させると、読み手が「これは生成器が解釈すべき意味か / gate が機械適用する規約か」を区別できず負荷が増える。工業的にも自然な分離 = **overlay は設計入力 / gate は受入試験 (acceptance test)**。

### 9.4 `generation_gate` スキーマ (追加提案)
```yaml
generation_gate:                 # 生成物の受入検査。overlay (生成入力) と対をなす
  style_contract:
    namespace: file-scoped                              # IDE0161
    null_guard: "ArgumentNullException.ThrowIfNull"     # CA1510
    usings: "global usings (Directory.Build.props) に依存。file 内の重複/未使用 using 禁止"  # IDE0005/CS8019
    enforcement: gate            # ★ 人間が overlay に書くのでなく、機械が出力へ適用/検査
    tools: [ "dotnet format", "analyzers (AnalysisLevel: latest-recommended)" ]
    treat_warnings_as_errors: true
    provenance: deliberate       # .editorconfig / Directory.Build.props で human が明示設定済 (§9.5)
  compilation_contract:
    must: "本番構成 (analyzers ON) で build 成功 — analyzers-off は意味等価の *分離観測* 専用"
  oracle_contract:
    must: "関連 oracle スイート (Core/Application) が pass"
    # ← §3.4 の oracle_tests の *合否判定* はここ。overlay 側には coverage_gaps の自己申告のみ残す (§9.6)
```

対応する overlay 側 (PlacementValidator の意味仕様。`placementvalidator-spec.md` を凝縮):
```yaml
generation_overlay:
  semantic_contract:             # ← §3 の各欄 (vo_method_contract/error_channel/ctor_guard/behavior_contract) を束ねる名 (P4: 再命名は presentational、意味不変)
    behavior_contract:
      check_order: "null→throw > 非正grid→OOB > 上限境界→OOB > overlap(exclude適用)→Conflict > Valid"
      bounds: "上限のみ。下限は CellPosition 自己検証 VO が保証"
      self_exclusion: "excludePlacementId は overlap 走査のみスキップ。境界に無影響"
      conflict_identity: { rule: "existingPlacements 反復順で最初に重複した既存の Id", provenance: as-built-incidental }  # D 候補
    error_channel: { result: "PlacementValidationResult (結果オブジェクト)", precondition: "existing null→ArgumentNullException", never: "ErrorOr/例外で結果失敗を表さない" }
    vo_method_contract:
      - { method: OccupiedCells, semantics: "row-major (dy外/dx内, origin.X+dx, origin.Y+dy)", provenance: deliberate }
```

### 9.5 なぜ style は gate で機械担保できるのか — 既存の human 契約があるから
crop の丸め (`as-built-incidental`) は **誰も決めていない** `Math.Round` 既定で、§4.1 の gating により恒久契約には人間 sign-off (`deliberate` 昇格) が要った。対して style は **すでに human が決めた契約**が `.editorconfig` / `Directory.Build.props` にリポジトリ内 artifact として存在する (file-scoped:warning, AnalysisLevel, TreatWarningsAsErrors)。→ gate は「project の既存 style 契約を生成物へ適用/検査するだけ」で **新たな決定点を生まない**。これが「意味は決定点を孕む (overlay + provenance) が、style は機械担保で足る (gate)」という非対称の本質。

### 9.6 §3.4 oracle_tests の精緻化 — oracle は gate 側 (no-churn 再解釈)
PV-2 は §3.4 の `oracle_tests` の位置づけを精緻化する: oracle は生成器が *読む入力* ではなく、生成物を *検査する受入条件* = 概念的に **gate 側 (`oracle_contract`)**。P4 (no-churn) を守るため §3.4 を全面移動はせず、**役割の再解釈**に留める:
- `generation_overlay` に残すのは `coverage_gaps` の *自己申告* (どの意味細部が oracle 未固定か = overlay で補填すべき箇所のヒント。例: crop の midpoint/無効寸法)。
- 実際の *合否判定* (テスト実行) は `generation_gate.oracle_contract`。
- これで「overlay = 何を作るかの入力 / gate = 何を通すべきかの検査」の役割が一貫する。

### 9.7 PV-2 が D1 (実 BOM 注入) に与える含意
- overlay 形は crop → validator で **安定** (過適合せず汎化) → §7 D1 の「2 例目で形を枯らす」前提は満たされた。**ただし** PV-2 は新たに `generation_gate` 層を要求するので、D1 の注入対象は overlay 単独でなく **overlay + gate の対**になる。
- gate の中身は project 横断 (style は全 BOM 共通の `.editorconfig`/props)。→ gate は各 BOM の overlay に重複コピーせず、**project レベルで 1 つ宣言し overlay から参照**するのが over-build 回避 (§3 の「rule が既に持つ意味は overlay が参照」の原則を style 契約にも適用)。
- 新たな人間決定点 (D 候補): PlacementValidator の **conflict 同定 (反復順で先) を `as-built-incidental` のまま再現するか `deliberate` 化するか** (crop の D2a/D2b と同型。反復順依存は偶発で、min-Guid 等の別規則を *選ぶ* 余地がある)。
