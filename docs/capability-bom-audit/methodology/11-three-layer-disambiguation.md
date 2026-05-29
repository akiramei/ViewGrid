# 11 — 三層構造による曖昧さ解消パターン

> **Status: 方法論本体への昇格候補ドラフト**
> 既存 05-rule-ledger.md / 07-overreach-detection.md を拡張する位置づけ

## この文書の目的

Capability BOM サンプル文書だけでは AI 実装者の **解釈が分かれる** ことが避けられない。
本文書では、**narrative (物語) + algorithmic (手順) + executable (実行可能テスト)** の三層で
意図を冗長に表現することで、AI の局所最適化衝動を **多段防御** する設計パターンを示す。

このパターンは Capability BOM Audit v0.2 試行で実証され、IMAGE_VARIANT_MANAGEMENT v0.1 試行で
**AI が "R-08 tug" を率直に報告したケースでの即時捕捉** という形で確認された。

---

## 1. 動機 — なぜ単層では足りないか

### 1.1 単層の典型的弱点

| 単層表現 | 弱点 |
| --- | --- |
| **narrative のみ** (Markdown 解説のみ) | AI が読み流して見落とす。「この一文の本当の意味は?」が伝わらない |
| **algorithmic のみ** (擬似コード仕様のみ) | 何のためか分からない手順は AI が「より綺麗な手順」に最適化しがち |
| **executable のみ** (テストのみ) | テストが何を守っているかが分からない。テストの追加・削除が安全に行えない |

### 1.2 単層が招いた実例 (GRID_COMPOSITION v0.1)

v0.1 では UC-07 (Swap) について次のように書いた:

> R-02: 対象自身 (移動元 / 入れ替え相手) は衝突対象から除外する

これは narrative としては明瞭だが、AI が **「A の新位置と B の新位置が互いに重なるケース」を
取り逃がした**。1000-step random walk テストで初めて顕在化した実バグ。

`「除外する」` という言葉の論理的帰結が、AI の頭の中で完結しなかった。
narrative 単層では意図が伝わりきらなかった例。

---

## 2. パターンの定義

ある意味的決定 (Rule / 制約 / 振る舞い) を文書化するとき、次の **3 層すべて** で同じ内容を冗長に表現する。

### 2.1 三層の定義

| 層 | 形式 | 目的 | 配置例 |
| --- | --- | --- | --- |
| **narrative** | 自然言語の物語 | 「なぜ問題か」「何を守りたいか」を語る | `30-design.md §1 R-XX NOTE` |
| **algorithmic** | 擬似コード / 手順 | 「どう解くか」を機械的に書く | `30-design.md §2.2 workflow_decision` |
| **executable** | テストコード仕様 | 「正解 / 不正解」をテストで固定 | `30-design.md §7 Worked Examples + §8 Anchor Tests AT-XX` |

### 2.2 三層が補完する役割

```text
意味的決定
  │
  ├─ narrative   ──→ AI に「なぜ重要か」を物語る (動機の伝達)
  │
  ├─ algorithmic ──→ AI に「どう実装するか」を手順で示す (実装の指針)
  │
  └─ executable  ──→ AI の実装の正しさを「機械的に判定」する (検証の固定)

   どれか 1 層を AI が読み流しても、他の 2 層が補完する
```

### 2.3 三層の関係

- **narrative は不可欠**: 動機がないと AI は「より綺麗そう」な実装で勝手に最適化する
- **algorithmic は不可欠**: 手順がないと AI は narrative を独自解釈する
- **executable は不可欠**: テストがないと AI が「実装した」と言っても確かめられない
- 3 層は **意味的に等価** であるべき。矛盾する場合は executable が最強の権威

---

## 3. 実証された防御効果 — IMAGE_VARIANT v0.1 試行での AI の率直な報告

Phase 2 IMAGE_VARIANT_MANAGEMENT v0.1 試行で、AI は次の心理状態を実装ノートに記録した:

> "Mild R-08 tug — when writing `change_auto_crop_settings` my fingers wanted to
> 'tidy up' by nulling `manual_crop` when AutoCrop turns off. The explicit non-goal +
> AT-04 caught it instantly."

具体的に何が起きたか:

| 層 | この事例での具体 |
| --- | --- |
| narrative | "本 Capability では AutoCrop / ManualCrop は共存可能、優先関係は RENDERING_EXPORT が解釈" (30-design.md R-08) |
| algorithmic | "ChangeAutoCropSettings は AutoCrop だけ更新、ManualCrop に触れない" (UC-12 仕様) |
| executable | "AT-04: AutoCrop と ManualCrop が共存して保存される" (Anchor Test) |

AI の頭の中:

1. narrative を読む → "なるほど両方残すのか" (理解)
2. 実装に向かう → "でもこの実装は美しくない、AutoCrop OFF にしたら ManualCrop も無効化すれば綺麗" (局所最適化衝動)
3. **AT-04 を見る → "あ、これだとテスト落ちる"** (executable 層が即捕捉)
4. narrative を再読 → "なるほど、共存可能 = 意図的に両方保持なのか" (動機理解)

これは **executable 層が AI の衝動を即座に捕捉し、narrative 層への再アクセスを誘発した** 事例。
単層では発生しなかった効果。

---

## 4. パターンの適用範囲

### 4.1 適用すべき場面

次のいずれかに該当する意味的決定には **必ず三層適用** する:

- **Capability 境界に跨る Rule** (e.g., R-08 ManualCropOverridesAutoCrop)
- **エッジケースで実装が分かれやすい不変条件** (e.g., R-02 Swap の自身排除)
- **「直感的に綺麗そう」な実装が誤った場合** (e.g., UC-02 が cascade 削除しない)
- **複数の妥当な解釈がある手順** (e.g., UC-09 SetOrder のセマンティクス)
- **ユーザー観点とシステム観点で意味が異なる概念** (e.g., 「画像を配置」≠「Placement を作る」)

### 4.2 適用しなくてよい場面

すべてに適用するとドキュメント量が爆発する。次は narrative 単層で十分:

- 明白で直観的な制約 (e.g., `grid_rows >= 1`)
- すでに業界標準で意味が定まった概念 (e.g., "SHA-256 hash")
- Enum の値集合 (Enum 制約は型レベルで保証されるため重複が不要)

判断基準: **「これを単層で書いて、AI が誤解する確率はどれくらいか?」** を執筆者が見積もる。
誤解の可能性が中以上なら三層を採用する。

---

## 5. 三層の配置規範

### 5.1 サンプル成果物内での配置

| 層 | 既定の配置場所 |
| --- | --- |
| narrative | `30-design.md` の Rule Ledger (`§1`) の NOTE ブロック / Worked Examples (`§7`) の Given/When/Then の解説部 |
| algorithmic | `30-design.md` の Decision Specification (`§2`) / `21-yaml` の `notes:` フィールド |
| executable | `30-design.md` の Anchor Tests (`§8`) AT-XX 一覧 / Worked Examples の `Then:` 部 |

### 5.2 相互参照規範

3 層は **互いに参照** する:

- narrative は「詳細は §2.2 / AT-XX 参照」と明示
- algorithmic は「動機は §1 R-XX NOTE 参照」と明示
- executable (Anchor Test) は対応する W-XX (Worked Example) を参照

これにより AI / 人間レビュアーが任意の層から入っても他 2 層へ到達できる。

---

## 6. アンチパターン

### 6.1 三層化したが内容が乖離

3 層を **同じ意味的決定について** 書くこと。
narrative が「除外する」と言い、algorithmic が「除外しない」と言うのは矛盾。
矛盾発見時は YAML を権威として整合させる (cf. 既存 09-ai-audit-prompt-guide.md と整合)。

### 6.2 narrative がコピペ化

algorithmic と executable は機械的に書けるが、narrative は **執筆者が動機を考えて書く** 必要がある。
「同じことを 3 回書く」のではない。

### 6.3 executable をテスト関数名だけで満足する

`test_swap_should_fail_when_overlap` だけでは executable とは呼べない。
Given / When / Then が明確で、テスト関数として独立にコピー可能な形にする。

### 6.4 三層適用しすぎてドキュメント爆発

§4.1 の適用すべき場面に限定する。
全 Rule に適用するとドキュメント量が 3 倍になり、誰も読まなくなる。

---

## 7. 三層適用の判定フロー (執筆者向け)

ある意味的決定について、次の問いに順に答える:

```text
Q1. この決定は明白で AI が一意に解釈するか?
    Yes → narrative 単層で十分
    No  → Q2 へ

Q2. この決定は AI が「より綺麗そう」と判断して別解釈する可能性があるか?
    No  → narrative + algorithmic の二層で足りる
    Yes → Q3 へ

Q3. この決定は実装の正しさを機械的に判定できるか?
    No  → narrative + algorithmic + 限定的な executable (型レベルチェック等)
    Yes → 三層適用 (narrative + algorithmic + executable Anchor Test)
```

実証された事例の判定:

- R-02 Swap edge case → Q3 まで Yes → 三層適用 (NOTE + workflow_decision step (iv) + AT-03)
- R-08 ManualCropOverridesAutoCrop → Q3 まで Yes → 三層適用 (NOTE + UC-12/13 仕様 + AT-04)
- R-03 ImageCopy が Asset を参照 → Q1 で Yes → narrative 単層 + Entity FK 型保証

---

## 8. 既存方法論本体への接続

| 既存文書 | 本パターンとの接続 |
| --- | --- |
| 05-rule-ledger.md | Rule の記録方法を述べているが、**「複数層での記録」** には言及がない。本文書がその補完 |
| 07-overreach-detection.md | Overreach は事後検出。本文書は事前防御 |
| 09-ai-audit-prompt-guide.md | AI への指示は「unclear を許容」と言っているが、**unclear で済まない領域** での三層活用を本文書が補完 |

本文書は **既存方法論本体と矛盾しない**。むしろ既存の Rule ledger 規範に
「実装フェーズでも有効な防御層」として三層構造を追加する位置づけ。

---

## 9. 採用判定

PoC 結果から、本パターンは **方法論本体の正式採用候補** に値する:

| 評価軸 | 結果 |
| --- | --- |
| 実証根拠 | Phase 2 v0.2 / IMAGE_VARIANT v0.1 で AI 防御効果を直接観測 |
| 適用コスト | 限定的 (適用範囲を §4.1 に明示) |
| 既存方法論との整合 | あり (Rule ledger 規範の拡張) |
| 認知負荷 | 中 (執筆者は判定フロー §7 で迷わずに判断可能) |

---

## 10. 関連ドキュメント

- 12-must-decide-and-document.md — 三層では決まらない、AI に決定を委ねる第三カテゴリ
- 14-author-checklist.md — 三層適用判定を含む執筆者向けチェックリスト
- 実証根拠: `docs/capability-bom-audit/evaluation/90-feasibility-notes.md` Addendum B §B.5, Addendum D §D.4
- サンプル実例: `docs/capability-bom-audit/samples/grid-composition/30-design.md` §1 R-02 NOTE / §2.2 UC-07 / §7 W-3 / §8 AT-03
