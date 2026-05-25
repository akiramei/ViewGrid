"""Minimal stubs for adjacent Capabilities.

These exist only to make the boundary contracts explicit; they are NOT
implementations of those Capabilities.
"""

from .grid_composition import GridCompositionStub
from .history_management import HistoryManagementStub
from .rendering_export import RenderingExportStub
from .workspace_management import WorkspaceManagementStub

__all__ = [
    "GridCompositionStub",
    "HistoryManagementStub",
    "RenderingExportStub",
    "WorkspaceManagementStub",
]
