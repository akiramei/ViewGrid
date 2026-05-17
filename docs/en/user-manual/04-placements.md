# §4 Placements

This section explains how to work with **Placements** — the concrete instances created by putting a Variant into a specific cell of a Grid. For the contrast with shared properties (which are per-Variant), see §1.2.

## 4.9 Creating / Moving / Deleting Placements

### 4.9.1 Creating a Placement (Drag & Drop)

Drag a Variant from the candidate list and drop it onto a cell. When you hover over a cell, it is highlighted in **green** to indicate that a drop is possible.

For a Placement that occupies multiple cells (e.g. W×H of 2×1), the entire group of cells to be occupied is highlighted at once. If it would extend outside the Grid or overlap another Placement, it is shown in **red** to indicate the drop is not allowed.

![Drag & drop placement (valid hover)](../images/um/um-04-09-drop-valid.png)

<!-- CAPTURE
file: docs/en/images/um/um-04-09-drop-valid.png
size: 580x790 (canvas area zoomed in)
crop: 390,100,580,790
samples: sample-01 (placed), sample-02 (being dragged)
state:
  - sample-01 placed in the top-left cell
  - sample-02 being dragged from the candidates, hovering over the top-right 1x1 cell with a green highlight
caption: Hovering over a valid drop target (green highlight)
-->

### 4.9.2 Moving / Swapping Placements

- **Move**: Grab a placed cell and drag it to an empty cell. The offset between the mouse position and the cell boundary is preserved
- **Swap**: Dropping it onto another placed cell swaps the positions of the two
- **Keyboard placement**: Select a candidate and press `Enter` to automatically place it in the first empty cell

### 4.9.3 Selecting / Deselecting a Placement

- Click a Placement → select it (the Inspector opens in the right pane)
- `Esc` → deselect

### 4.9.4 Deleting a Placement

Delete the selected Placement with the **Delete Placement** button at the top of the Inspector. The `Delete` key also works. It is pushed onto the history, so it can be undone.

## 4.10 Placement-Specific Properties (Inspector)

The editing panel shown in the right pane for the selected Placement.

![Placement-specific and shared properties in the Inspector](../images/um/um-04-10-inspector.png)

<!-- CAPTURE
file: docs/en/images/um/um-04-10-inspector.png
size: 482x800 (right pane zoomed in, Inspector)
crop: 960,100,482,800
samples: sample-01 (placed and selected)
state:
  - sample-01 placed and selected, Inspector shown
  - "Placement-Specific" Expander expanded (occupied cells + pixel offset)
  - "Shared Properties" Expander collapsed (same collapse pattern as the output settings)
caption: Structure of the Inspector (placement-specific + shared properties, with the save bar + Delete Placement at the top)
-->

### 4.10.1 Occupied Cells (OccupySize)

Specified as W×H. "1×1" occupies 1 cell; "2×2" occupies 4 cells. Even if you place the same Variant in another Grid or another position, the occupied cells are independent per Placement.

#### Constraints When Expanding

An operation that increases the occupied cells (e.g. 1×1 → 2×2) is **rejected** in the following cases:

- The expanded group of cells would extend outside the Grid
- A different Placement already exists in the expanded cells (overlap)

On error, a message such as "Occupied cells 2×2 would put Placement (1,2) outside the Grid bounds" is shown in the status bar, and the occupied cells are not changed. Shrinking (2×2 → 1×1) always succeeds.

#### Common Patterns

- **Place a wide image to span columns**: Place it as 1×1 → change the occupied cells to 2×1 → enlarge the adjacent column to make the image bigger
- **Use as a full-screen background image**: Change the occupied cells to N×M (the entire Grid)

### 4.10.2 Pixel Offset (PixelOffset)

Specify ΔX / ΔY in pixels from the cell boundary. This lets you finely adjust the display position of a placed image.

How to use it:
- Enter a number + `Enter`
- `Shift` + drag the Placement
- With the Placement selected, `Ctrl + arrow` (1px) / `Ctrl + Shift + arrow` (10px)
- The `Reset to 0` button reverts it

### 4.10.3 Save Bar + Delete Placement

A **Save** button and a **Delete Placement** button are pinned at the very top of the Inspector. Occupied cells and pixel offset are **draft edits** and are not applied until you press the Save button.

#### Behavior of Draft Editing

- At the moment you enter a number, nothing is written to the database; the value is held only within the Inspector
- A **bullet Unsaved changes** badge is shown (in the status bar)
- The Save button persists the change and pushes it onto the history
- Selecting another Placement, switching Grids, or exiting the app triggers an **automatic flush** (the change is saved internally)
- The Esc key or the Reset button discards the draft

Drag-based operations (Shift+drag / Ctrl+arrow), by contrast, are **persisted immediately** (they do not go through a draft). This is so that the on-screen movement and the save timing match.

#### Auto-Save (Experimental)

If you turn on **Enable auto-save** in Settings, draft edits are also flushed automatically each time a value changes (with debounce). Use this when you want to skip the step of pressing the Save button.

→ §9.26.3 [Auto-save](09-settings.md)

## 4.11 Shared Properties (Expander Within the Inspector)

The Expander at the bottom of the Inspector expands the **shared properties** of the selected Placement's Variant. Note that editing here **also affects other Placements that reference the same Variant**.

→ Described in detail in §5 [Shared Properties](05-shared-properties.md)

## 4.12 Placement Position / Size Display

Labels at the top of the Inspector show the **position / occupancy / render area**.

```
Position: (0,0) / Occupies: 1×1
Image render area: 560×400 px
```

- Position: the cell coordinates within the Grid (top-left is `(0,0)`)
- Occupies: the number of W×H cells
- Render area: the actual rendered pixel dimensions, excluding PixelOffset (the result of Scaling/Alignment)

## 4.13 Fitting Grid Weights (FitGridWeight)

A feature that shrinks the occupied column widths / row heights to match the actual render rectangle of a Placement.

- How to use it: with the Placement selected, the **Fit** button in a column header or row header
- The freed space is distributed to adjacent columns/rows (one side is discarded for an edge column)
- Locked columns/rows are skipped
- Undo reverts to the old weights

→ §3.7.2 [Relationship with locks](03-grids.md)

## 4.14 Forking a Variant (Fork)

The **Fork this Placement into a separate Variant** button at the bottom of the Inspector. It creates a new Variant that is a duplicate of the current Variant and switches only this Placement to it. Other Placements that reference the original Variant are not affected.

### 4.14.1 How It Works

```
[Before Fork]
Variant A
  ├── Placement P1   (unchanged)
  ├── Placement P2   ← Fork target
  └── Placement P3   (unchanged)

[After Fork]
Variant A
  ├── Placement P1   (unchanged)
  └── Placement P3   (unchanged)
Variant A' (= a duplicate of A, can be edited independently)
  └── Placement P2   ← reassigned to the new Variant
```

The new Variant's name is auto-numbered (e.g. "Variant 2"). It is added to the candidate list immediately.

### 4.14.2 Use Cases

- **Deriving from a shared setup**: You want to place one Asset both as a "person-centered crop" and a "landscape-centered crop" within the same Grid
- **Experimenting without breaking existing Placements**: You want to try changing shared properties but avoid affecting other Placements
- **Slight variations**: A comparison image lining up the same subject differing only in rotation

### 4.14.3 Notes

- A Fork is pushed onto the history, so it can be undone
- The new Variant after a Fork remains in the candidate list, so other Placements can also reference it via drag & drop (i.e. it can be treated as a plain "independent copy")
- Even if the number of Placements referencing the new Variant drops to zero, the Variant is not deleted automatically (you must explicitly delete it from the candidate list)
