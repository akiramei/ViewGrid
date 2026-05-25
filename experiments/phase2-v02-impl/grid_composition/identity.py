"""Opaque identity type for entities.

MUST_DECIDE_AND_DOCUMENT: We use ``uuid.UUID`` underneath but expose the
type alias ``Id`` so consumers do not depend on the representation.
"""

from __future__ import annotations

import uuid

Id = uuid.UUID


def new_id() -> Id:
    """Create a fresh opaque identity (UUID4)."""
    return uuid.uuid4()
