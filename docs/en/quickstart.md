# ViewGrid Quick Start

This guide gives first-time ViewGrid users the shortest path to **exporting their first combined image as a PNG in 5 to 10 minutes**. It omits detailed explanations of concepts and individual features. If you want to know about finer behavior, refer to the [User Manual](user-manual/README.md).

---

## 1. What is ViewGrid

ViewGrid is a desktop app that arranges multiple images on a grid (an NxM array of cells) and exports them as a single combined image (PNG).

Main uses:

- Comparison images for product catalogs / SNS posts (Before / After, etc.)
- Photo-board-style composite images
- Arranging images on any N×M grid with any canvas size

![ViewGrid main window](images/qs/qs-01-01-main-window-overview.png)

<!-- CAPTURE
file: docs/en/images/qs/qs-01-01-main-window-overview.png
size: 1280x800
samples: sample-01.png, sample-02.png, sample-03.png, sample-04.png
state:
  - Workspace: Default (immediately after creation)
  - Assets: sample-01 to 04 already imported
  - Grid: 2×2 (canvas 1200×1200), 2 to 3 placements (e.g. sample-01 top-left + sample-03 bottom-right)
  - Language: English
  - Right pane: default hint shown with nothing selected
caption: Overview of the app after launch (three-pane layout)
-->

---

## 2. Installation and launch

ViewGrid runs simply by **launching a single executable (`ViewGrid.Presentation.exe`) directly**. There is no need to install a .NET runtime separately.

1. Place the distributed exe in any folder (e.g. `C:\Apps\ViewGrid\`)
2. Double-click the exe to launch it
3. On first launch, a data folder is created automatically under `%LocalAppData%\ViewGrid\` (the storage location for the image files / thumbnails / DB / logs / settings)

> **The first launch may take a moment**: because the single exe extracts the runtime internally, there is an overhead of a few seconds on the first launch only. From the second launch onward it starts instantly.

---

## 3. 5-minute tutorial: exporting your first image

What you will make in this section: **arrange 4 images on a 2×2 grid and turn them into a single PNG**.

### 3-1. Import images

Drag and drop images onto the right pane of the main window, or import them via **File → Add images...** in the menu.

![Importing images by drag and drop](images/qs/qs-03-01-drag-drop-images.png)

<!-- CAPTURE
file: docs/en/images/qs/qs-03-01-drag-drop-images.png
size: 1280x800
samples: sample-01.png, sample-02.png, sample-03.png, sample-04.png (4 files in Explorer)
state:
  - Workspace: empty (immediately after Default)
  - Selecting the 4 files sample-01 to 04 in Explorer and dragging them onto the main window
  - Cursor: the OS Copy indicator (a + mark) is shown (drop accepted)
  - Language: English
caption: Importing images from Explorer by drag and drop
note: A composition showing the semi-transparent icon during the drag + the Copy cursor. ViewGrid does not currently highlight the whole panel (the drop is indicated only by the OS cursor change).
-->

Imported images appear in the **candidate list** (right pane) as **Variants** (logical copies of the image).

### 3-2. Create a Grid

Click the **"+ New"** button at the top of the left pane. Input fields for the name / number of columns / number of rows / canvas size appear.

![New Grid creation flyout](images/qs/qs-03-02-create-grid-flyout.png)

<!-- CAPTURE
file: docs/en/images/qs/qs-03-02-create-grid-flyout.png
size: 880x330 (left pane enlarged, the whole title + menu + Grid toolbar + flyout)
crop: 0,0,880,330
samples: (no sample images needed, UI only)
state:
  - Immediately after pressing "+ New" so the creation flyout has opened
  - Input values: name "Grid 1" / columns 2 / rows 2 / canvas width 1200 / height 1200
  - Language: English
caption: New Grid creation flyout (2 columns × 2 rows, 1200×1200 px)
note: Focus is on the name input field. The raw file is docs/images/_raw/qs/qs-03-02-create-grid-flyout.png
-->

Once you have entered the values, press the **Create** button. A 2×2 grid appears in the canvas area in the center.

### 3-3. Drag images onto cells to place them

From the candidate list in the right pane, drag and drop image thumbnails onto the grid cells. Place the 4 images in the order top-left → top-right → bottom-left → bottom-right.

![Dragging from the candidate list onto a cell](images/qs/qs-03-03-drag-to-cell.png)

<!-- CAPTURE
file: docs/en/images/qs/qs-03-03-drag-to-cell.png
size: 1280x800
samples: sample-01 (already placed top-left), sample-02 (being dragged)
state:
  - A 2×2 Grid (canvas 1200×1200) already created
  - sample-01 already placed in the top-left cell
  - sample-02 from the candidate list is being dragged and is hovering over the top-right cell (green highlight)
  - Language: English
caption: Drag and drop from the candidate list onto a cell
note: The thumbnail preview during the drag + the highlight of the destination cell + an already-placed cell are all visible at once.
-->

The placed cell is filled with the image, and the message "Placed" appears in the status bar.

### 3-4. Preview and export PNG

You can check the result with the **Preview** button at the right of the header.

![Preview window](images/qs/qs-03-04-preview-window.png)

<!-- CAPTURE
file: docs/en/images/qs/qs-03-04-preview-window.png
size: 1280x800 (both the preview window and the parent window are visible)
samples: sample-01 to 04 (already placed in a 2×2)
state:
  - The preview window with sample-01 to 04 already placed on a 2×2 grid
  - Zoom level: 100% (physical pixels at 1:1)
  - Language: English
caption: Checking the result in the preview window
note: A position where the "Fit" and "100%" buttons of the zoom bar are visible (the actual UI has 4 buttons: "−", "Fit", "100%", "+").
-->

If everything looks fine, save it as a PNG with the **Export PNG** button (or the menu **File → Output** / `Ctrl+E`). Specify a file name in the save dialog to finish.

![Export PNG dialog](images/qs/qs-03-05-export-png-dialog.png)

<!-- CAPTURE
file: docs/en/images/qs/qs-03-05-export-png-dialog.png
size: Actual size (the whole OS-native dialog)
samples: (no sample images needed, OS dialog only)
state:
  - The Windows "Save as PNG" dialog after pressing the Export PNG button
  - Suggested file name: viewgrid-export.png (or the current automatic naming)
  - Language: English
caption: The OS-native save dialog
note: The dialog title "Save as PNG" must be clearly visible.
-->

Your first image is now complete.

---

## 4. Just learn this and you are ready to go

Here are the frequently used operations summarized on a single page.

### Keyboard shortcuts (most common)

| Operation | Shortcut |
|---|---|
| Add images | `Ctrl+O` |
| Create new Grid | `Ctrl+N` |
| Export as PNG | `Ctrl+E` |
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Y` |
| Deselect placement | `Esc` |

### Mouse operations

| Operation | How to |
|---|---|
| Change column width / row height | Drag a grid boundary line |
| Pixel offset of a placement | `Shift` + drag the placement |
| Move a placement to another cell | Drag a placed cell onto another cell |
| Swap two placed cells | Drop onto another placed cell |
| Rename a Variant in the candidate list | Select the Variant → `F2` |

### Scaling mode quick reference

This determines the behavior when the aspect ratios of the cell frame and the image differ (right pane → Scaling).

| Mode | Behavior |
|---|---|
| Original size | Places the image at its original pixel size (overflow is clipped at the cell boundary) |
| Keep aspect ratio (contain) | Sized to fit within the cell while keeping the aspect ratio |
| Keep aspect ratio (cover / crop) | Completely fills the cell while keeping the aspect ratio; the overflow is clipped |
| Full fill (stretch independently) | Stretches width and height independently to fit the cell exactly (the aspect ratio is distorted) |

---

## 5. Next steps

The features beyond this point are explained in detail in the User Manual. Look them up based on what you want to do.

| What you want to do | Reference |
|---|---|
| Automatically trim the margins of an image | [User Manual §5.13 Cropping (AutoCrop)](user-manual/05-shared-properties.md) |
| Crop with an arbitrary rectangle | [User Manual §5.13 Cropping (ManualCrop)](user-manual/05-shared-properties.md) |
| Output in a photo-board style (frame + shadow + tilt) | [User Manual §6.16 PhotoBoard details](user-manual/06-output.md) |
| Float just part of an image as a separate image (protected region) | [User Manual §5.14 Protected regions](user-manual/05-shared-properties.md) |
| Use multiple workspaces for different purposes | [User Manual §7 Workspaces](user-manual/07-workspaces.md) |
| Undo a past operation | [User Manual §8 Operation history](user-manual/08-history.md) |
| Change the language / theme / accent color | [User Manual §9 Settings](user-manual/09-settings.md) |
| See the list of keyboard shortcuts | [User Manual §10.26 Keyboard shortcuts](user-manual/10-reference.md) |

If you come across an unfamiliar term, also refer to the [User Manual §10.30 Glossary](user-manual/10-reference.md).
