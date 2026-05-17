# §3 Grids

This section explains how to create, edit, and switch between Grids (NxM canvases).

## 3.6 Creating / Editing Grids

### 3.6.1 Creating a New Grid

The **+ New** button at the top of the left pane (`Ctrl+N`) opens the creation flyout.

| Field | Description | Default |
|---|---|---|
| Name | Display name of the Grid | Grid N (auto-numbered) |
| Columns / Rows | Number of subdivisions in the Grid | 3 / 3 |
| Canvas width / height (px) | Maximum dimensions of the output PNG | 1200 / 1200 |
| Column ratios / Row ratios | Comma-separated values for individual sizing (equal if left blank) | (blank) |

![New Grid creation flyout](../images/um/um-03-06-create-grid-flyout.png)

<!-- CAPTURE
file: docs/en/images/um/um-03-06-create-grid-flyout.png
size: 880x330 (left pane zoomed in, full Grid creation flyout)
crop: 0,0,880,330
samples: (no sample image needed, UI only)
state:
  - Creation flyout expanded, defaults unchanged (Grid N / 3x3 / 1200x1200)
  - Language: English
caption: New Grid creation flyout
note: All fields and the Create / Cancel buttons are visible
-->

### 3.6.2 Editing the Grid Name / Canvas Size

You can edit the name and canvas size under **Grid Settings** in the right pane (with only the Grid selected). Make draft edits, then confirm with the **Save** button (cancel with Esc / Reset).

![Grid Settings (right pane)](../images/um/um-03-06-grid-properties.png)

<!-- CAPTURE
file: docs/en/images/um/um-03-06-grid-properties.png
size: 482x600 (right pane zoomed in, Grid properties shown)
crop: 960,100,482,600
samples: (no sample image needed, Grid only selected)
state:
  - Grid selected (whether or not it has Placements), Grid Settings shown in the right pane
  - Name edited into a draft state (showing the bullet badge)
caption: Grid Settings in the right pane (draft editing + Save button)
-->

## 3.7 Adjusting Column / Row Ratios (Weights)

### 3.7.1 Dragging Boundaries

When you move the mouse near a boundary line between cells, the cursor changes to a resize shape. Drag it to adjust the widths of the left/right or top/bottom cells.

![Changing column width by dragging a boundary](../images/um/um-03-07-boundary-drag.png)

<!-- CAPTURE
file: docs/en/images/um/um-03-07-boundary-drag.png
size: 580x790 (canvas area zoomed in)
crop: 390,100,580,790
samples: sample-01 through sample-06 (6 of the 9 cells in a 3x3 grid filled; the grid lines make boundary movement easy to see)
state:
  - sample-01 through sample-06 placed in a 3x3 Grid (canvas around 1500x1500)
  - The left boundary of the center column is being dragged, with the boundary line emphasized as a thicker line
  - The grid lines on the sample images make the boundary movement readable to the pixel
caption: Adjusting column width by dragging a boundary
-->

### 3.7.2 Relationship with Locks (🔒)

When you lock a column or row using the 🔒 toggle in its header, the width/height of those cells no longer changes from boundary dragging or from "fitting to occupied cells."

Boundary lines adjacent to a locked cell turn **orange** to indicate that they cannot be moved.

→ §4.13 [Fitting a Placement (FitGridWeight)](04-placements.md)

#### Operations Affected / Unaffected by Locks

| Operation | Effect of the lock |
|---|---|
| Boundary dragging | Boundaries adjacent to a lock cannot be dragged (shown in orange) |
| Grid fit (Inspector) | Locked columns/rows are skipped; the freed space is distributed to the unlocked side |
| Canvas size change | No effect (ratios are preserved and everything scales uniformly) |
| Recreating the Grid | No effect (a new Grid is created with no locks) |

#### Typical Uses of Locks

- **Pin a header row**: Lock the first row at a specific height, then freely adjust Placements in the remaining rows
- **Protect an even layout**: Lock Placements arranged at the same size so a stray boundary drag doesn't disrupt them
- **Keep just one side flexible**: Leave only the center column unlocked and lock the left and right, so "only the center column's width can be adjusted"

## 3.8 The Grid List (Left Pane)

### 3.8.1 Switching

Click an item in the list to switch. Any unsaved edits are automatically flushed when you switch.

### 3.8.2 Renaming

Double-click a Grid name for inline renaming. Alternatively, edit it from Grid Settings in the right pane.

### 3.8.3 Deleting

Use the menu to the right of a list item → Delete. Because Placements are also removed by cascade, a confirmation dialog appears. The operation history is cleared entirely (deletion cannot be undone).

### 3.8.4 Restoration on Startup

The last opened Grid is persisted as the `LastOpenedGridId` setting and is automatically selected on the next startup.

Selection rules:
- The Grid exists (has not been deleted) → make that Grid active
- The Grid is not found → make the first Grid in the Grid list active
- There are no Grids at all → select nothing (prompt to create a new one)

## 3.9 Editing the Canvas Size

A Grid's **canvas size** (the width and height in pixels of the output PNG) can be edited from the right pane.

### 3.9.1 Draft Editing and Saving

- Enter a number → draft state (showing the bullet unsaved badge)
- The **Save** button persists the change and pushes it onto the history
- **Reset** / `Esc` discards the draft

### 3.9.2 Behavior While in a Draft

Even while in a draft, the overall canvas size on screen does not change; the display **scales uniformly while preserving the ratios** (since the column/row weights are not changed, every cell scales up/down by the same proportion).

In other words, even if you change 1200×1200 to 2000×2000, the **proportion of cells occupied** by each Placement stays the same. The only difference is the absolute pixel count of the output PNG.

### 3.9.3 Constraints

- Minimum: 100×100 px
- Maximum: 8192×8192 px (a practical upper limit; larger values make PhotoBoard compositing noticeably slower)
- The aspect ratio is unrestricted
