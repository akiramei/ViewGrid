# §8 Operation History

This section covers how to use Undo / Redo and the history flyout.

## 8.24 Undo / Redo

### 8.24.1 How to Use

| Operation | Keyboard | Menu / UI |
|---|---|---|
| Undo | `Ctrl + Z` | **Edit → Undo** / left arrow in the toolbar |
| Redo | `Ctrl + Y` | **Edit → Redo** / right arrow in the toolbar |

The menu items dynamically show a description of the next operation to undo or redo (for example, "Undo: Placement: (Unnamed) → (0,0)", showing the variant's name if it has one).

### 8.24.2 Operations Subject to Undo

- Creating / moving / swapping / deleting a placement
- Changing occupied cells / pixel offset
- Editing shared properties (rotation / flip / scaling / cropping / protected region)
- Changing grid weights / toggling lock / fit
- Renaming a grid / changing the canvas size
- Renaming a variant / forking a variant

### 8.24.3 Operations Not Subject to Undo

- Importing / deleting images
- Creating / deleting a grid
- Creating / deleting a variant
- Workspace operations in general
- Changing settings

Because these have a large cascade impact or would cause history inconsistencies, they clear the entire history.

## 8.25 History Flyout

Clicking the **history summary** on the right side of the status bar (for example, "History: 5/10") opens the flyout.

![History flyout](../images/um/um-08-25-history-flyout.png)

<!-- CAPTURE
file: docs/en/images/um/um-08-25-history-flyout.png
size: 400x420 (actual flyout size + surrounding margin)
crop: 1042,10,400,420
samples: sample-01 to 05 (assets for stacking up 10 operations of history)
state:
  - Pre-shoot preparation: rename the variant of sample-01.png to "Red" and sample-02.png to "Blue" with F2 (leave the rest as "(Unnamed)")
  - After stacking up 10 operations such as placing / deleting / changing column widths using sample-01 to 05
  - 10 history entries shown, current position is the 5th (5 entries undone)
  - The description of each entry is the output of History_*Fmt. Examples: "Placement: Red → (0,0)", "Delete: (Unnamed) (1,0)", "Column width change: Grid Grid 1", etc.
  - The 5th entry and below are grayed out as redo candidates
caption: History flyout (shows the latest 50 entries, for jumping)
-->

### 8.25.1 Bulk Jump

Clicking a history entry performs a **bulk Undo / Redo up to that position** (skipping the intervening operations).

### 8.25.2 Automatic Switch to Another Grid

When the operation being undone belongs to another grid, ViewGrid automatically switches to that grid before performing the Undo / Redo. Walking through the history thus replays operations across multiple grids.

### 8.25.3 Up to the Latest 50 Entries Shown

The history itself is held in memory internally, but the flyout shows only the latest 50 entries. The history is lost when the app restarts (it is not persisted).

### 8.25.4 Language of History Descriptions

The description of each history entry (for example, "Rename: Grid 1 → Working") is recorded in the **app language at the time the operation was performed**. Even if you switch the language later, the descriptions of existing history entries remain in the language used at the time (the history is not recreated).

## 8.26 Persistence and What Is Not Subject to Undo

### 8.26.1 What Is Undone / Redone

- Placements (creating / moving / swapping / deleting)
- Placement-specific properties (occupied cells / pixel offset)
- Shared properties (rotation / flip / scaling / alignment / cropping / protected region)
- Grids (renaming / canvas size / weight / lock / fit)
- Variants (renaming / fork)

### 8.26.2 Operations That Clear the Entire History

The following operations involve a cascade delete, so the history is cleared entirely before they are executed (there is no way to restore the state from before they ran):

- Importing / deleting an asset
- Creating / deleting a variant
- Creating / deleting a grid
- Switching workspaces

Before performing these, you can export important states as a PNG so that recovery is possible.

### 8.26.3 Persistence of the History

The history is held **in memory only** and is not saved to the DB. It is lost when the app exits. This is because:

- Accumulating history puts pressure on memory
- Restoring history in another session could break the integrity of the cascade

For these reasons, it is kept as a feature limited to within a single session.
