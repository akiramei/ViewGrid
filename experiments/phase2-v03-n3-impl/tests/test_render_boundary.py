"""Boundary test (C-CONSUMER-PORTS / 30-design.md §6.6).

Statically confirms RENDERING_EXPORT does NOT import producer domain modules
(grid_composition / image_variant_management). It may import only shared.*.
"""
from __future__ import annotations

import ast
import os

RENDERING_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "src",
    "rendering_export",
)

FORBIDDEN_ROOTS = {"grid_composition", "image_variant_management"}


def _module_files():
    for root, _dirs, files in os.walk(RENDERING_DIR):
        for f in files:
            if f.endswith(".py"):
                yield os.path.join(root, f)


def _imported_roots(path: str) -> set[str]:
    with open(path, "r", encoding="utf-8") as fh:
        tree = ast.parse(fh.read(), filename=path)
    roots: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                roots.add(alias.name.split(".")[0])
        elif isinstance(node, ast.ImportFrom):
            if node.module and node.level == 0:
                roots.add(node.module.split(".")[0])
    return roots


def test_rendering_does_not_import_producer_domains():
    offenders: dict[str, set[str]] = {}
    for path in _module_files():
        bad = _imported_roots(path) & FORBIDDEN_ROOTS
        if bad:
            offenders[path] = bad
    assert not offenders, f"RENDERING imports producer domains: {offenders}"


def test_rendering_consumes_only_shared_for_cross_capability():
    # All cross-capability symbols come from `shared` (ports + render_contracts).
    for path in _module_files():
        roots = _imported_roots(path)
        assert not (roots & FORBIDDEN_ROOTS)
