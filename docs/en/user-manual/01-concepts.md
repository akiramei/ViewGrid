# §1 Core Concepts

This chapter explains ViewGrid's data model and screen layout. It collects the definitions of the terms used throughout the following chapters: **Asset / Variant / Grid / Placement / Workspace**.

> **Corresponding Quick Start scope**: prerequisite knowledge that spans every chapter. If you want to start using the app first, read [Quick Start §3](../quickstart.md#3-5-minute-tutorial-exporting-your-first-image) beforehand.

## 1.1 Data Model

ViewGrid's data has the following hierarchical structure.

```
Workspace (physically separates the DB file + images + thumbnails)
├── Asset A  (1 image file = 1 Asset)
│   ├── Variant A-1  ← reusable unit for different settings (holds shared properties)
│   │   └── Placement P1  (specifies which cell of which grid it goes in)
│   │   └── Placement P2  (the same Variant can also be placed on a different grid)
│   └── Variant A-2
│       └── Placement P3
└── Asset B
    └── Variant B-1

Grid G1 (NxM canvas) ←── Placements P1, P3 belong to it
Grid G2              ←── Placement P2 belongs to it
```

- **Workspace** — a unit that physically separates the DB / images / thumbnails. It prevents work and hobby projects, for example, from getting mixed up by accident
- **Asset** — an imported image file (1 file = 1 Asset)
- **Variant** (logical copy) — a "reusable unit with different properties" that can be created multiple times from a single Asset. **Shared properties** such as cropping / rotation / flip are tied to it
- **Placement** — the concrete instance of a Variant placed in a specific cell of a specific grid. It holds **placement-specific properties** such as position / occupied cells / pixel offset
- **Grid** — an NxM canvas that serves as the unit of output

### Entity Lifecycle

| Action | Scope of effect | Undoable? |
|---|---|---|
| Asset import | A new Asset plus 1 default Variant are created automatically | ❌ (clears all history) |
| Asset deletion | The Asset + all Variants + all Placements are removed by cascade | ❌ (clears all history) |
| Variant creation | A single Variant is added, with zero Placements | ❌ (clears all history) |
| Variant deletion | The Variant + all Placements referencing it are removed | ❌ (clears all history) |
| Fork | An existing Variant is duplicated, and just 1 Placement is reassigned to the new Variant | ✅ |
| Shared property edit | **The appearance of all Placements** referencing that Variant changes | ✅ |
| Placement creation / move / deletion | Only the Placement in question | ✅ |
| Placement-specific property edit | Only the Placement in question | ✅ |
| Grid creation / deletion | The Grid in question (on deletion, its Placements cascade too) | ❌ (clears all history) |
| Grid name / canvas size change | The Grid's appearance | ✅ |
| Workspace operations | Involves a process restart | ❌ |

Actions marked "clears all history" clear the entire Undo / Redo history immediately after they run, in order to prevent history inconsistencies caused by cascades. If you want to be able to recover an action, export it as a PNG before running it.

## 1.2 Shared Properties and Placement-Specific Properties

There are two kinds of properties: those that "apply to all Placements at the Variant level" and those that "each Placement holds individually".

| Type | Examples | Scope of effect |
|---|---|---|
| **Shared properties** | Scaling / rotation / flip / cropping / protected region | All Placements that reference the same Variant |
| **Placement-specific properties** | Occupied cells (W×H) / pixel offset (ΔX, ΔY) / placement position | Only that Placement |

→ Explained in detail in §5 [Shared Properties](05-shared-properties.md) / §4 [Placements](04-placements.md)

### What "Changing a Shared Property Affects Other Placements Too" Means

For example, suppose Variant A of photo-001.jpg is reused across two Placements (P1 / P2):

- Changing Variant A's **rotation to 90°** → the appearance of both P1 and P2 changes (shared property)
- Changing Placement P1's **occupied cells from 1×1 to 2×2** → only P1 gets larger (placement-specific property)

If you want "only P1 to be rotated 90°", create an independent copy with a **Fork** first, then change its rotation. P2, which references the original Variant, is not affected.

→ §4.14 [Fork](04-placements.md)

## 1.3 Screen Layout

ViewGrid's main window has a **three-pane layout plus a header and a status bar**.

![Three-pane layout of the main window](../images/um/um-01-03-main-window-3pane.png)

<!-- CAPTURE
file: docs/en/images/um/um-01-03-main-window-3pane.png
size: 1280x800
samples: sample-01, sample-02, sample-03 (3 assets + 3 placements)
state:
  - 2 grids exist, the first one active (assumed 3×3)
  - 3 placements (sample-01 top-left / sample-02 center / sample-03 bottom-right), with the center sample-02 selected (DodgerBlue selection border + translucent blue fill)
  - The right-pane Inspector shows the properties of the selected placement
  - Language: English
caption: Three-pane layout of the main window (left: grid list, center: canvas, right: Inspector / candidates)
note: A composition that clearly shows the pane borders and label positions
-->

- **Left pane: grid list** — lists all grids in the workspace. Switch / create / rename / delete
- **Center: canvas area** — displays the current grid at true-scale proportions. Placement / moving / column and row handles
- **Right pane: context-switching** — shows the candidate list when nothing is selected, the Inspector when a placement is selected, and so on

### 1.3.1 Header

The header at the top of the center canvas shows the grid name / cell count / canvas size, alongside the **Preview** and **Export PNG** buttons.

### 1.3.2 Status Bar

At the very bottom of the window. Left: a hint matching the current state / the asset count / an unsaved badge. Right: a history summary plus an icon for opening the history flyout.

## 1.4 Where Data Is Stored

ViewGrid stores all its data under `%LocalAppData%\ViewGrid\`.

```
%LocalAppData%\ViewGrid\
├─ active.json              Name of the currently active workspace
├─ workspaces.json          List of all workspaces
├─ workspaces\
│   └─ <name>\
│      ├─ viewgrid.db       SQLite DB (grids / placements / variants / asset metadata)
│      ├─ images\           The imported image files themselves
│      └─ thumbnails\       Thumbnail cache (webp)
├─ settings.json            App-wide settings (theme / language / accent color, etc.)
└─ logs\                    Serilog logs
```

Settings (theme / language) are **shared across workspaces**. Assets / placements / grids are **isolated per workspace**.

→ §7 [Workspaces](07-workspaces.md) / §10.28 [File Locations](10-reference.md)

## 1.5 Auto-Save and Manual Save

ViewGrid basically **persists edits to the DB immediately** (drag-and-drop placement, renaming, deletion, and so on). However, on screens where **multiple items are edited together**, such as the right-pane Inspector / property-editing tab, changes are not applied until you press the **Save** button (a draft-editing model).

- When the **● Unsaved changes** badge appears in the status bar, there are changes waiting to be saved
- Changes are flushed automatically when the app exits

→ §4.10 [The Inspector Save Button](04-placements.md) / §9.24 [Auto-Save Settings](09-settings.md)
