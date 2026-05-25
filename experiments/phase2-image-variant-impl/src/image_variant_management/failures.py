"""Canonical failure reasons.

Each failure is represented as a value object (frozen dataclass). UseCases
return ``Result`` (either an ``Ok`` carrying the payload or a ``Failure``
carrying one of these). This mirrors the YAML's ``canonical_failure_reasons``
section exactly — names must NOT be changed.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Generic, Optional, TypeVar, Union


# ----------------------------------------------------------------------------
# Failure value objects
# ----------------------------------------------------------------------------


@dataclass(frozen=True, slots=True)
class NotFound:
    """Preconditions violated: referenced entity does not exist.

    Applies to UC-02, UC-04, UC-05, UC-06, UC-08, UC-09, UC-10, UC-11, UC-12,
    UC-13, UC-14, UC-15.
    """

    entity_kind: str  # "ImageAsset" | "ImageCopy"
    entity_id: str


@dataclass(frozen=True, slots=True)
class DependentCopiesExist:
    """UC-02 only: cannot delete an ImageAsset that has dependent ImageCopies.

    Body of the cascade_decision design choice: this Capability does NOT
    auto-cascade. The caller (upper Coordinator) must decide.
    """

    asset_id: str
    dependent_copy_ids: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class InvalidImageData:
    """UC-01: image bytes cannot be decoded."""

    detail: str


@dataclass(frozen=True, slots=True)
class UnsupportedMimeType:
    """UC-01: MIME type not in the supported set."""

    attempted_mime_type: str
    supported_mime_types: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class InvalidAlignment:
    """UC-05, UC-11."""

    attempted_value: str


@dataclass(frozen=True, slots=True)
class InvalidScalingMode:
    """UC-05, UC-10."""

    attempted_value: str


@dataclass(frozen=True, slots=True)
class InvalidTransform:
    """UC-05, UC-09."""

    attempted_rotation: str
    attempted_flip_x: object  # may be non-bool — we record what was attempted
    attempted_flip_y: object


@dataclass(frozen=True, slots=True)
class InvalidOccupySize:
    """UC-05, UC-14."""

    attempted_width: object
    attempted_height: object


@dataclass(frozen=True, slots=True)
class InvalidAutoCropSettings:
    """UC-12. Both fields must be null together or both non-null with valid ranges."""

    detail: str
    attempted_target_color: Optional[int]
    attempted_threshold: Optional[int]


@dataclass(frozen=True, slots=True)
class InvalidManualCropFractions:
    """UC-13. All-4-null or all-4-valid; sum bounds enforced."""

    detail: str
    x: Optional[float]
    y: Optional[float]
    width: Optional[float]
    height: Optional[float]


@dataclass(frozen=True, slots=True)
class InvalidCopyName:
    """UC-15. Empty string is invalid; None is valid (auto-generated name)."""

    detail: str
    attempted_value: Optional[str]


# Discriminated union of all failure types.
FailureReason = Union[
    NotFound,
    DependentCopiesExist,
    InvalidImageData,
    UnsupportedMimeType,
    InvalidAlignment,
    InvalidScalingMode,
    InvalidTransform,
    InvalidOccupySize,
    InvalidAutoCropSettings,
    InvalidManualCropFractions,
    InvalidCopyName,
]


# ----------------------------------------------------------------------------
# Result wrapper
# ----------------------------------------------------------------------------


T = TypeVar("T")


@dataclass(frozen=True, slots=True)
class Ok(Generic[T]):
    value: T

    @property
    def is_ok(self) -> bool:
        return True

    @property
    def is_failure(self) -> bool:
        return False


@dataclass(frozen=True, slots=True)
class Failure:
    reason: FailureReason

    @property
    def is_ok(self) -> bool:
        return False

    @property
    def is_failure(self) -> bool:
        return True


Result = Union[Ok[T], Failure]
