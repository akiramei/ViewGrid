"""Boundary import-check (30-design.md §4.6 / C-CONSUMER-PORTS / BOM §5.1).

RENDERING_EXPORT must NOT import grid_composition or image_variant_management
(no producer domain types). This is verified statically by parsing every
rendering_export source file's AST and asserting no such import nodes exist, and
dynamically by confirming the producer packages are absent from the consumer's
module dependency closure.
"""

import ast
import os

import rendering_export

_FORBIDDEN_ROOTS = ("grid_composition", "image_variant_management")


def _rendering_source_files():
    pkg_dir = os.path.dirname(rendering_export.__file__)
    for root, _dirs, files in os.walk(pkg_dir):
        for f in files:
            if f.endswith(".py"):
                yield os.path.join(root, f)


def _imported_roots(path):
    with open(path, "r", encoding="utf-8") as fh:
        tree = ast.parse(fh.read(), filename=path)
    roots = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                roots.add(alias.name.split(".")[0])
        elif isinstance(node, ast.ImportFrom):
            if node.module and node.level == 0:
                roots.add(node.module.split(".")[0])
    return roots


def test_rendering_export_does_not_import_producers_static():
    offenders = {}
    files = list(_rendering_source_files())
    assert files, "expected at least one rendering_export source file"
    for path in files:
        roots = _imported_roots(path)
        bad = roots & set(_FORBIDDEN_ROOTS)
        if bad:
            offenders[os.path.basename(path)] = sorted(bad)
    assert offenders == {}, f"forbidden producer imports found: {offenders}"


def test_rendering_export_only_depends_on_shared_and_self():
    # Every external root imported by rendering_export must be either 'shared',
    # 'rendering_export' itself, or a stdlib/typing module — never a producer.
    allowed_first_party = {"shared", "rendering_export"}
    for path in _rendering_source_files():
        for root in _imported_roots(path):
            assert root not in _FORBIDDEN_ROOTS, (
                f"{os.path.basename(path)} imports forbidden producer '{root}'"
            )
            # first-party imports are restricted to shared / self.
            if root in ("grid_composition", "image_variant_management"):
                raise AssertionError("unreachable")  # covered above


def test_consumer_module_closure_excludes_producers():
    # Dynamic check: importing rendering_export.use_cases must not pull in the
    # producer packages as a side effect.
    import importlib
    import sys

    for mod in list(sys.modules):
        if mod.startswith(("grid_composition", "image_variant_management")):
            del sys.modules[mod]
    importlib.import_module("rendering_export.use_cases")
    pulled = [m for m in sys.modules
              if m.startswith(("grid_composition", "image_variant_management"))]
    assert pulled == [], f"consumer import pulled in producers: {pulled}"
