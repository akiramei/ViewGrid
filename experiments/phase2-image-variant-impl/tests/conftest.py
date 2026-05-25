"""Pytest configuration: add src to sys.path and provide common fixtures."""

from __future__ import annotations

import os
import sys
from datetime import datetime, timezone
from itertools import count
from typing import Iterator

import pytest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
if SRC not in sys.path:
    sys.path.insert(0, SRC)

from image_variant_management import (  # noqa: E402  (after sys.path mutation)
    EventBus,
    ImageVariantManagementService,
    InMemoryImageAssetRepository,
    InMemoryImageBlobStorage,
    InMemoryImageCopyRepository,
    MockImageDecoder,
    PixelSize,
)


@pytest.fixture
def fixed_clock() -> "callable":  # type: ignore[type-arg]
    """A monotonically incrementing UTC clock for deterministic tests."""

    counter = count(start=0)

    def _clock() -> datetime:
        i = next(counter)
        return datetime(2026, 1, 1, 0, 0, i, tzinfo=timezone.utc)

    return _clock


@pytest.fixture
def id_factory() -> "callable":  # type: ignore[type-arg]
    """Deterministic id factory for tests."""

    counter = count(start=1)

    def _next_id() -> str:
        return f"id-{next(counter):06d}"

    return _next_id


@pytest.fixture
def decoder() -> MockImageDecoder:
    """Mock decoder with a couple of canned blobs.

    ``b"<png-100x80>"``  -> 100x80
    ``b"<jpg-50x50>"``   -> 50x50
    """

    return MockImageDecoder(
        table={
            b"<png-100x80>": PixelSize(width=100, height=80),
            b"<png-100x80>-alt": PixelSize(width=100, height=80),  # same size, different bytes
            b"<jpg-50x50>": PixelSize(width=50, height=50),
            b"<webp-1024x768>": PixelSize(width=1024, height=768),
        }
    )


@pytest.fixture
def event_bus() -> EventBus:
    return EventBus()


@pytest.fixture
def service(
    fixed_clock: "callable", id_factory: "callable", decoder: MockImageDecoder, event_bus: EventBus
) -> ImageVariantManagementService:
    return ImageVariantManagementService(
        asset_repo=InMemoryImageAssetRepository(),
        copy_repo=InMemoryImageCopyRepository(),
        blob_storage=InMemoryImageBlobStorage(),
        decoder=decoder,
        event_bus=event_bus,
        clock=fixed_clock,
        id_factory=id_factory,
    )
