"""In-memory repository + blob storage + decoder stubs for IMAGE_VARIANT.

Persistence is out of scope (30-design.md §5). C-REPO-NOTFOUND: "not found"
returns None. R-02 (hash uniqueness) is enforced by the UseCase layer querying
FindByHash, NOT by any DB unique constraint.

The image decoder is a Capability-local MUST_DECIDE (documented in
IMPLEMENTATION_NOTES.md): we use a pluggable callable so tests can mock it and
no real PIL dependency is required.
"""

from __future__ import annotations

import hashlib
import uuid
from typing import Callable

from image_variant_management.domain import ImageAsset, ImageCopy
from shared.value_objects import PixelSize


class InMemoryImageAssetRepository:
    def __init__(self) -> None:
        self._assets: dict[uuid.UUID, ImageAsset] = {}

    def get_by_id(self, asset_id: uuid.UUID) -> ImageAsset | None:
        return self._assets.get(asset_id)

    def save(self, asset: ImageAsset) -> None:
        self._assets[asset.id] = asset

    def delete(self, asset_id: uuid.UUID) -> None:
        self._assets.pop(asset_id, None)

    def list_all(self) -> list[ImageAsset]:
        return list(self._assets.values())

    def find_by_hash(self, file_hash: str) -> ImageAsset | None:
        for a in self._assets.values():
            if a.file_hash == file_hash:
                return a
        return None


class InMemoryImageCopyRepository:
    def __init__(self) -> None:
        self._copies: dict[uuid.UUID, ImageCopy] = {}

    def get_by_id(self, copy_id: uuid.UUID) -> ImageCopy | None:
        return self._copies.get(copy_id)

    def get_by_asset_id(self, asset_id: uuid.UUID) -> list[ImageCopy]:
        return [c for c in self._copies.values() if c.asset_id == asset_id]

    def save(self, copy: ImageCopy) -> None:
        self._copies[copy.id] = copy

    def delete(self, copy_id: uuid.UUID) -> None:
        self._copies.pop(copy_id, None)

    def list_all(self) -> list[ImageCopy]:
        return list(self._copies.values())

    def exists(self, copy_id: uuid.UUID) -> bool:
        return copy_id in self._copies


class InMemoryBlobStorage:
    def __init__(self) -> None:
        self._blobs: dict[str, bytes] = {}

    def store(self, data: bytes, file_hash: str) -> str:
        rel = f"blobs/{file_hash}"
        self._blobs[rel] = data
        return rel

    def load(self, relative_path: str) -> bytes | None:
        return self._blobs.get(relative_path)

    def delete(self, relative_path: str) -> None:
        self._blobs.pop(relative_path, None)

    def has(self, relative_path: str) -> bool:
        return relative_path in self._blobs


# Capability-local MUST_DECIDE: image decoder.
# A decoder takes raw bytes and returns a PixelSize, or raises ValueError if the
# bytes cannot be decoded (R-01). Default = a tiny fake header parser so tests
# need no real image library; real deployments inject a PIL-backed decoder.
ImageDecoder = Callable[[bytes], PixelSize]


def fake_header_decoder(data: bytes) -> PixelSize:
    """Decode a toy format: bytes b"IMG:<w>x<h>:<payload>".

    Anything else raises ValueError (R-01 violation -> InvalidImageData).
    """
    try:
        text = data.decode("ascii")
    except Exception as exc:  # noqa: BLE001
        raise ValueError(f"not decodable bytes: {exc}") from exc
    if not text.startswith("IMG:"):
        raise ValueError("missing IMG header")
    try:
        dims = text.split(":", 2)[1]
        w_str, h_str = dims.lower().split("x")
        w, h = int(w_str), int(h_str)
    except Exception as exc:  # noqa: BLE001
        raise ValueError(f"bad dimensions: {exc}") from exc
    if w < 1 or h < 1:
        raise ValueError("non-positive dimensions")
    return PixelSize(w, h)


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()
