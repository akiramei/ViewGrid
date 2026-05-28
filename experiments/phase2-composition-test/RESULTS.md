# 合成試行の結果 (Phase 2 候補 E ステップ 1)

## 実行ログ

```text
[LAYOUT] 両パッケージの import に成功 (ただし片方は src/ 配下・片方はルート直下で sys.path を 2 種類通す必要があった)
[SHARED-TYPE] GRID.OccupySize is ImgVar.OccupySize ? -> False
[SHARED-EQ] GridOccupySize(2,3) == ImgVarOccupySize(2,3) ? -> False  (frozen dataclass の eq は型も比較する)
[SHARED-VALIDATION] GRID.OccupySize(True, 1) は拒否: TypeError
[SHARED-VALIDATION] ImgVar.OccupySize(True, 1) は通った -> 両者でバリデーション挙動が異なる
[IDENTITY] GRID identity 型 = UUID
[IDENTITY] ImgVar identity 型 = str (id_factory = lambda: str(uuid.uuid4()))
[IDENTITY] GRID は uuid.UUID オブジェクト / ImgVar は str -> 境界で型不一致 (UUID vs str)
[RESULT] GRID は失敗ラッパに 'Err' を使用
[RESULT] ImgVar は失敗ラッパに 'Failure' を使用 -> 名前不一致 (Err vs Failure)
[NAMING] UC コンテナ命名が不一致: GRID='GridCompositionUseCases' / ImgVar='ImageVariantManagementService'
[INTEGRATION] 境界は (a) 引数型 (UUID vs str) (b) 戻り型 (bool vs Result[bool]) で不一致。アダプタ必須。
```

## 観測された不整合 (6 カテゴリ)

| # | カテゴリ | GRID v0.2 | IMAGE_VARIANT v0.1 | 衝突の性質 |
| --- | --- | --- | --- | --- |
| 1 | モジュールレイアウト | flat (`grid_composition/` をルート直下) | `src/` layout (`src/image_variant_management/`) | sys.path を 2 種類通す必要。パッケージ発見規約が不一致 |
| 2 | 共有値オブジェクト型 | `grid_composition.value_objects.OccupySize` (`frozen=True`) | `image_variant_management.shared.value_objects.OccupySize` (`frozen=True, slots=True`) | **別モジュール定義 = 別型**。`is` も `==` も False。構造同一でも交換不可 |
| 3 | 値オブジェクトのバリデーション | `bool` を `int` として拒否 (`isinstance` + bool 除外) | `bool` を通す (bool 除外なし) | `OccupySize(True, 1)` の挙動が分岐。エッジケースの契約が異なる |
| 4 | identity 表現 | `Id = uuid.UUID` (UUID オブジェクト) | `id: str` (`str(uuid.uuid4())`) | 境界で UUID ↔ str 変換が必要 |
| 5 | Result ラッパ命名 | `Ok` / `Err` | `Ok` / `Failure` | 失敗ラッパの名前が違う。`Ok` も別モジュール = 別型 |
| 6 | UC コンテナ命名 | `GridCompositionUseCases` | `ImageVariantManagementService` | 「UseCases」vs「Service」。BOM から予測不能 |

## 境界結線に必要だった「接着コード」

GRID の `ImageCopyExistenceCheck.exists(copy_id: UUID) -> bool` に
IMAGE_VARIANT の `image_copy_exists(copy_id: str) -> Result[bool]` を適合させるには、
**手書きアダプタ**が必須だった:

```python
class ImgVarExistenceAdapter:
    def __init__(self, imgvar_uc: ImageVariantManagementService) -> None:
        self._uc = imgvar_uc

    def exists(self, copy_id) -> bool:        # GRID は uuid.UUID を渡す
        copy_id_str = str(copy_id)            # 変換 1: UUID -> str
        result = self._uc.image_copy_exists(copy_id=copy_id_str)
        return getattr(result, "value", False)  # 変換 2: Result[bool] -> bool
```

このアダプタは **規範継承では生成されない**。両 Capability の内部規約を知る第三者
(または上位 Coordinator) が手書きする必要がある。

## メタ観測: 第三者は命名を予測できない

合成スクリプトを書いた際、UC コンテナを `ImageVariantManagementUseCases` と推測したが、
実際は `ImageVariantManagementService` で **ImportError**。

これは重要な証拠: **BOM (20/21) には「UseCase を提供する」と書かれているが、
それを束ねるクラスの命名は実装者の自由裁量**であり、第三者は BOM から予測できない。
cross-Capability 結線には「インターフェースの物理的な形 (型・名前)」の契約が要る。

## 「coexist yes / compose no」という結論の正確な意味

- **coexist (同居) は可能**: 2 パッケージは import 衝突なく 1 プロセスに同居できた
- **compose (合成) は不可**: 境界を直接結線できず、6 カテゴリの不整合を埋めるアダプタが必要

つまり失敗は「完全な非互換」ではなく「**接着コストが規範継承の外側に存在する**」という形。

## 方法論への含意

規範継承 (13-norm-inheritance) は **Capability 内部** の品質を継承するが、
**Capability 間** のコード規約は各セッションの自由裁量に委ねられ、合成時に衝突する。

これを埋めるには、サンプル成果物より上位のレイヤに
**codebase convention contract (横断規約契約)** が必要:

| 契約すべき項目 | 例 |
| --- | --- |
| identity 表現 | 全 Capability で `uuid.UUID` か `str` か統一 |
| 共有値オブジェクトの物理配置 | `shared/` ライブラリに 1 定義、全 Capability が import |
| Result/失敗ラッパの命名と配置 | `Ok`/`Err` を共有モジュールに 1 定義 |
| モジュールレイアウト | flat か src/ か統一 |
| UC コンテナ命名規約 | `<Capability>UseCases` 等のパターン固定 |
| 境界インターフェースの型 | 存在確認は `exists(id) -> bool` に統一 (Result でラップしない等) |

詳細は `../../docs/capability-bom-sample/90-feasibility-notes.md` Addendum E。
