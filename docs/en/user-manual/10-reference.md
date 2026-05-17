# §10 Reference

Keyboard shortcuts, menu list, file locations, troubleshooting, and Glossary.

## 10.28 Keyboard shortcuts

### 10.28.1 File / Grid operations

| Operation | Shortcut |
|---|---|
| Add images | `Ctrl + O` |
| Create new Grid | `Ctrl + N` |
| Export as PNG | `Ctrl + E` |
| Open Settings | (from the menu) |

### 10.28.2 Editing

| Operation | Shortcut |
|---|---|
| Undo | `Ctrl + Z` |
| Redo | `Ctrl + Y` |
| Delete placement | `Delete` |
| Deselect placement | `Esc` |

### 10.28.3 Placement operations

| Operation | Shortcut |
|---|---|
| Pixel offset by 1 px | `Ctrl + Arrow` |
| Pixel offset by 10 px | `Ctrl + Shift + Arrow` |
| Fine adjustment with Shift + drag | Hold `Shift` + mouse drag |

### 10.28.4 Protected region (when selected)

| Operation | Shortcut |
|---|---|
| Region offset by 1 px | `Ctrl + Arrow` |
| Region offset by 10 px | `Ctrl + Shift + Arrow` |
| Edit offset with Shift + drag | Hold `Shift` + drag on the canvas |
| Deselect region | `Esc` (deselects in order: region → placement) |

### 10.28.5 Renaming a Variant

| Operation | Shortcut |
|---|---|
| Start inline rename | `F2` |
| Confirm | `Enter` |
| Cancel | `Esc` |

### 10.28.6 Preview / ManualCrop dialog

| Operation | Shortcut |
|---|---|
| Zoom in | `Ctrl + Wheel ↑` |
| Zoom out | `Ctrl + Wheel ↓` |
| Pan | Middle-button drag or `Space + left drag` |
| Move rectangle by 1 px (ManualCrop) | `Arrow` |
| Move rectangle by 10 px (ManualCrop) | `Shift + Arrow` |

## 10.29 Menu list

### 10.29.1 File

| Item | Action |
|---|---|
| Add images... | [§2.4 Importing images](02-assets.md) |
| Switch workspace... | [§7.21 Workspace management](07-workspaces.md) |
| Settings... | [§9.26 Settings dialog](09-settings.md) |
| Output (Export PNG) | [§6.19 Exporting PNG](06-output.md) |
| Exit | Quit the app (unsaved edits are flushed) |

### 10.29.2 Edit

| Item | Action |
|---|---|
| Undo (Ctrl+Z) | [§8.24 Undo / Redo](08-history.md) |
| Redo (Ctrl+Y) | Same as above |

### 10.29.3 Help

| Item | Action |
|---|---|
| About... | Version information + a button to show the license |
| License information... | Displays the full text of THIRD-PARTY-NOTICES.md |

## 10.30 File / folder locations

```
%LocalAppData%\ViewGrid\
├─ active.json              Name of the active workspace
├─ workspaces.json          List of workspaces
├─ settings.json            App-wide settings (theme / language / defaults)
├─ workspaces\
│   ├─ <name>\
│   │   ├─ viewgrid.db          SQLite DB
│   │   ├─ images\<hash[0..1]>\<hash>.{ext}   Image files
│   │   └─ thumbnails\<hash[0..1]>\<hash>.webp  Thumbnails
│   └─ .trash\<name>-<timestamp>\   Deleted workspaces
└─ logs\viewgrid-*.log      Serilog logs (UTF-8)
```

On Windows 11 the actual path is `C:\Users\<user>\AppData\Local\ViewGrid\`. You can open it directly by entering `%LocalAppData%\ViewGrid` in the Explorer address bar.

## 10.31 Troubleshooting

### 10.31.1 A lock dialog appears when launching a second instance

This is protection against trying to open the same workspace in another process. Activate the existing process and continue editing. If the existing process is just a leftover (for example after a crash), end it in Task Manager and then restart.

### 10.31.2 Thumbnails are corrupted / inconsistent

This can happen when the process crashes during file import. You can regenerate every Asset via **Settings → Regenerate all thumbnails**. Restarting the app is recommended after completion.

### 10.31.3 Clutter accumulates in .trash/

Deleted workspaces are not removed automatically. If you no longer need them, manually delete the contents of `%LocalAppData%\ViewGrid\workspaces\.trash\`.

### 10.31.4 Some text stays in Japanese even after changing the language in the Settings dialog

The wording of candidate list ItemVMs (such as Variant display names) is **applied on the next load**. It is updated immediately by switching workspaces, redisplaying the placement tab, or restarting the app.

### 10.31.5 An edit disappeared / reverted

Edits in the placement Inspector are not persisted until you press the **Save button**. If you close the app or switch Grids while the **● Unsaved changes** badge is shown in the status bar, the edits are normally flushed automatically, but in rare cases this can fail (a restart is needed). Turning on auto-save in Settings can mitigate this (experimental).

### 10.31.6 Checking the logs

Open `%LocalAppData%\ViewGrid\logs\viewgrid-<date>.log` in any text editor (UTF-8). The text will be garbled in a Windows console (cp932).

## 10.32 Glossary

| Term | Description |
|---|---|
| **Workspace** | A unit that physically separates the DB / images / thumbnails (`%LocalAppData%\ViewGrid\workspaces\<name>\`) |
| **Asset** | An imported image file (1 file = 1 Asset) |
| **Variant** (logical copy) | A reusable unit derived from an Asset with different settings. Holds shared properties (rotation / cropping, etc.) |
| **Placement** | An instance of a Variant placed in a specific cell of a Grid. Holds placement-specific properties (occupied cells / pixel offset) |
| **Grid** | An NxM output canvas. Holds column/row ratios and the canvas pixel size |
| **Shared properties** | Properties at the Variant level. Reflected in all placements that reference the same Variant |
| **Placement-specific properties** | Properties at the placement level. Do not affect other placements of the same Variant |
| **Occupied cells** | The number of cells a placement occupies (W×H). Default 1×1 |
| **Pixel offset** (PixelOffset) | ΔX / ΔY (px) from the cell boundary. Fine adjustment of the placement position |
| **Scaling** | The behavior when the aspect ratios of the cell and the image differ (6 modes) |
| **Alignment** | The choice of placement position / visible portion of the image within the cell (9 anchors) |
| **AutoCrop** | A feature that automatically detects and trims the outer margin of a specified color |
| **ManualCrop** | Cropping by specifying an arbitrary pixel rectangle |
| **Protected region** (ProtectedRegion) | A feature that "floats" part of the image within a cell as an independent Asset |
| **PhotoBoard** | A mode that outputs in a photo-board style with a frame + shadow + jitter |
| **TrimMode** | Specifies the trim range of the output PNG (full / occupied cells / drawn pixels) |
| **History flyout** | The operation history list at the right of the status bar. Allows jumping in bulk to a past position |
| **Fork (Variant fork)** | An operation that duplicates an existing Variant per placement and switches to the independent Variant |
