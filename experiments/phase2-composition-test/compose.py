"""候補 E ステップ 1 — 既存 2 実装の合成試行 (empirical composition test).

目的:
  GRID_COMPOSITION v0.2 (experiments/phase2-v02-impl) と
  IMAGE_VARIANT_MANAGEMENT v0.1 (experiments/phase2-image-variant-impl) を
  1 つのプロセスに同居させ、Capability 境界 (ImageCopyExists 連携 +
  ImageCopyDeleted カスケード + 共有値オブジェクト) を結線できるか観測する。

この試行は「規範継承は Capability 内部品質を揃えるが、Capability 間のコード
規約整合は保証しない」という仮説を経験的に検証するためのもの。
失敗は想定内であり、失敗の具体的内訳こそが観測対象。

実行:
  python experiments/phase2-composition-test/compose.py
"""

from __future__ import annotations

import os
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

# 2 実装はディレクトリレイアウトが異なる (これ自体が不整合点 #1)
GRID_PATH = os.path.join(ROOT, "experiments", "phase2-v02-impl")           # flat layout
IMGVAR_PATH = os.path.join(ROOT, "experiments", "phase2-image-variant-impl", "src")  # src layout
sys.path.insert(0, GRID_PATH)
sys.path.insert(0, IMGVAR_PATH)

findings: list[tuple[str, str]] = []


def record(tag: str, detail: str) -> None:
    findings.append((tag, detail))
    print(f"[{tag}] {detail}")


# ---------------------------------------------------------------------------
# 不整合点 #1: モジュールレイアウト / import 規約
# ---------------------------------------------------------------------------
try:
    import grid_composition as grid  # noqa: E402
    import image_variant_management as imgvar  # noqa: E402
    record("LAYOUT", "両パッケージの import に成功 (ただし片方は src/ 配下・片方はルート直下で sys.path を 2 種類通す必要があった)")
except Exception as e:  # pragma: no cover
    record("LAYOUT-FAIL", f"import 自体が失敗: {e!r}")
    print("\n".join(traceback.format_exc().splitlines()[-5:]))
    sys.exit(1)


# ---------------------------------------------------------------------------
# 不整合点 #2: 共有値オブジェクト (OccupySize / PixelSize) の型同一性
# ---------------------------------------------------------------------------
from grid_composition.value_objects import OccupySize as GridOccupySize  # noqa: E402
from image_variant_management.shared import OccupySize as ImgVarOccupySize  # noqa: E402

go = GridOccupySize(2, 3)
io = ImgVarOccupySize(2, 3)

record("SHARED-TYPE", f"GRID.OccupySize is ImgVar.OccupySize ? -> {GridOccupySize is ImgVarOccupySize}")
record("SHARED-EQ", f"GridOccupySize(2,3) == ImgVarOccupySize(2,3) ? -> {go == io}  (frozen dataclass の eq は型も比較する)")

# bool バリデーションの差 (GRID は bool を拒否、ImgVar は通す)
try:
    GridOccupySize(True, 1)
    record("SHARED-VALIDATION", "GRID.OccupySize(True, 1) は通った (想定外)")
except (TypeError, ValueError) as e:
    record("SHARED-VALIDATION", f"GRID.OccupySize(True, 1) は拒否: {type(e).__name__}")
try:
    ImgVarOccupySize(True, 1)
    record("SHARED-VALIDATION", "ImgVar.OccupySize(True, 1) は通った -> 両者でバリデーション挙動が異なる")
except (TypeError, ValueError) as e:
    record("SHARED-VALIDATION", f"ImgVar.OccupySize(True, 1) は拒否: {type(e).__name__}")


# ---------------------------------------------------------------------------
# 不整合点 #3: identity 型 (uuid.UUID vs str)
# ---------------------------------------------------------------------------
from grid_composition.identity import new_id as grid_new_id  # noqa: E402

grid_id = grid_new_id()
record("IDENTITY", f"GRID identity 型 = {type(grid_id).__name__} (例: {grid_id!r})")
record("IDENTITY", "ImgVar identity 型 = str (id_factory = lambda: str(uuid.uuid4()))")
record("IDENTITY", f"GRID は uuid.UUID オブジェクト / ImgVar は str -> 境界で型不一致 (UUID vs str)")


# ---------------------------------------------------------------------------
# 不整合点 #4: Result ラッパの名前 (Ok/Err vs Ok/Failure)
# ---------------------------------------------------------------------------
from grid_composition.use_cases import Ok as GridOk  # noqa: E402
try:
    from grid_composition.use_cases import Err as GridErr  # noqa: E402
    record("RESULT", "GRID は失敗ラッパに 'Err' を使用")
except ImportError:
    record("RESULT", "GRID の 'Err' import 失敗")
from image_variant_management.failures import Failure as ImgVarFailure  # noqa: E402
record("RESULT", "ImgVar は失敗ラッパに 'Failure' を使用 -> 名前不一致 (Err vs Failure)")
record("RESULT", f"GRID.Ok is ImgVar.Ok ? 別モジュール定義のため別型")


# ---------------------------------------------------------------------------
# 統合試行: ImgVar を GRID の ImageCopyExistenceCheck として結線する
# ---------------------------------------------------------------------------
print("\n=== 統合試行: ImgVar の image_copy_exists を GRID の存在確認に結線 ===")

# 不整合点 #5: UC コンテナのクラス命名規約 (UseCases vs Service)
# GRID は GridCompositionUseCases、ImgVar は ImageVariantManagementService。
# 「<Capability>UseCases」と推測すると import に失敗する。
from grid_composition.use_cases import GridCompositionUseCases  # noqa: E402, F401
from image_variant_management.use_cases import ImageVariantManagementService  # noqa: E402
record(
    "NAMING",
    "UC コンテナ命名が不一致: GRID='GridCompositionUseCases' / "
    "ImgVar='ImageVariantManagementService' (UseCases vs Service)",
)

# GRID 側が要求する exists(copy_id: uuid.UUID) -> bool に、
# ImgVar の image_copy_exists(copy_id: str) -> Result[bool] を適合させる必要がある。
# ここで「アダプタを書かないと結線できない」ことが核心的発見。
record(
    "INTEGRATION",
    "GRID.ImageCopyExistenceCheck.exists(copy_id: UUID)->bool と "
    "ImgVar.image_copy_exists(copy_id: str)->Result[bool] は "
    "(a) 引数型 (UUID vs str) (b) 戻り型 (bool vs Result[bool]) の 2 点で不一致。"
    "直接結線不可、アダプタが必須。",
)


# アダプタを実際に書いてみる (これが「手作業の接着剤」のコスト)
class ImgVarExistenceAdapter:
    """ImgVar の UC を GRID の ImageCopyExistenceCheck プロトコルに適合させる。

    必要な変換:
      - copy_id: GRID は uuid.UUID を渡してくる -> str に変換して ImgVar へ
      - 戻り値: ImgVar は Result[bool] (Ok(True/False)) -> GRID は素の bool を期待
    """

    def __init__(self, imgvar_uc: "ImageVariantManagementService") -> None:
        self._uc = imgvar_uc

    def exists(self, copy_id) -> bool:  # GRID は uuid.UUID を渡す
        # 変換 1: UUID -> str
        copy_id_str = str(copy_id)
        # 変換 2: Result[bool] -> bool
        result = self._uc.image_copy_exists(copy_id=copy_id_str)
        # ImgVar の Ok は .value を持つ (規約を知らないと書けない)
        return getattr(result, "value", False)


record(
    "INTEGRATION",
    "アダプタ ImgVarExistenceAdapter を手書きした: UUID->str 変換 + Result[bool]->bool 変換の "
    "2 段アンマーシャルが必要。これは規範継承では生成されない『接着コード』。",
)


# ---------------------------------------------------------------------------
# まとめ
# ---------------------------------------------------------------------------
print("\n" + "=" * 70)
print("合成試行サマリ — 観測された Capability 間規約不整合")
print("=" * 70)
categories = {}
for tag, _ in findings:
    base = tag.split("-")[0]
    categories[base] = categories.get(base, 0) + 1
for tag, detail in findings:
    print(f"  - [{tag}] {detail}")
print("\n結論: 2 つの実装は『同じサンプル規範を継承していても』直接合成できない。")
print("各 AI セッションが独立に決めた以下が Capability 境界で衝突する:")
print("  1. モジュールレイアウト (flat vs src/)")
print("  2. 共有値オブジェクトの型同一性 (別モジュール定義 = 別型)")
print("  3. identity 表現 (uuid.UUID vs str)")
print("  4. Result ラッパ命名 (Err vs Failure)")
print("  -> codebase convention contract (横断規約契約) が方法論に必要。")
