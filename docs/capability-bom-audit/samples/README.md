# Capability BOM Audit — サンプル成果物 (worked examples)

> **位置づけ**: Capability BOM Audit 方法論に従って **人間が用意すべき開発成果物のサンプル集**。
> ViewGrid の Capability をモデルケースに「要求仕様 + PLM/BOM + 設計書を AI に渡せば
> ソフトウェアを生成できるか」を問う、**入力ドキュメント側の雛形**。

> [!IMPORTANT]
> ViewGrid 本体の再実装や UI 同一性の再現は目的ではない。評価軸は
> 「ユースケースを満たすソフトウェアが生み出せるか」。

## 背景 — なぜ必要か

AI 時代は「コード生成コスト ≪ コードレビューコスト」となり、AI による攻撃に対し
人間の修正速度では飽和する。そこで **人間=要求/意味境界/BOM 設計、AI=実装/テスト/レビュー/保守**
へ役割をシフトする。本サンプルは、AI が迷わず実装でき、かつ人間が意思決定の所在を追跡できる
「入力ドキュメント」の形式の試作である。

## 通常の Capability BOM Audit との関係

通常は **「コード → BOM 観測」** の後付け監査。本サンプルは逆方向 **「BOM → コード生成」** が
成立するかを問う(非対称: 生成方向では人間が書いた BOM の十分性が問われる)。

---

## ディレクトリ構成 (このフォルダ)

```text
docs/capability-bom-audit/samples/
├── README.md                       ← このファイル
├── 00-convention-contract.md       ← 横断規約契約 (v1.0 baseline、複数 Capability 共通の物理規約)
├── grid-composition/               ← GRID_COMPOSITION (v0.2)
│   ├── 10-requirements.md / 20-capability-bom.md / 21-grid-composition.yaml
│   ├── 30-design.md / 40-ai-implementation-prompt.md
├── image-variant-management/       ← IMAGE_VARIANT_MANAGEMENT (v0.1)
│   └── 10 / 20 / 21 / 30 / 40
├── rendering-export/               ← RENDERING_EXPORT (focused v0.1)
│   └── 10 / 20 / 21 / 30
└── prompts/                        ← 複数 Capability の生成プロンプト
    ├── 41-cocompose-prompt.md          (n=2 同時生成)
    ├── 42-rendering-incremental-prompt.md (n=3 後付け)
    └── 43-preloaded-ports-prompt.md    (read ポート前倒し n=2 再生成 → n=3)
```

各 Capability は **対称に `<capability-id>/` subdir** に収めている (旧 v0.2 の GRID 直下配置という
非対称は本再編で解消、`90-feasibility-notes.md` Addendum C §C.2 / Cpc-4)。

> 評価・findings は隣の `../evaluation/`、方法論は `../methodology/`、照合ツールは `../tools/` を参照。

## 各 Capability 内の読み順 (推奨)

1. `10-requirements.md` — 何を作るのか
2. `20-capability-bom.md` — 意味境界と意思決定の所在
3. `30-design.md` — 保証 (Rule ledger) と仕様の詳細 (Worked examples / Anchor tests)
4. `40-ai-implementation-prompt.md` — AI へ渡す指示 (§A をそのまま貼れる)
5. `21-*.yaml` — 機械可読の正準データ (矛盾時は YAML が正)

複数 Capability をまとめて生成する場合は `prompts/` を使う。

## 実験プロトコル (概要)

- **Phase 1 静的レビュー (人間)**: 要求の曖昧さ / BOM の意思決定一意性 / Rule 保証場所 / 迷う余地。
- **Phase 2 AI 実装試行**: `40-...prompt.md` (or `prompts/`) を AI に渡し実装させる。
  完了前に **照合ゲート** (`../tools/bom-conformance-check/checker.py <生成物>/src`) が GATE: PASS であること。
- **Phase 3 事後監査**: 生成コードに逆方向 Capability BOM Audit を適用し、入力 BOM と観測 BOM の差を見る。

詳細プロトコルは `../methodology/13-norm-inheritance-and-inverse-audit.md`、結果は `../evaluation/90-feasibility-notes.md`。

## このサンプルは何ではないか

| 誤解 | 実際 |
| --- | --- |
| AI 丸投げ用の設計書 | 違う。意味境界と意思決定の所在を人間が固定する |
| リファクタ/実装方針指示書 | 違う。クラス分割・パターンは規定しない |
| ViewGrid 作り直しの仕様書 | 違う。同じユースケースを満たす別ソフトを AI が作れるかを問う |
| 完全な仕様書 | 違う。技術スタック・UI・コード構造は意図的に未定義 |

## 関連

- 方法論: `../methodology/` (01〜10 本体 + 11〜14 / 21 / 22 拡張)
- 評価・findings: `../evaluation/90-feasibility-notes.md` / `../evaluation/91-findings-ledger.md`
- 照合ツール: `../tools/bom-conformance-check/`
- 全体ナビ: `../README.md`
