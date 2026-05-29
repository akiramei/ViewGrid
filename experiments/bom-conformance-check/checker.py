"""BOM <-> Implementation Conformance Checker (candidate E, step 5).

Machine-checkable cross-reference between a Capability BOM (21-*.yaml) and an
implementation, addressing the residual findings F-1 / F-2 / D-1 / D-3 that all
converged on "the declared contract vs what the implementation actually does".

Three check categories (see ../../docs/methodology-extensions/22-bom-conformance-check.md):

  C3 (static, generic): canonical_failure_reasons <-> per-UC failure_reasons
      bidirectional consistency. Catches D-1-class drift. Pure YAML, no impl.

  C1 (dynamic, focused): per-UC failure-reason *coverage* -- each declared
      reason must be either (a) producible by calling the UC, or (b) annotated
      `guaranteed_by` (upstream value-object/enum construction), in which case
      the guard is verified instead. Catches F-2 (mis-mapping) and F-1 (a
      declared reason that is unreachable because a self-validating value object
      makes the invalid input unconstructable).

  C2 (dynamic, focused): declared-precondition *enforcement* -- if the BOM
      declares a precondition, the UC must reject inputs that violate it with a
      declared failure reason. Catches D-3 (cross-grid swap) once the BOM
      declares BothPlacementsBelongToSameGrid.

Run:  python experiments/bom-conformance-check/checker.py
"""
from __future__ import annotations

import sys
import uuid
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
SAMPLE = ROOT / "docs" / "capability-bom-sample"
# Target the FIRST n=2 implementation (Addendum F), where the residual findings
# F-2 (UC-05 mis-mapping) and D-3 (cross-grid swap) actually live. The later
# v0.3 regeneration (phase2-v03-n3-impl) improved UC-05 and would pass C1 -- which
# is itself the point: the checker distinguishes a buggy impl from a fixed one.
IMPL_SRC = ROOT / "experiments" / "phase2-cocompose-impl" / "src"

BOMS = {
    "GRID_COMPOSITION": SAMPLE / "21-grid-composition.yaml",
    "IMAGE_VARIANT_MANAGEMENT": SAMPLE / "image-variant-management" / "21-image-variant-management.yaml",
    "RENDERING_EXPORT": SAMPLE / "rendering-export" / "21-rendering-export.yaml",
}


# --------------------------------------------------------------------------- #
# helpers
# --------------------------------------------------------------------------- #
def _find_key(obj, key):
    """DFS for the first value under `key` anywhere in a nested dict/list.
    Makes the checker robust to BOMs nesting use_cases under a top key vs not."""
    if isinstance(obj, dict):
        if key in obj:
            return obj[key]
        for v in obj.values():
            r = _find_key(v, key)
            if r is not None:
                return r
    elif isinstance(obj, list):
        for v in obj:
            r = _find_key(v, key)
            if r is not None:
                return r
    return None


def load_bom(path: Path) -> dict:
    return yaml.safe_load(path.read_text(encoding="utf-8"))


# --------------------------------------------------------------------------- #
# C3 - static cross-reference (generic)
# --------------------------------------------------------------------------- #
def check_static(name: str, bom: dict) -> list[str]:
    findings: list[str] = []
    ucs = _find_key(bom, "use_cases") or []
    cfrs = _find_key(bom, "canonical_failure_reasons") or []

    uc_reasons: dict[str, set[str]] = {}  # reason -> {uc ids that list it}
    for uc in ucs:
        for r in uc.get("failure_reasons", []) or []:
            uc_reasons.setdefault(r, set()).add(uc["id"])

    cfr_applies: dict[str, set[str]] = {}
    for c in cfrs:
        cfr_applies[c["name"]] = set(c.get("applies_to", []) or [])

    # (a) every per-UC failure reason must be declared in canonical_failure_reasons
    for reason, ucids in uc_reasons.items():
        if reason not in cfr_applies:
            findings.append(f"[C3] reason '{reason}' used by {sorted(ucids)} is NOT in canonical_failure_reasons")

    # (b) bidirectional applies_to consistency
    for reason, declared_ucs in cfr_applies.items():
        actual_ucs = uc_reasons.get(reason, set())
        missing = declared_ucs - actual_ucs            # cfr says applies, UC doesn't list
        extra = actual_ucs - declared_ucs              # UC lists, cfr doesn't say applies
        if missing:
            findings.append(f"[C3] '{reason}'.applies_to lists {sorted(missing)} but those UCs don't list it")
        if extra:
            findings.append(f"[C3] '{reason}' is listed by {sorted(extra)} but missing from its applies_to")
    return findings


# --------------------------------------------------------------------------- #
# dynamic import of the frozen v0.3 n=3 implementation
# --------------------------------------------------------------------------- #
def _import_impl():
    if str(IMPL_SRC) not in sys.path:
        sys.path.insert(0, str(IMPL_SRC))
    import importlib
    # bus module is named eventbus.py in some impls, events.py in others.
    bus_mod = None
    for cand in ("shared.eventbus", "shared.events"):
        try:
            bus_mod = importlib.import_module(cand)
            break
        except ModuleNotFoundError:
            continue
    if bus_mod is None or not hasattr(bus_mod, "RecordingBus"):
        raise RuntimeError("could not locate RecordingBus in shared.eventbus / shared.events")
    mods = {
        "imgvar_uc": importlib.import_module("image_variant_management.use_cases"),
        "imgvar_dom": importlib.import_module("image_variant_management.domain"),
        "imgvar_repo": importlib.import_module("image_variant_management.repositories"),
        "grid_uc": importlib.import_module("grid_composition.use_cases"),
        "grid_dom": importlib.import_module("grid_composition.domain"),
        "grid_repo": importlib.import_module("grid_composition.repositories"),
        "shared_vo": importlib.import_module("shared.value_objects"),
        "events": bus_mod,
    }
    return mods


def _verify_guard(reason: str, mods) -> tuple[bool, str]:
    """For a `guaranteed_by` reason, actually CONSTRUCT the guarding value object /
    enum with invalid input and confirm it raises. A waiver is only sound if the
    upstream guard genuinely rejects -- otherwise annotation alone would hide the
    very mismatch C1 is meant to catch."""
    dom = mods["imgvar_dom"]
    vo = mods["shared_vo"]
    # reason -> a thunk that constructs the guard with INVALID input (must raise)
    guards = {
        "InvalidOccupySize": lambda: vo.OccupySize(0, 0),                 # value < 1
        "InvalidScalingMode": lambda: dom.ScalingMode("__invalid__"),     # not an enum member
        "InvalidAlignment": lambda: dom.Alignment("__invalid__"),
        "InvalidTransform": lambda: dom.ImageTransform(rotation="__invalid__"),
    }
    probe = guards.get(reason)
    if probe is None:
        return (False, "no guard probe defined for this reason")
    try:
        probe()
    except Exception as exc:  # noqa: BLE001 -- any rejection is a valid guard
        return (True, f"guard rejects invalid input ({type(exc).__name__})")
    return (False, "guard did NOT reject invalid input (annotation unsound)")


def _build_imgvar(mods):
    """Construct ImageVariantManagementUseCases, tolerating impls that need
    explicit repos (cocompose) vs those that default them (v0.3)."""
    UC = mods["imgvar_uc"].ImageVariantManagementUseCases
    bus = mods["events"].RecordingBus()
    try:
        return UC(bus=bus), bus
    except TypeError:
        r = mods["imgvar_repo"]
        return UC(
            asset_repo=r.InMemoryImageAssetRepository(),
            copy_repo=r.InMemoryImageCopyRepository(),
            blob_storage=r.InMemoryBlobStorage(),
            bus=bus,
        ), bus


def _build_grid(mods, imgvar, bus):
    GUC = mods["grid_uc"].GridCompositionUseCases
    try:
        return GUC(image_copy_existence=imgvar, bus=bus)
    except TypeError:
        r = mods["grid_repo"]
        return GUC(
            grid_repo=r.InMemoryGridCanvasRepository(),
            placement_repo=r.InMemoryPlacementRepository(),
            image_copy_existence=imgvar,
            bus=bus,
        )


def _ok_id(result):
    """Extract an id from an Ok result, tolerating impls that return the id
    directly (Ok(id)) vs an entity (Ok(entity) with .id)."""
    v = getattr(result, "value", None)
    return getattr(v, "id", v)


def _reason_name(result) -> str:
    """API-agnostic: an Err carries `.error` (the failure object); an Ok carries
    `.value`. Avoid calling is_ok() because some impls expose it as a property."""
    if result is None:
        return "<None>"
    err = getattr(result, "error", None)
    if err is not None:
        return type(err).__name__
    return "<Ok>"


# --------------------------------------------------------------------------- #
# C1 - per-UC failure-reason coverage (focused on IMGVAR UC-05, the F-1/F-2 case)
# --------------------------------------------------------------------------- #
def check_uc05_coverage(bom: dict, mods) -> list[str]:
    findings: list[str] = []
    uc = next((u for u in (_find_key(bom, "use_cases") or []) if u["id"] == "UC-05"), None)
    if uc is None:
        return ["[C1] UC-05 not found in IMGVAR BOM"]
    declared = list(uc.get("failure_reasons", []) or [])

    # which reasons are annotated as upstream-guaranteed (value-object/enum construction)?
    cfrs = {c["name"]: c for c in (_find_key(bom, "canonical_failure_reasons") or [])}
    guaranteed = {r for r in declared if cfrs.get(r, {}).get("guaranteed_by")}

    uc_obj, _bus = _build_imgvar(mods)
    # cocompose's fake decoder expects b"IMG:<w>x<h>:..." header bytes.
    asset_id = _ok_id(uc_obj.import_image_asset(b"IMG:10x10:demo", "p.png", "image/png"))

    # probe inputs: input kwargs -> the reason the BOM *intends* for this input
    probes = {
        "NotFound": dict(asset_id=uuid.uuid4()),
        "InvalidCopyName": dict(asset_id=asset_id, copy_name=""),
        "InvalidTransform": dict(asset_id=asset_id, initial_transform="BAD"),
        "InvalidScalingMode": dict(asset_id=asset_id, initial_scaling_mode="BAD"),
        "InvalidAlignment": dict(asset_id=asset_id, initial_alignment="BAD"),
        "InvalidOccupySize": dict(asset_id=asset_id, initial_occupy_size="BAD"),
    }

    for reason in declared:
        if reason in guaranteed:
            ok, detail = _verify_guard(reason, mods)
            if ok:
                findings.append(f"[C1] OK '{reason}': guaranteed_by upstream and {detail}")
            else:
                findings.append(f"[C1] FLAG '{reason}': annotated guaranteed_by but {detail}")
            continue
        kwargs = probes.get(reason)
        if kwargs is None:
            findings.append(f"[C1] '{reason}': no probe input defined (cannot verify producibility)")
            continue
        try:
            res = uc_obj.create_image_copy(**kwargs)
            got = _reason_name(res)
        except Exception as exc:  # noqa: BLE001
            got = f"raised {type(exc).__name__}: {exc}"
        if got == reason:
            findings.append(f"[C1] OK '{reason}': producible at UC-05")
        else:
            findings.append(
                f"[C1] FLAG '{reason}': declared at UC-05 but probe produced '{got}' "
                f"(not reachable / mis-mapped)"
            )
    return findings


# --------------------------------------------------------------------------- #
# C2 - declared-precondition enforcement (focused on GRID UC-07 cross-grid, D-3)
# --------------------------------------------------------------------------- #
def check_uc07_precondition(bom: dict, mods) -> list[str]:
    findings: list[str] = []
    uc = next((u for u in (_find_key(bom, "use_cases") or []) if u["id"] == "UC-07"), None)
    if uc is None:
        return ["[C2] UC-07 not found in GRID BOM"]
    preconds = uc.get("preconditions", []) or []
    if "BothPlacementsBelongToSameGrid" not in preconds:
        return ["[C2] UC-07 BOM does not declare BothPlacementsBelongToSameGrid -> "
                "cross-grid swap is UNSPECIFIED (D-3 still open at the BOM level)"]

    CellPosition = mods["grid_dom"].CellPosition
    OccupySize = mods["shared_vo"].OccupySize
    PixelSize = mods["shared_vo"].PixelSize

    imgvar, bus = _build_imgvar(mods)
    grid = _build_grid(mods, imgvar, bus)
    a = _ok_id(imgvar.import_image_asset(b"IMG:10x10:a", "a.png", "image/png"))
    c1 = _ok_id(imgvar.create_image_copy(a, copy_name="c1"))
    c2 = _ok_id(imgvar.create_image_copy(a, copy_name="c2"))

    g1 = _ok_id(grid.create_grid_canvas("g1", 2, 2, PixelSize(100, 100)))
    g2 = _ok_id(grid.create_grid_canvas("g2", 2, 2, PixelSize(100, 100)))
    p1 = _ok_id(grid.place_image_copy(g1, c1, CellPosition(0, 0), OccupySize(1, 1)))
    p2 = _ok_id(grid.place_image_copy(g2, c2, CellPosition(0, 0), OccupySize(1, 1)))

    try:
        res = grid.swap_placements(p1, p2)  # p1 in g1, p2 in g2 -> cross-grid
        got = _reason_name(res)
    except Exception as exc:  # noqa: BLE001
        got = f"raised {type(exc).__name__}"

    if got == "CrossGridSwapNotAllowed":
        findings.append("[C2] OK 'BothPlacementsBelongToSameGrid': UC-07 enforces it (CrossGridSwapNotAllowed)")
    else:
        findings.append(
            f"[C2] FLAG 'BothPlacementsBelongToSameGrid': declared in BOM but UC-07 did NOT enforce it "
            f"(cross-grid swap produced '{got}') -- this is the D-3 bug the frozen impl carries"
        )
    return findings


# --------------------------------------------------------------------------- #
def main() -> int:
    print("=" * 72)
    print("BOM <-> Implementation Conformance Check")
    print("=" * 72)

    all_findings: list[str] = []

    print("\n## C3 static cross-reference (all BOMs)")
    for name, path in BOMS.items():
        bom = load_bom(path)
        fs = check_static(name, bom)
        all_findings += fs
        if fs:
            for f in fs:
                print(f"  {name}: {f}")
        else:
            print(f"  {name}: consistent (canonical_failure_reasons <-> per-UC failure_reasons)")

    mods = _import_impl()

    print("\n## C1 failure-reason coverage (IMAGE_VARIANT_MANAGEMENT UC-05)")
    imgvar_bom = load_bom(BOMS["IMAGE_VARIANT_MANAGEMENT"])
    for f in check_uc05_coverage(imgvar_bom, mods):
        print(f"  {f}")
        if "FLAG" in f:
            all_findings.append(f)

    print("\n## C2 precondition enforcement (GRID_COMPOSITION UC-07 cross-grid / D-3)")
    grid_bom = load_bom(BOMS["GRID_COMPOSITION"])
    for f in check_uc07_precondition(grid_bom, mods):
        print(f"  {f}")
        if "FLAG" in f:
            all_findings.append(f)

    flags = [f for f in all_findings if "FLAG" in f or "[C3]" in f]
    print("\n" + "=" * 72)
    print(f"FLAGS: {len(flags)}")
    for f in flags:
        print(f"  - {f}")
    print("=" * 72)
    # CI-style guard: non-zero exit when any conformance flag remains, so
    # automated consumers treat a failed conformance check as a failure.
    # (The frozen phase2-cocompose-impl legitimately carries the D-3 bug, so the
    # current after-state exits 1 -- a regenerated, compliant impl would exit 0.)
    return 1 if flags else 0


if __name__ == "__main__":
    raise SystemExit(main())
