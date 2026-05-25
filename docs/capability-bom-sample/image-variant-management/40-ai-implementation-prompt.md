# 40 — AI 実装プロンプト雛形 (IMAGE_VARIANT_MANAGEMENT)

> **Version: v0.1** (GRID_COMPOSITION v0.2 で確立した規範を初回から適用)

## A. 完全版プロンプト

```text
あなたは Capability BOM Audit 方法論に従って動作するソフトウェア実装者である。
本タスクで実装する Capability は IMAGE_VARIANT_MANAGEMENT のみである。

== INPUT DOCUMENTS ==
以下を正準入力として扱うこと。

1. docs/capability-bom-sample/image-variant-management/10-requirements.md
2. docs/capability-bom-sample/image-variant-management/20-capability-bom.md
3. docs/capability-bom-sample/image-variant-management/21-image-variant-management.yaml
4. docs/capability-bom-sample/image-variant-management/30-design.md

参考 (隣接 Capability の境界条件、必要に応じて):
- docs/capability-bom-sample/20-capability-bom.md (GRID_COMPOSITION)
- docs/capability-bom-sample/21-grid-composition.yaml

YAML と Markdown で矛盾を見つけた場合、YAML が正準。
矛盾を見つけたら実装ノートに必ず明示すること。

== GOAL ==
IMAGE_VARIANT_MANAGEMENT の全 UseCase (UC-01..UC-17) を実装し、
全 Rule (R-01..R-11) を指定された保証場所で保証 (R-08 は宣言のみ)、
全 Event を指定タイミングで発行する。

成功条件:
- 30-design.md §6.1 の必須テストカテゴリが網羅されている
- Anchor tests AT-01..AT-10 すべてパス
- Property-based (1000-step random walk) が実装されパス
- Decision ownership 表に違反する実装がない
- 特に cascade_decision を本 Capability が持っていない

== SCOPE ==
- 対象 Capability: IMAGE_VARIANT_MANAGEMENT のみ
- 隣接 Capability (GRID_COMPOSITION, RENDERING_EXPORT, WORKSPACE_MANAGEMENT, HISTORY_MANAGEMENT) は
  最小スタブで表現
- ProtectedRegion (保護領域) は対象外 (v0.2 候補)
- 画像 decoder の実装は AI 任意 (PIL / Skia / その他)
  ただし「テスト時に decode を mock 可能」であること

== NON-GOALS ==
以下は禁止:

- ViewGrid 既存実装を参照すること
- Capability スコープ超え (例: ProtectedRegion 実装)
- Rule ID / UseCase ID / Event 名 / 失敗理由名の変更
- Decision ownership 違反 (特に cascade_decision を本 Capability が持つ)
- R-08 (ManualCropOverridesAutoCrop) を本 Capability で適用すること
- 自動カスケード削除を UC-02 で実装すること
- DB ユニーク制約に R-02 (hash 一意性) を委ねること
- 「綺麗そう」「責務過多」を理由に Decision の所在を勝手に動かすこと

== CAPABILITY CONTEXT ==
本 Capability の解くべき問題:
1 枚の元画像 (ImageAsset) から設定違いの論理コピー (ImageCopy) を複数生成・編集する。
ImageCopy の意味的権威 (許容設定とその意味) は本 Capability にある。

中核概念:
- 「論理コピー (ImageCopy)」は物理コピーではない。1 つの ImageAsset を共有しつつ
  異なる設定 (Transform / Scaling / Crop 等) を持つ
- hash による重複除去 (R-02) — 同じバイト列の Asset を 2 回取り込んでも物理は 1 つ
- AutoCrop と ManualCrop は共存可能 (R-08 の優先関係は RENDERING_EXPORT が解釈)

== ALLOWED (AI が自由、報告不要) ==
- 言語、フレームワーク、クラス分割、命名、永続化形式、画像 decoder
- イベント発行機構
- ロギング

== MUST_DECIDE_AND_DOCUMENT (AI が決めてよいが実装ノートに明示) ==
- timestamp の時間帯
- Repository の "not found" 表現
- 画像 decoder の選定
- hash 計算の実装 (標準ライブラリ or 専用ライブラリ)
- ImageBlobStorage のスタブ実装方針 (in-memory dict 等)
- AutoCropSettings / ManualCropFraction を集約値オブジェクトとして表現する型
- Enum の表現方式 (Python enum / Rust enum / TS const string 等)

== FORBIDDEN ==
- Rule ID / 名称 (R-01〜R-11)
- UseCase ID / 名称 / 失敗理由名 (canonical_failure_reasons 参照)
- Event 名 / 発行タイミング
- Capability 境界 (20-capability-bom.md §8)
- Decision ownership 表 (20-capability-bom.md §6)
- 用語集 (10-requirements.md §5 — 特に OccupySize / PixelSize は GRID_COMPOSITION と共有)
- Anchor tests (30-design.md §8 AT-01〜AT-10) の期待振る舞い

== OUTPUT FORMAT ==
以下を含む実装一式を生成:

1. ソースコード
   - Domain Model (ImageAsset, ImageCopy, 値オブジェクト)
   - UseCase (UC-01..UC-17)
   - Repository インターフェース + in-memory スタブ
   - ImageBlobStorage スタブ (in-memory dict で可)
   - Event 発行機構

2. テストコード
   - 30-design.md §6.1 の必須テストカテゴリを網羅
   - Anchor tests AT-01..AT-10 を `test_at_01_*` 形式で実装
   - 1000-step random walk (property-based、必須)
   - R-01..R-07, R-09..R-11 を独立にテスト (R-08 は「共存可能」のみ)

3. 実装ノート (IMPLEMENTATION_NOTES.md)
   - Decision ownership 自己監査
   - unclear / suspected_overreach
   - **MUST_DECIDE_AND_DOCUMENT (≥ 5 件)**
   - Anchor tests 合格状況
   - R-08 の「宣言のみ」を実装でどう表現したか (テストで「共存可能」を確認、適用ロジック不在)
   - **境界調整: GRID_COMPOSITION との用語整合 (OccupySize / PixelSize の扱い) をどう実装したか**

4. README
   - ビルド方法、テスト実行方法
   - 言語選定理由

== CONFIDENCE POLICY ==
- 入力から一意に決まらない事項は MUST_DECIDE_AND_DOCUMENT に記録
- Rule 保証場所が複数候補ある場合は suspected_overreach に
- 仕様矛盾を発見したら実装を止めて質問
- R-08 については「本 Capability では宣言のみ」を必ず守る

== POST-IMPLEMENTATION SELF-AUDIT (七項目) ==
1. 各 Rule (R-01..R-07, R-09..R-11) の保証コードが 1 箇所にあるか
2. R-08 が本 Capability の実装に存在しないか (= テストで両値共存のみ確認、適用ロジック不在)
3. 各 UseCase が input → result の単一関数として表現可能か
4. Event 発行が状態変更と独立にテスト可能か
5. UC-02 が cascade 削除を持っていないか (DependentCopiesExist で拒否のみ)
6. Anchor tests AT-01..AT-10 が全パスするか
7. MUST_DECIDE_AND_DOCUMENT 項目を ≥ 5 件、実装ノートに列挙したか

これら自己監査結果を IMPLEMENTATION_NOTES.md に記載すること。
```

---

## B. プロンプトの注意点

### B.1 隣接 Capability の参照範囲

入力ドキュメント 4 件 + 参考 2 件 (GRID_COMPOSITION 側の 20 / 21) を渡す。
**OccupySize / PixelSize の用語整合のためだけ** に参考側を許可する。
他の整合 (例: `ImageCopyExists` を呼ぶ側) は本 Capability 内ですべて閉じる。

### B.2 R-08 の特殊性に対する明示的禁則

`ManualCropOverridesAutoCrop` を本 Capability で適用しないことは、
AI が「賢く」優先関係を実装してしまわないように明示的に Non-goals に挙げる必要がある。

### B.3 Cascade decision の禁則

UC-02 で `DependentCopiesExist` を返すだけ、というのは設計上 **「不便」に見える**。
AI は「自動で派生物も消す方が綺麗」と判断しがち。
これも明示的に Non-goals に挙げる。

---

## C. プロンプト変種

### C.1 言語指定版

技術スタック比較が目的なら ALLOWED に:

```
+ プログラミング言語は Python 3.11+ で実装すること (GRID_COMPOSITION 試行と同条件)
```

を追加。

### C.2 検証専用版

既存実装を入力としてサンプル適合性を検査させる場合は Goal を:

```
GOAL:
  既存実装が本書の Capability BOM に適合しているか観測する。
  特に: R-08 の適用が本 Capability に漏れていないか、UC-02 が cascade 削除を持っていないか。
  コード修正は禁止。
```

に置き換え。これは通常の Capability BOM Audit に戻る。

---

## D. 関連ドキュメント

- `~/OneDrive/ドキュメント/Capability BOM Audit/09-ai-audit-prompt-guide.md` — プロンプト設計原典
- `10/20/21/30-*.md` — 本プロンプトの入力
- `../40-ai-implementation-prompt.md` — GRID_COMPOSITION 側 (規範比較)
- `../90-feasibility-notes.md` Addendum C — 境界調整負荷の評価
