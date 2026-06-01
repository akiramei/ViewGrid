# Crop Resolver Micro-Pilot — 実行計画 (F-P10、未実施)

> Pattern 1 (再生成) の最初の micro-pilot。対象 = ImageCropResolver + 実効クロップ解決の *意味*。
> 問い: **as-built BOM (+ 最小の gap 補填) から、意味等価な crop resolver を再生成できるか**。
> 前提: `GENERATION-GAP-REPORT.md` (gap 8 次元) / `candidate-targets.md` (対象選定)。

## 1. 目的と方法論
F-P2 の「blind auditor」の **生成版**:
- **生成器 (独立 agent)** に *生成仕様だけ* を渡す。**既存 `ImageCropResolver.cs` を見せない** (見せたら「再生成」でなく「写経」になる)。
- 生成物を **既存 oracle (`ImageCropResolverTests`)** に通し、さらに執筆者が実装と意味差分を取る。
- 「BOM が生成仕様として足りたか」を、生成の成否と *補填が必要だった項目* で測る (d-3 フィードバック)。

## 2. スコープ (物理範囲を明示 — 生成仕様の次元7)
- **再生成する**: `CropFraction` (VO) + `IImageCropResolver` / `ImageCropResolver` (Application サービス)。
- **所与とする (再生成しない・インターフェイスとして渡す)**: 入力型 `ImageCopy`(の AutoCrop/ManualCrop プロパティ)/`ImageAsset`/`ManualCropFraction`/`AutoCropFraction`/`AutoCropSettings`、依存 `IAutoCropBboxResolver`/`IImageStorage`。
- **範囲外**: Skia 走査の実装 (Infra)、EF 永続化、ViewModel、Avalonia UI、SkiaGridImageRenderer 側の crop 適用。

## 3. 生成仕様 = BOM 抜粋 + gap 補填
生成器へ渡す仕様は以下。**「補填」は現 BOM に無く今回追記した項目** = d-3 で BOM スキーマへ戻す候補。

### 3.1 BOM から (既にある)
- IMAGE_VARIANT BOM `IR-04 ManualCropOverridesAutoCrop`: 実効クロップ = ManualCrop 排他優先 → AutoCrop → null。
- `IR-05`: AutoCrop=(TargetColor,Threshold) 両方 / ManualCrop=(x,y,w,h) 4 値 揃った時だけ有効 (null=OFF)。
- `IR-06`: AutoCrop は実画像走査が要り Infra に委譲 (cache 前提)。ManualCrop は走査不要。
- `does_not_own`: 走査実装そのものは持たない。

### 3.2 gap 補填 (現 BOM に欠落 → 今回明示する)
- **[次元2 署名]** `Task<CropFraction?> ResolveAsync(ImageCopy copy, ImageAsset asset, CancellationToken ct = default)`。
- **[次元6 DI]** `ImageCropResolver(IAutoCropBboxResolver autoCropResolver, IImageStorage imageStorage)`。AutoCrop 経路は `imageStorage.ResolveAbsolutePath(asset.StoredRelativePath)` → `autoCropResolver.ResolveAsync(asset.Id, path, settings, ct)`。
- **[次元4 precedence の短絡]** ManualCrop が設定済でも `CropFraction.IsFull()` なら **null を返し、AutoCrop に *落ちない*** (短絡)。AutoCrop 解決結果が `IsFull` 相当 (null) なら null。
- **[次元3 エラー表現]** この層は `ErrorOr` を使わず **`CropFraction?` の null** で「クロップ無効 *または* 解決失敗」を両義的に表す (AutoCrop 走査失敗も null で、結果失敗は例外化しない)。ただし **copy/asset の null 引数は `ArgumentNullException.ThrowIfNull` で throw** する前提ガードがある (結果チャネルは null、前提違反は例外、の使い分け)。
- **[次元5 null 意味]** `copy.ManualCrop`/`copy.AutoCrop` の null = 機能 OFF。
- **[次元1 型メソッド意味]** `CropFraction(double X,Y,Width,Height)` record struct:
  - `Full = (0,0,1,1)`、`IsFull(tol=1e-6)` = X,Y≈0 かつ W,H≈1。
  - `ToPixelBbox(w,h)` = 各値を軸サイズ倍して `Round` → `Clamp(0..軸 / 残り)`。
  - `From(AutoCropFraction)` / `From(ManualCropFraction)` = フィールドの単純写像。

## 4. 等価判定 oracle (次元8)
- **主 oracle (既存・precedence を網羅)**: `tests/ViewGrid.Application.Tests/Services/ImageCropResolverTests.cs` の 5 件 —
  1. both off → null (AutoCrop resolver 不発)
  2. manual wins over auto (+ AutoCrop resolver 不発)
  3. manual full → null (AutoCrop resolver 不発 = 短絡)
  4. manual null → auto fallback
  5. auto resolver null → null
  → 生成物を **このテストファイルのまま** (sut を差し替え) 通す。5/5 green が precedence 等価の必要条件。
- **VO 補強 (要追加判定)**: `CropFraction` の `IsFull(tolerance)` / `ToPixelBbox` (round+clamp) / `From` を直接見る anchor が現状 *無い* (`ManualCropFractionTests` は ManualCropFraction VO のみ)。micro-pilot で `CropFractionTests` を 1 本追加し、生成物の VO method 意味も固定する。
- **意味差分 (執筆者)**: 生成 `ImageCropResolver` と実コードを行レベルでなく *振る舞い* で比較 (分岐順・短絡・I/O 呼び出し条件)。

## 5. 手順
1. **spec 凍結**: §3 を独立した spec ファイル (BOM 抜粋 + 補填) として書き出す。既存実装コードは含めない。
2. **blind generation**: 独立 agent に spec のみ渡し `CropFraction` + `ImageCropResolver` を生成させる (既存 .cs 非開示)。
3. **oracle 実行**: 生成物を別ディレクトリ/別 namespace に置き、`ImageCropResolverTests` の sut を生成物へ差し替えて `dotnet test`。5/5 green を確認。`CropFractionTests` (新規) も。
4. **意味差分レビュー**: 生成物 vs 実装の振る舞い差分を記録 (特に full-manual 短絡・null 両義・I/O 呼び出し条件)。
5. **gap フィードバック (d-3)**: 「§3.2 の補填が無いと生成器が誤った箇所」を列挙し、BOM スキーマへ戻すべき欄 (`signature` / `error_channel` / `vo_method_contract` / `oracle_tests` / `generation_scope`) を提案。

## 6. 成功条件
- **生成成功**: spec のみから生成した crop resolver が oracle 5/5 + 新 `CropFractionTests` green。
- **方法論的成功 (より重要)**: 生成器が誤った/曖昧だった点が **§3.2 の補填項目に正確に対応** すること = 「現 BOM の欠落次元」が実証的に同定される。逆に補填なしでも生成器が当てた項目は「BOM だけで足りた」次元。
- **非目標**: 行単位一致 / Skia 走査の再生成 / 性能等価。

## 7. リスクと guard
- **写経リスク**: 生成器に実装を見せない徹底 (blind)。spec は意味記述に留め、コード断片を貼らない。
- **oracle 過信**: `ImageCropResolverTests` は precedence を網羅するが VO method (round/clamp/tolerance) は手薄 → §4 の `CropFractionTests` 追加で補う。これを怠ると「oracle green だが ToPixelBbox がズレる」を見逃す。
- **両義 null の罠**: 「OFF」と「解決失敗」が同じ null。生成器がここで `ErrorOr` 等の別 channel を選んだら *それ自体が gap の証拠* (次元3) として記録する (誤りでなく所見)。
- **スコープ漏れ**: 生成器が ImageCopy の all-or-nothing getter (IR-05) まで作り込もうとしたら範囲外と判定 (入力型は所与)。

## 8. 位置づけ
F-P10 (本計画の実施) は **✅ 実施済 (2026-06-01)**。結果は `crop-resolver-micro-pilot-RESULT.md` を参照
(生成物は全スイートを drop-in で通過、唯一の発散 = ToPixelBbox の丸めモード未指定 → d-3 へ)。
