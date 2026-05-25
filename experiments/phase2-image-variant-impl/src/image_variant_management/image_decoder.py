"""Image decoder abstraction (for R-01 enforcement).

Per 40-prompt MUST_DECIDE_AND_DOCUMENT, the decoder is AI choice but must be
mockable. We expose a Protocol-style interface ``ImageDecoder`` plus two
implementations:

* ``PillowImageDecoder``: real decoder using Pillow (PIL)
* ``MockImageDecoder``:   in-memory map of bytes -> PixelSize for tests

The UseCase layer accepts any ``ImageDecoder`` via DI.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Optional

from .shared import PixelSize


class ImageDecoder(ABC):
    @abstractmethod
    def decode_size(self, blob: bytes, mime_type: str) -> Optional[PixelSize]:
        """Return the PixelSize if decoding succeeds, else None.

        Returning ``None`` is the canonical "not a valid image" signal — the
        UseCase translates it to ``InvalidImageData``. Exceptions are caught
        by the UseCase and also reported as ``InvalidImageData(detail=...)``.
        """


class PillowImageDecoder(ImageDecoder):
    """Real decoder using Pillow."""

    def decode_size(self, blob: bytes, mime_type: str) -> Optional[PixelSize]:
        from io import BytesIO

        try:
            from PIL import Image  # type: ignore
        except ImportError:  # pragma: no cover
            raise RuntimeError(
                "Pillow is required for PillowImageDecoder. Install via 'pip install pillow'."
            )

        try:
            with Image.open(BytesIO(blob)) as img:
                img.verify()  # raises if corrupt
            # Re-open because verify() consumes the file.
            with Image.open(BytesIO(blob)) as img:
                width, height = img.size
                if width < 1 or height < 1:
                    return None
                return PixelSize(width=int(width), height=int(height))
        except Exception:
            return None


class MockImageDecoder(ImageDecoder):
    """Deterministic in-memory decoder for tests.

    Maps bytes -> PixelSize. Bytes not in the map are treated as invalid.
    """

    def __init__(self, table: Optional[dict[bytes, PixelSize]] = None) -> None:
        self.table: dict[bytes, PixelSize] = table or {}

    def register(self, blob: bytes, size: PixelSize) -> None:
        self.table[blob] = size

    def decode_size(self, blob: bytes, mime_type: str) -> Optional[PixelSize]:
        return self.table.get(blob)
