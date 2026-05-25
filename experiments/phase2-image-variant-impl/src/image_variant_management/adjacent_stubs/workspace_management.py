"""Stub for WORKSPACE_MANAGEMENT.

Per ``20-capability-bom.md`` §8.3, this Capability *uses* an ``IImageStorage``
provided by WORKSPACE_MANAGEMENT. From this side the contract is "we accept
any ``ImageBlobStorage`` implementation in DI". The stub is a thin wrapper
demonstrating that a workspace would simply hand us an in-memory storage.
"""

from __future__ import annotations

from ..repositories import (
    InMemoryImageAssetRepository,
    InMemoryImageBlobStorage,
    InMemoryImageCopyRepository,
)


class WorkspaceManagementStub:
    """Minimal workspace context: holds the three repositories for one workspace."""

    def __init__(self) -> None:
        self.asset_repo = InMemoryImageAssetRepository()
        self.copy_repo = InMemoryImageCopyRepository()
        self.blob_storage = InMemoryImageBlobStorage()
