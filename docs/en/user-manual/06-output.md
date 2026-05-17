# §6 Output

This section covers preview and PNG export, along with settings such as the output mode (Normal / PhotoBoard) and the trim mode.

## 6.15 Output Mode

Switch modes from the **Output Settings** expander at the top of the right pane.

| Mode | Description |
|---|---|
| **Normal** | Renders the cell placements of the grid exactly as they are |
| **PhotoBoard** | Recomposites the placements photo-board style, adding frames, shadows, and jitter |

![Output Settings expander](../images/um/um-06-15-output-settings.png)

<!-- CAPTURE
file: docs/en/images/um/um-06-15-output-settings.png
size: 482x600 (right pane close-up, Output Settings expander)
crop: 960,100,482,600
samples: sample-01 to 04 (placed in a 2x2 layout, partly visible in the background)
state:
  - sample-01 to 04 placed in a 2x2 grid
  - Output Settings expander in the right pane is expanded, Normal mode / Full trim
  - If a separate shot of the collapsed header summary "Normal / Full" is available, add it too
caption: Expanded state of the Output Settings (mode / style / intensity / trim)
-->

## 6.16 PhotoBoard Details

This covers the parameters used in photo-board style mode.

### 6.16.1 What Is PhotoBoard?

Whereas Normal mode produces a "tiled" output that strictly respects cell boundaries, PhotoBoard mode treats each placement as an **independent photo** and outputs a composite image with frames, shadows, and jitter applied. The result looks like "an overhead view of photos laid out on a desk."

Normal mode and PhotoBoard mode differ **only in the rendering pipeline** used at output time; the grid and placement data are shared. You can switch between them at any time during a session (the choice is not persisted).

### 6.16.2 Style Presets

Ten styles are provided. Each preset is tuned with a combination of the following:

- Frame (white border / black border / none) and its thickness
- How the shadow falls (distance / blur / color)
- Jitter (tilt / positional offset)
- Naturalness of overlap (i.e., the "human feel" of the two-stage curve)

Guidance on choosing a style:
- **A safe, tidy look** → simple Polaroid-family presets
- **Lively, pop** → styles with stronger jitter
- **Monochrome / chic** → dark-family presets

### 6.16.3 Intensity Slider

Adjusts the "human feel" of the chosen style — the strength of the two-stage curve — from subtle to bold. The default of 0.5 represents "the baseline for that style."

- Toward 0.0 (subtle): jitter and tilt are suppressed for a neat, orderly look
- 0.5 (default): the style's intended character
- Toward 1.0 (bold): jitter and tilt are emphasized further, for a "tossed onto the desk" feel

The **Reset to default** button resets the slider to 0.5. The slider also resets automatically to 0.5 when you switch styles (the assumption being that each style slides from its own reference point).

### 6.16.4 Computation Cost and Preview

The final PhotoBoard composite takes **about 1 second** to produce. For this reason, **real-time adjustment is not recommended** while dragging the intensity slider.

Recommended workflow:
1. Change the settings in the right pane
2. Press the **Preview** button to check the result
3. If you are not satisfied, readjust in the right pane and preview again

![PhotoBoard style comparison](../images/um/um-06-16-photoboard-styles.png)

<!-- CAPTURE
file: docs/en/images/um/um-06-16-photoboard-styles.png
size: comparison composite image (1200x500, three styles side by side)
samples: photo-01.png, photo-02.png, photo-03.png, photo-04.png (photographic images that showcase the PhotoBoard effect)
state:
  - photo-01 to 04 in an identical 2x2 placement
  - Export each of the three styles (Natural / Rough / Scattered) as a PNG in order
  - Keep the intensity at 0.5 for all
caption: Comparison of the three PhotoBoard styles
note: Composite image. Use tools/CaptureToolkit -- compose-photoboard to auto-compose the three PNGs into a 3x1 grid
-->

## 6.17 Trim Mode

The final trim range specification, applied in both Normal mode and PhotoBoard mode.

| Mode | Trim range |
|---|---|
| **Full** | The entire canvas (canvas width × height) |
| **Occupied cells** | The bounding box of the group of cells occupied by placements |
| **Drawn pixels** | The bounding box found by scanning for pixels with α > 0 |

"Full" gives a fixed output size, "Occupied cells" covers only the placement range, and "Drawn pixels" covers only the area actually drawn. In PhotoBoard mode, the bounding box is calculated including the results of shadows and jitter.

### 6.17.1 Choosing a Mode

| Situation | Recommended mode |
|---|---|
| **You always want a PNG of the same size** (e.g., to match the spec for social media posts) | Full |
| **You want to output only the necessary placements, without empty cells** | Occupied cells |
| **In PhotoBoard, you want a tight trim that includes the photo tilt** | Drawn pixels |
| **You want to output an icon asset from a transparent-background canvas** | Drawn pixels (no margin) |

### 6.17.2 Concrete Examples for Each Mode

For a 3×3 grid (canvas 1200×1200), with one placement in the top-left 2×2 cells and one placement in the bottom-right 1×1 cell:

| Mode | Output PNG size | Content |
|---|---|---|
| Full | 1200×1200 | The entire canvas (the center cell, top-right, and bottom-left are empty — transparent or background color) |
| Occupied cells | About 1200×1200 | The range that merges the bounding box of the top-left placement with that of the bottom-right placement (in this example, almost the same as Full) |
| Drawn pixels | About 1000×1000 | Only pixels with α > 0. If the placement's scaling result leaves transparent areas, those are omitted |

In PhotoBoard mode, the bounding box expands to accommodate the jitter (tilt) and shadows. Trimming with "Drawn pixels" gives a size where "the corners of the photos fit exactly."

### 6.17.3 Notes

- With "Occupied cells" or "Drawn pixels," the output size varies depending on the placement, so use "Full" when you want a consistent size
- "Drawn pixels" relies on α scanning, so even very faint semi-transparent pixels (for example, anti-aliased edges) are included. If the resulting size is larger than expected, check whether semi-transparent areas are scattered across the canvas

## 6.18 Preview

The **Preview** button in the header opens the `PreviewWindow`.

![Preview window](../images/um/um-06-18-preview.png)

<!-- CAPTURE
file: docs/en/images/um/um-06-18-preview.png
size: actual size of the preview window (1280x900)
samples: sample-01 to 04 (2x2 placement)
state:
  - Preview of a grid with sample-01 to 04 placed in a 2x2 layout, zoom at 100%
  - The zoom bar (- / Fit / 100% / + / magnification display) is visible
caption: Layout of the preview window
-->

### 6.18.1 Zoom Controls

- **Fit**: a size that fits within the window
- **100%**: physical pixels at 1:1 (the same size as the output PNG)
- **+ / -**: step zoom in / out (also possible with Ctrl + wheel)
- Anchor-preserving zoom (zooms in and out centered on the mouse position)

### 6.18.2 Regeneration

Previews are cached. They are regenerated automatically right after you change a placement or a setting (about 0.5 to 1 second).

## 6.19 PNG Export

### 6.19.1 How to Export

- The **Export PNG** button in the header
- The menu **File → Output**
- `Ctrl + E`

The native OS save dialog opens, where you specify the file name.

### 6.19.2 Output Specifications

- Format: PNG (lossless)
- Color: RGBA (preserves α if there is transparency, e.g., with FillMode.Transparent)
- Resolution: the actual size based on the TrimMode
- Margin: no outer margin around the bounding box in "Occupied cells" and "Drawn pixels" modes
