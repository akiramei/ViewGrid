# ViewGrid

複数画像を NxM のグリッドキャンバスに配置・合成して 1 枚の PNG に書き出す、
Windows 向けのデスクトップアプリケーション。

![ViewGrid のメインウィンドウ](docs/images/um/um-01-03-main-window-3pane.png)

## 主な特徴

- **NxM グリッド構成** — 任意の行 × 列のキャンバスを作成し、 各セルに画像を配置
- **論理コピー (バリアント)** — 1 枚の画像から複数の「設定違いの使い回し単位」 を作成可能 (トリミング / 回転 / スケーリング等の特性違い)
- **柔軟な配置編集** — D&D / 占有セル NxM (1 枚を複数セルに跨らせる) / ピクセル単位の微調整
- **スケーリング / トリミング / 保護領域** — 各画像に対し UniformContain / UniformCover / Fill 等のスケーリングモード、 自動 / 手動トリミング、 矩形保護領域 (PhotoBoard 出力対応) を設定
- **PhotoBoard 出力** — Normal モード (純粋なグリッド合成) と PhotoBoard モード (装飾枠付き写真ボード風) の 2 系統
- **複数ワークスペース** — DB / 画像 / サムネを物理分離した単位で「仕事用 / 趣味用」 等を混在事故なく切替
- **Undo / Redo + 操作履歴 Flyout** — 主要操作のほぼ全てが取り消し可能
- **日本語 / 英語 UI** — 切替可能

詳しい使い方は [ユーザーマニュアル](docs/user-manual/README.md) ([English](docs/en/user-manual/README.md)) を、 5 分で動かしてみたい場合は [クイックスタート](docs/quickstart.md) ([English](docs/en/quickstart.md)) を参照してください。

## 動作環境

- **OS**: Windows 10 / 11 (x64)
- **ランタイム**: .NET 10 (self-contained 配布も可能)

## 技術スタック

- C# / .NET 10
- [Avalonia 12](https://avaloniaui.net/) — クロスプラットフォーム UI
- [Entity Framework Core](https://learn.microsoft.com/ef/core/) + SQLite — ワークスペース永続化
- [SkiaSharp](https://github.com/mono/SkiaSharp) — ピクセル合成 / PNG 出力
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 基盤

依存ライブラリの完全な一覧とライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照。

## ビルドと実行

```bash
git clone <repository-url> ViewGrid
cd ViewGrid
dotnet build ViewGrid.sln
dotnet run --project src/ViewGrid.Presentation/ViewGrid.Presentation.csproj
```

## テスト

```bash
dotnet test ViewGrid.sln
```

現時点で Core 165 件 + Application 455 件 (計 620 件、 1 件 Skip) の単体テストを含みます。

## アーキテクチャ

クリーンアーキテクチャ + MVVM をベースにした 4 層構成:

| 層 | プロジェクト | 役割 |
|---|---|---|
| Core | `src/ViewGrid.Core` | ドメインモデル / エンティティ / 値オブジェクト / 純関数 |
| Application | `src/ViewGrid.Application` | ViewModel / UseCase / 履歴コマンド |
| Infrastructure | `src/ViewGrid.Infrastructure` | EF Core / SQLite / ファイル IO / Skia レンダラー |
| Presentation | `src/ViewGrid.Presentation` | Avalonia View (AXAML) / DI 起点 |

## ライセンス

[MIT License](LICENSE) — Copyright (c) 2026 akiramei
