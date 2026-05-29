# Capability BOM Audit

AI 駆動開発時代の **監査方法論**。「関与していること」ではなく「**意思決定を所有していること**」を
観測・統制する。既存設計流派 (Clean Architecture / DDD / SOLID 等) を置き換えるのではなく、その上に適用する。

> このフォルダ **1 つで自己完結** している (方法論・サンプル・評価・ツール)。別 AI / 別プロジェクトへは
> このフォルダごと渡せばよい。

## フォルダ構成

| フォルダ | 中身 |
| --- | --- |
| [`methodology/`](methodology/) | 方法論。**本体 01〜10** (canonical) + **拡張 11〜14 / 21 / 22** (PoC 由来、draft) |
| [`samples/`](samples/) | worked examples。`grid-composition/` `image-variant-management/` `rendering-export/` + 横断契約 `00-convention-contract.md` + `prompts/` |
| [`evaluation/`](evaluation/) | `90-feasibility-notes.md` (Addendum A〜J の試行記録) / `91-findings-ledger.md` (全 finding 索引) |
| [`tools/`](tools/) | `bom-conformance-check/` — BOM↔実装の機械照合ツール (受け入れゲート) |

> 生成された実装(記録)はリポジトリ直下の `experiments/` にある(本フォルダ外。凍結記録)。

## 目的別: 別 AI に何を渡すか

### (A) 既存コードを「監査」させる(本来方向: コード → BOM 観測)
→ `methodology/01〜10`(特に `09-ai-audit-prompt-guide.md` がプロンプト雛形、`02/03/04/07` が分類)+ 監査対象コード。**最小セット**。

### (B) BOM から「実装」させる(逆方向 / Phase 2)
→ 対象 Capability 一式 `samples/<capability>/`(`10/20/21/30` + `40-...prompt.md` §A をそのまま貼る)
+ `samples/00-convention-contract.md` + `methodology/21`・`22`。複数 Capability は `samples/prompts/`。
生成後は `tools/bom-conformance-check/checker.py <生成物>/src` を回し **GATE: PASS** を受け入れ条件にする。

### (C) 方法論を「学ばせる / 新しい BOM を執筆させる」
→ `methodology/01〜10` + `11〜14 / 21 / 22`(理解)+ `samples/` を実例参照 + `methodology/14-author-checklist.md` をチェックリストに。

## 読み順 (推奨)

```text
1. methodology/01〜10            ← 方法論の定義 (まずここ)
2. methodology/11〜14            ← 生成方向の防御 (三層 / MUST_DECIDE / 規範継承 / 執筆 checklist)
3. methodology/21, 22           ← 複数 Capability の横断規約契約 + 機械照合
4. samples/<capability>/        ← 具体例で確認
5. evaluation/90, 91            ← 何を実証し何が残っているか
```

各フォルダの `README.md` がさらに詳細を案内する。

## このリポジトリ (ViewGrid) との関係

ViewGrid は本方法論の **PoC**。本フォルダの方法論・サンプル・ツールは ViewGrid を題材に
反復改訂してきた成果で、他プロジェクトにも流用できる(方法論本体 01〜10 は ViewGrid 非依存)。

- PoC の経緯と到達点: `evaluation/90-feasibility-notes.md` / `evaluation/91-findings-ledger.md`
- 旧構成からの移行: 本フォルダは元 `docs/methodology-extensions/` + `docs/capability-bom-sample/`
  + OneDrive の `01〜10` + `experiments/bom-conformance-check/` を統合したもの。
