"""Repository interfaces + in-memory implementations.

Per 30-design.md §5.1, three repositories are required:

* ``ImageAssetRepository``  — get/save/delete/list/find-by-hash
* ``ImageCopyRepository``   — get/save/delete/list/by-asset/exists
* ``ImageBlobStorage``       — store/load/delete physical bytes (stub)

Decision (see IMPLEMENTATION_NOTES.md):

* Repositories return ``None`` when an entity is missing rather than raising.
  The UseCase layer maps ``None`` to ``NotFound`` failure values.
* ``ImageBlobStorage`` uses an in-memory dict keyed by ``relative_path``.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Iterable, Optional

from .domain import ImageAsset, ImageCopy


# ----------------------------------------------------------------------------
# Interfaces
# ----------------------------------------------------------------------------


class ImageAssetRepository(ABC):
    @abstractmethod
    def get_by_id(self, asset_id: str) -> Optional[ImageAsset]: ...

    @abstractmethod
    def save(self, asset: ImageAsset) -> None: ...

    @abstractmethod
    def delete(self, asset_id: str) -> None: ...

    @abstractmethod
    def list_all(self) -> list[ImageAsset]: ...

    @abstractmethod
    def find_by_hash(self, file_hash: str) -> Optional[ImageAsset]:
        """Required for R-02 hash-unique enforcement."""


class ImageCopyRepository(ABC):
    @abstractmethod
    def get_by_id(self, copy_id: str) -> Optional[ImageCopy]: ...

    @abstractmethod
    def get_by_asset_id(self, asset_id: str) -> list[ImageCopy]:
        """Required for UC-02 dependent-copies check."""

    @abstractmethod
    def save(self, copy: ImageCopy) -> None: ...

    @abstractmethod
    def delete(self, copy_id: str) -> None: ...

    @abstractmethod
    def list_all(self, asset_id: Optional[str] = None) -> list[ImageCopy]: ...

    @abstractmethod
    def exists(self, copy_id: str) -> bool: ...


class ImageBlobStorage(ABC):
    @abstractmethod
    def store(self, blob: bytes, file_hash: str) -> str:
        """Persist bytes and return the storage-relative path."""

    @abstractmethod
    def load(self, relative_path: str) -> bytes: ...

    @abstractmethod
    def delete(self, relative_path: str) -> None: ...


# ----------------------------------------------------------------------------
# In-memory implementations (stubs for the experiment / tests)
# ----------------------------------------------------------------------------


class InMemoryImageAssetRepository(ImageAssetRepository):
    def __init__(self) -> None:
        self._by_id: dict[str, ImageAsset] = {}
        # R-02 acceleration index: hash -> asset_id (O(1) lookup, not relying on
        # DB constraints per 30-design.md §R-02 note).
        self._by_hash: dict[str, str] = {}

    def get_by_id(self, asset_id: str) -> Optional[ImageAsset]:
        return self._by_id.get(asset_id)

    def save(self, asset: ImageAsset) -> None:
        # When updating an existing record under a new hash (not a flow in this
        # Capability — assets are immutable post-creation) the old hash index
        # would leak; here we only ever insert new records.
        self._by_id[asset.id] = asset
        self._by_hash[asset.file_hash] = asset.id

    def delete(self, asset_id: str) -> None:
        asset = self._by_id.pop(asset_id, None)
        if asset is not None:
            self._by_hash.pop(asset.file_hash, None)

    def list_all(self) -> list[ImageAsset]:
        return list(self._by_id.values())

    def find_by_hash(self, file_hash: str) -> Optional[ImageAsset]:
        asset_id = self._by_hash.get(file_hash)
        if asset_id is None:
            return None
        return self._by_id.get(asset_id)


class InMemoryImageCopyRepository(ImageCopyRepository):
    def __init__(self) -> None:
        self._by_id: dict[str, ImageCopy] = {}

    def get_by_id(self, copy_id: str) -> Optional[ImageCopy]:
        return self._by_id.get(copy_id)

    def get_by_asset_id(self, asset_id: str) -> list[ImageCopy]:
        return [c for c in self._by_id.values() if c.asset_id == asset_id]

    def save(self, copy: ImageCopy) -> None:
        self._by_id[copy.id] = copy

    def delete(self, copy_id: str) -> None:
        self._by_id.pop(copy_id, None)

    def list_all(self, asset_id: Optional[str] = None) -> list[ImageCopy]:
        if asset_id is None:
            return list(self._by_id.values())
        return [c for c in self._by_id.values() if c.asset_id == asset_id]

    def exists(self, copy_id: str) -> bool:
        return copy_id in self._by_id


class InMemoryImageBlobStorage(ImageBlobStorage):
    """Dict-backed blob storage. ``relative_path`` is just the hash (lowercase).

    This is a stub for the experiment; per 40-prompt this Capability does NOT
    own physical storage decisions.
    """

    def __init__(self) -> None:
        self._blobs: dict[str, bytes] = {}

    def store(self, blob: bytes, file_hash: str) -> str:
        # Same hash -> same path. Caller (UseCase) deduplicates via repository,
        # so we tolerate re-writes here without double-storage.
        path = f"blobs/{file_hash}"
        self._blobs[path] = blob
        return path

    def load(self, relative_path: str) -> bytes:
        return self._blobs[relative_path]

    def delete(self, relative_path: str) -> None:
        self._blobs.pop(relative_path, None)

    # Test instrumentation
    def all_paths(self) -> list[str]:
        return list(self._blobs.keys())
