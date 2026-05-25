# Capability BOM Audit — サンプル成果物 (GRID_COMPOSITION)

> **Sample Version: v0.2** (Phase 2 v0.1 試行で検出された 3 つの仕様穴を塞ぐ改訂)
> v0.1 → v0.2 の変更点は `90-feasibility-notes.md` Addendum A §A.4 + Addendum B を参照

## このディレクトリの目的

本ディレクトリは、**Capability BOM Audit 方法論に従って人間が用意すべき開発成果物のサンプル**である。

ViewGrid の `GRID_COMPOSITION` Capability をモデルケースとして、

> 「要求仕様 + PLM/BOM + 設計書」を AI に渡せば、AI はソフトウェアをプログラミングできるか?

という問いに対する、**入力ドキュメント側の雛形** を提示する。

> [!IMPORTANT]
> このサンプルは ViewGrid 本体の再実装を目的としない。
> 同一 UI の再現も求めない。評価軸は「ユースケースを満たすソフトウェアが生み出せるか」である。

---

## 背景 — なぜこのサンプルが必要か

AI 時代のソフトウェア開発では、

```text
コード生成コスト ≪ コードレビューコスト
```

となり、さらに AI による攻撃 (脆弱性発見・自動エクスプロイト) に対し、
**人間の修正・レビュー速度では飽和する** という防衛上の必然がある。

このため次のシフトが要請される。

| 役割 | 担い手 |
| --- | --- |
| 要求定義・意味境界設計・PLM/BOM 設計 | **人間** |
| コーディング・テスト・レビュー・保守 | **AI** |

このシフトを成立させるには、AI が判断に迷わず実装でき、かつ
**人間が意思決定の所在を追跡可能** な「入力ドキュメント」の形式を確立する必要がある。

本ディレクトリは、その形式の最初の試作である。

---

## 通常の Capability BOM Audit との関係

通常の Capability BOM Audit は **「コード → BOM 観測」** という後付け監査である。
本サンプルは逆方向の **「BOM → コード生成」** が成立するかを問う。

これは対称な問題ではない:

| 方向 | 入力 | 出力 | 検証可能性 |
| --- | --- | --- | --- |
| 監査 | 既存コード | BOM (観測台帳) | コードと突き合わせて検証可能 |
| 生成 | BOM (人間が記述) | コード | 「ユースケースを満たすか」で検証 |

生成方向では、**人間が記述した BOM の十分性** が問われる。
不足や曖昧さは AI の局所最適や恣意的判断として顕在化する。

---

## スコープ

| 項目 | 内容 |
| --- | --- |
| 対象 Capability | `GRID_COMPOSITION` のみ (1 Capability で雛形を完成させ、課題を洗い出す) |
| 関連する隣接 Capability | `IMAGE_VARIANT_MANAGEMENT` (依存元), `HISTORY_MANAGEMENT` (依存先) — 境界のみ宣言 |
| 対象外 | UI レイアウト・色・操作感の指定、技術スタックの強制、ファイル/フォルダ構造の指定 |
| 評価対象 | (i) サンプル成果物そのものの十分性 (ii) この方法論を全 Capability に水平展開する際の人間コスト |

---

## 成果物の構成

```text
docs/capability-bom-sample/
├── README.md                          ← このファイル (全体ナビゲーション)
│
│   ─── GRID_COMPOSITION (v0.2) ───────────────────────────
├── 10-requirements.md                 ← 要求仕様
├── 20-capability-bom.md               ← PLM/BOM (人間可読版)
├── 21-grid-composition.yaml           ← PLM/BOM (機械可読版、正準)
├── 30-design.md                       ← 設計書 (Rule ledger / Worked examples / Anchor tests)
├── 40-ai-implementation-prompt.md     ← AI 実装プロンプト雛形
└── 90-feasibility-notes.md            ← フィージビリティ評価
                                            Addendum A: Phase 2 v0.1 結果
                                            Addendum B: v0.2 改訂効果
                                            Addendum C: 境界調整負荷の実測 (2 Capability ドラフト)

│   ─── IMAGE_VARIANT_MANAGEMENT (v0.1) ──────────────────
└── image-variant-management/
    ├── README.md
    ├── 10-requirements.md
    ├── 20-capability-bom.md
    ├── 21-image-variant-management.yaml
    ├── 30-design.md
    └── 40-ai-implementation-prompt.md
```

### Capability 間の構造的非対称性 (意図された PoC 観察対象)

GRID_COMPOSITION は **トップレベルにフラット配置**、IMAGE_VARIANT_MANAGEMENT は **サブディレクトリ**。
この非対称性は v0.2 時点では「境界調整負荷の一例」として意図的に残されている。

将来的には両方とも `<capability-id>/` サブディレクトリに収め、トップレベルはメタ情報のみとする
再構成が望まれる (`90-feasibility-notes.md` Addendum C §C.2 で詳述)。

### 読み順 (推奨)

人間が成果物の妥当性をレビューする場合:

1. `README.md` (このファイル) — 全体像
2. `10-requirements.md` — 何を作るのか
3. `20-capability-bom.md` — 意味境界と意思決定の所在
4. `30-design.md` — 保証と仕様の詳細
5. `40-ai-implementation-prompt.md` — AI へ渡す指示
6. `21-grid-composition.yaml` — 機械可読の正準データ (差分確認用)
7. `90-feasibility-notes.md` — 評価所見

AI が実装に使う場合の参照順は `40-ai-implementation-prompt.md` 内で指定する。

---

## 実験プロトコル

このサンプルの評価は次の手順で行う想定である。

### Phase 1: 静的レビュー (人間のみ)

- 要求仕様は曖昧でないか
- BOM は意思決定の所在を一意に決めているか
- Rule の保証場所が宣言されているか
- AI に渡したときに「迷う余地」がどこに残っているか

### Phase 2: AI 実装試行 (任意)

- `40-ai-implementation-prompt.md` を AI に渡し、`GRID_COMPOSITION` を実装させる
- 言語・フレームワークは AI に選ばせる (UI 同一性は要求しない)
- 結果の「ユースケース満足度」と「Rule 遵守度」を評価

### Phase 3: 監査による事後検証

- 生成されたコードに対し通常の Capability BOM Audit を逆方向に適用
- 入力 BOM と観測 BOM の差を観測 → サンプルの不足箇所を特定

> 本ディレクトリは Phase 1 の対象物を提供する。Phase 2, 3 は別作業。

---

## このサンプルの位置づけ — 何ではないか

| よくある誤解 | 実際 |
| --- | --- |
| 「AI に丸投げするための設計書」 | 違う。**意味境界と意思決定の所在を人間が固定する** ためのドキュメント |
| 「リファクタ指示書」 | 違う。実装方針 (クラス分割・パターン適用) は規定しない |
| 「ViewGrid を作り直すための仕様書」 | 違う。**同じユースケースを満たす別ソフトウェア** を AI が作れるかを問う |
| 「完全な仕様書」 | 違う。意図的に技術スタック・UI・コード構造は未定義 |
| 「Capability BOM Audit の決定版」 | 違う。**雛形の試作** であり、課題抽出が主目的 |

---

## 関連ドキュメント

- 方法論本体: `~/OneDrive/ドキュメント/Capability BOM Audit/01-10-*.md` (UTF-8)
- ViewGrid 本体: リポジトリルート `README.md`、`docs/user-manual/`
