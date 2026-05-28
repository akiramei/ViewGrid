"""Result wrapper.

Contract binding (00-convention-contract.md):
  - C-RESULT: success = Ok, failure = Err, defined ONCE here. No synonyms
    (no "Failure", "Success", etc.). Both Capabilities import these.

Failure reasons themselves are modelled per-Capability as dataclasses carrying
the canonical name + payload; they are wrapped in Err(...).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Generic, TypeVar, Union

T = TypeVar("T")
E = TypeVar("E")


@dataclass(frozen=True)
class Ok(Generic[T]):
    value: T

    def is_ok(self) -> bool:
        return True

    def is_err(self) -> bool:
        return False

    def unwrap(self) -> T:
        return self.value


@dataclass(frozen=True)
class Err(Generic[E]):
    error: E

    def is_ok(self) -> bool:
        return False

    def is_err(self) -> bool:
        return True

    def unwrap(self) -> T:  # type: ignore[type-var]
        raise AssertionError(f"unwrap() called on Err({self.error!r})")


# Result[T] is the union both Capabilities import.
Result = Union[Ok[T], Err[E]]
