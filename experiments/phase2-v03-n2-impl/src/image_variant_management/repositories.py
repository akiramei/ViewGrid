"""In-memory repositories for IMAGE_VARIANT_MANAGEMENT.

R-02 hash uniqueness is enforced by the UseCase layer (FindByHash), NOT by
a DB unique constraint. not-found -> None (C-REPO-NOTFOUND).
"""
from __future__ import annotations

import uuid

from .domain import ImageAsset, ImageCopy


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
        for asset in self._assets.values():
            if asset.file_hash == file_hash:
                return asset
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
