# §5 Shared Properties

This section explains the properties that are tied to a Variant (logical copy) and that affect **all Placements** referencing the same Variant. There are two places to edit them:

- **The "Edit Properties" tab in the right pane** (when a single Variant is selected from the candidate list)
- **The "Shared Properties" Expander in the Placement Inspector** (with a Placement selected, editing that Variant's shared properties)

The result is the same whichever you edit from (both write to the same Variant).

## Application Order (Important)

Shared properties are applied to the image in the following order. The later a transformation comes, the more it "layers on top."

```
Source image
  ↓ ① Cropping (AutoCrop / ManualCrop)        — determines the image's effective area
  ↓ ② Rotation / flip                          — rotates / flips the effective area
  ↓ ③ Scaling + alignment                      — fits within / covers the cell frame
  ↓ ④ Drawing protected regions (region)       — overlaid as independent layers within the cell
  → Done (drawn onto the canvas)
```

For example, you **cannot** specify "rotate the image 90° and then AutoCrop it" (that is the reverse order). Instead, the alternative is to use ManualCrop to directly specify the rectangle after rotation.

## 5.11 Scaling and Alignment

### 5.11.1 Scaling Modes

These determine the behavior when the aspect ratios of the cell frame and the image differ. Select from the ComboBox.

| Mode | Behavior | Typical use case |
|---|---|---|
| **Original size** | Placed at the original pixel size. Overflow is clipped at the cell boundary | Preserving actual size for print |
| **Maintain aspect ratio (contain)** | Sized to **fit within** the cell while preserving the aspect ratio | Standard shrink-to-fit |
| **Shrink only** | Shrinks only when larger than the cell; keeps original size when smaller | When you don't want to enlarge beyond original size |
| **Enlarge only** | Enlarges only when smaller than the cell; keeps original size when larger | Enlarging just the small images to match |
| **Maintain aspect ratio (cover / crop)** | Sized to **cover** the entire cell while preserving the aspect ratio. Overflow is clipped | Thumbnail-style cover images |
| **Fill completely (independent axes)** | Stretches width and height independently to fully fit the cell (aspect ratio is broken) | Fitting to the frame regardless of aspect ratio |

#### Guidance for Choosing a Mode

There are only two axes to consider: **(a) whether to preserve the aspect ratio, and (b) whether to fill the entire cell**.

|  | Fit within the cell (no overflow) | Cover the entire cell (overflows) |
|---|---|---|
| **Preserve aspect ratio** | Maintain aspect ratio (contain) | Maintain aspect ratio (cover / crop) |
| **Ignore aspect ratio** | (none; Original size is the closest) | Fill completely (independent axes) |

- **Print / submission material** → use "Original size" to keep the actual dimensions
- **SNS comparison images / blog thumbnails** → use "Maintain aspect ratio (cover / crop)" to fill the entire cell
- **Reference material (fine as long as it fits the frame while keeping the aspect ratio)** → use "Maintain aspect ratio (contain)"

#### Relationship Between the Mode and PixelOffset / Alignment

With "Maintain aspect ratio (contain)," the image fits within the cell, leaving margins. The position of the margins is determined by the **alignment**. If you still want to fine-tune after exhausting the alignment, use the per-Placement **pixel offset (PixelOffset)** in combination.

→ §4.10.2 [Pixel offset](04-placements.md)

![Scaling mode comparison](../images/um/um-05-11-scaling-modes.png)

<!-- CAPTURE
file: docs/en/images/um/um-05-11-scaling-modes.png
size: comparison composite image (1280x720)
samples: aspect-landscape.png (placed in a 1:1 cell; ideal as material that causes an aspect mismatch)
state:
  - aspect-landscape (1920x1080) placed in a 1:1 cell (e.g. 600x600)
  - The same placement output in all 6 modes (Original / Contain / ShrinkOnly / EnlargeOnly / Cover / Stretch)
  - A composite image laid out with captions
caption: Comparison of the 6 scaling modes
note: tools/CaptureToolkit -- compose-scaling automatically composites the 6 PNGs (docs/images/_raw/composites/scaling-modes/1-None.png through 6-Fill.png) into a 3x2 grid. Comparing the four-corner marker positions of each sample makes the differences clear
-->

### 5.11.2 Alignment

Nine anchors that specify where to place the image within the cell (top-left / top / top-right / left / center / right / bottom-left / bottom / bottom-right).

Alignment is a single setting that serves two roles, with its **meaning changing depending on the size relationship between the image and the cell** (the same convention as CSS `background-position`):

| Situation | What alignment determines | Example |
|---|---|---|
| **Image ≤ cell** | How the margin is distributed = the image's placement position | "Top-left" → pushes the image to the top-left with margins on the right and bottom |
| **Image > cell** | Which side is kept by the clip = the visible portion | "Top-left" → keeps the top-left part of the image and cuts off the right and bottom |

With **Fill completely (Stretch)**, since a broken aspect ratio is assumed, Alignment has no effect.

#### Typical Combinations

- Neatly center a product photo → "Maintain aspect ratio (contain)" + "Center"
- Keep the top half of a landscape photo as a thumbnail cover → "Maintain aspect ratio (cover / crop)" + "Top"
- A person's face is shifted to the right side → adjust the crop position with "Maintain aspect ratio (cover / crop)" + "Right"

![Alignment (9 anchors)](../images/um/um-05-11-alignment.png)

<!-- CAPTURE
file: docs/en/images/um/um-05-11-alignment.png
size: 482x400 (right pane zoomed in, full shared properties editor (Properties tab))
crop: 960,100,482,400
samples: (no sample image needed, UI only)
state:
  - A Variant selected in the candidate list (the Properties tab is open in the right pane)
  - Contents of the Properties tab: Rotation (None) / Flip (horizontal / vertical) / Scaling (Maintain aspect ratio (contain)) / Alignment X+Y (both Center)
  - Save / Reset buttons at the bottom (grayed out = no edits)
caption: Structure of the shared properties editing tab (Properties)
note: In the implementation, alignment is two ComboBoxes (AlignX / AlignY). The "3x3 radio buttons + arrow icons" that the spec envisioned is not implemented (a candidate for a future UI improvement)
-->

## 5.12 Rotation / Flip

### 5.12.1 Rotation

Four steps of 0°/90°/180°/270° (clockwise), selected from the ComboBox. Arbitrary angles are not supported.

Rotation is applied to the image after cropping and is performed before scaling (see the diagram at the start of the §ction on application order). This means that at 90° / 270° the **image's width and height are swapped**, and the fit calculation for the cell is performed using those swapped dimensions.

### 5.12.2 Flip

- **Horizontal (H)**: flips left-right
- **Vertical (V)**: flips top-bottom

Turning both H and V on produces the same result as a 180° rotation.

#### Flip → Rotation Order

Internally, the order applied is **flip → rotation**. Note that "Flip H + 90°" and "90° + Flip H" produce different results. In the UI you do not need to be aware of this order (you simply specify Flip and Rotation independently).

### 5.12.3 Independent Transformation of Protected Regions

A protected region (region) has its own **region-only** rotation / flip, applied independently of the parent Placement's rotation / flip. This handles cases such as "even in output where the photo is rotated 90°, keep only the speech-bubble region horizontal."

→ §5.14.4 [Independent rotation / flip](#5144-independent-rotation--flip)

## 5.13 Cropping

Choose from one of three cropping methods (right pane "Edit Properties" → "Cropping" tab).

| Method | Description |
|---|---|
| **OFF** | No cropping |
| **Automatic (solid-color margin)** | Automatically detects and trims a solid-color outer margin of a specified color (AutoCrop) |
| **Manual (arbitrary rectangle)** | Specifies an arbitrary pixel rectangle (ManualCrop) |

#### Guidance on Which to Use

| Situation | Recommendation |
|---|---|
| You want to remove the white border margin of a scanned image or screenshot all at once | **Automatic (solid-color margin)** + white #FFFFFF |
| You want to trim the margin of a transparent PNG | **Automatic** + transparent (α=0) |
| You want to specify a rectangle "from here to here" by eye | **Manual (arbitrary rectangle)** |
| You want to use two different crops with the same Variant | Fork the Variant and specify **Manual** crops separately |

If "Automatic" produces a crop that differs from your intent, raise the **color tolerance** or switch to **Manual**.

The two cannot be used together (they are mutually exclusive). If you want to fine-tune from a state where Automatic got it 90% right, you'll need to look at the rectangle that Automatic determined and redo it with Manual.

### 5.13.1 AutoCrop (Automatic)

#### Target Color

Select **white #FFFFFF** / **black #000000** / **transparent (α=0)** / **custom** from the ComboBox. For custom, enter a HEX value or click the thumbnail image to sample a color (eyedropper).

#### Color Tolerance (Threshold)

Specified from 0 to 128. At 0, only an exact match is treated as margin (default). The larger the value, the wider the acceptable range.

The decision is made using **Chebyshev distance** (the maximum of the per-channel R / G / B differences):

| Threshold | Meaning |
|---|---|
| **0** | Only an exact match is treated as margin (all of RGB exactly matching) |
| **8** | Enough to tolerate slight JPEG noise |
| **16–32** | Tolerates faint color unevenness in scanned images |
| **64+** | Picks up even subtle gradient margins (be careful not to pick up too much) |

When adjusting, raise or lower the value while checking the result against the **detected rectangle (dotted line)** shown on the preview thumbnail. Increasing it sharply will treat pixels of the subject itself as margin, eating into the subject.

![AutoCrop settings](../images/um/um-05-13-autocrop.png)

<!-- CAPTURE
file: docs/en/images/um/um-05-13-autocrop.png
size: 482x800 (right pane zoomed in, AutoCrop settings)
crop: 960,100,482,800
samples: autocrop-white.png (200px white margin on the outer edge + a 1200x1200 subject inside)
state:
  - The Variant for autocrop-white.png is selected
  - Cropping: "Automatic (solid-color margin)" selected
  - Target color: white #FFFFFF
  - Color tolerance slider value around 16
  - The detected crop rectangle (the 1200x1200 portion) shown as a dotted line on the preview thumbnail
caption: AutoCrop settings + the detected rectangle on the preview thumbnail
note: If taking 3 shots to compare the white / black / transparent presets, use autocrop-black/transparent with the same composition
-->

### 5.13.2 ManualCrop (Manual)

#### Operations on the Thumbnail

Draw a rectangle by dragging on the thumbnail in the right pane. Resize with the 8 handles (4 corners + 4 edges); drag inside to move it.

#### Detailed Editing Dialog

The **Detailed editing (enlarged view)** button opens `ManualCropEditorWindow`.

- Precise editing in an enlarged view
- Numeric input in pixels (X / Y / W / H)
- Pan with a middle-button drag or with `Space` + left drag
- `Ctrl + wheel` to zoom from 6.25% to 1600%
- `Arrow keys` to move 1px, `Shift + arrow` to move 10px

![ManualCrop detailed editing dialog](../images/um/um-05-13-manualcrop-editor.png)

<!-- CAPTURE
file: docs/en/images/um/um-05-13-manualcrop-editor.png
size: actual dialog size (around 1024x768)
samples: sample-01 (grid lines + four-corner markers make the rectangle position easy to read)
state:
  - The ManualCrop detailed editing dialog opened with the Variant for sample-01.png
  - Zoom 400%, editing the rectangle (8 handles shown)
  - Values entered in the numeric input fields (e.g. X=200, Y=200, W=800, H=800)
caption: ManualCrop detailed editing dialog (8 handles + matte display + numeric input)
note: The grid lines on sample-01 make the rectangle position visible to the pixel
-->

## 5.14 Protected Regions (ProtectedRegion)

A feature that "floats" part of the image within a cell as an independent asset. The parent's rotation / flip / alignment are not applied to the region; **only the scale follows**. Valid in both normal mode and PhotoBoard mode.

### 5.14.1 Concept

```
After the parent image is cropped
└─ Protected region = part of the parent image drawn as a separate layer
   ├─ Offset (in px within the cell)
   ├─ Independent rotation / flip (region-only)
   ├─ FillMode (the parent-side fill)
   └─ Crop (the area specified within the region)
```

Use cases such as "keep speech bubbles horizontal," "move only the logo part to a different position," and "emphasize just a specific face."

### 5.14.2 Adding a Protected Region

Edit Properties → "Protected Regions" tab → **Add** button. A default rectangle is created at the center of the thumbnail image.

![Protected Regions tab](../images/um/um-05-14-region-tab.png)

<!-- CAPTURE
file: docs/en/images/um/um-05-14-region-tab.png
size: 482x830 (right pane zoomed in, Protected Regions tab)
crop: 960,100,482,830
samples: region-speech.png (a source image where a speech-bubble-style region is easy to understand)
state:
  - The Variant for region-speech.png is selected, the Protected Regions tab expanded
  - 2 regions registered (1st: a region matching the speech-bubble area; 2nd: a different area)
  - The 1st region selected, showing the detailed editing panel (Offset / Rotation / Flip / FillMode)
caption: Structure of the Protected Regions tab (list + detailed editing of the selected region)
-->

### 5.14.3 Offset (px Within the Cell)

Specify X / Y as integer px. Shifts the position from the cell boundary.

Operations:
- Numeric input
- `Shift` + drag the **orange frame** (RegionSelectionFrame) on the canvas
- `Ctrl + arrow` (1px) / `Ctrl + Shift + arrow` (10px)

### 5.14.4 Independent Rotation / Flip

Give the region itself a rotation in 90° steps and a horizontal / vertical flip (independent of the parent Placement).

### 5.14.5 FillMode (the Parent-Side Fill)

How to fill the original position on the parent image side after the region has been "lifted out."

| FillMode | Behavior |
|---|---|
| **White #FFFFFF** | Fills with white |
| **Black #000000** | Fills with black |
| **Transparent (α=0)** | Punches through at alpha 0 |
| **Custom** | An arbitrary color specified in HEX (can be sampled from the thumbnail with the eyedropper) |
| **As-is (keep the parent image)** | Does not fill (the parent image shows through) |

### 5.14.6 Reordering

Multiple regions are overlaid in array order (= drawing order). Change the order with the **↑** / **↓** buttons in the list.

### 5.14.7 Showing Unselected Regions Simultaneously

Regions that are not selected are also shown on the canvas with a thin gray frame and an opacity of 0.7 (for checking positions). Only the selected one can be manipulated.

### 5.14.8 Application Order of Protected Regions (Detailed)

Regions are overlaid within the cell **after placement and scaling**. The processing for each region is as follows:

```
The drawing of the parent image after scaling (a rectangle on the canvas)
  │
  ├─ Parent-side fill: fills the region's Crop rectangle area according to FillMode
  │            ("hides" the original position on the parent image)
  │
  └─ Region drawing:
     ① Trim the corresponding area of the source image with region.Crop
     ② Apply the region-only flip → rotation (watch out for axis swapping)
     ③ Match the scale with the source → cell scale (independent of the parent's Scaling)
     ④ Position it with region.Offset (px within the cell), clipping at the cell boundary
     ⑤ Overlay it onto the canvas
```

Key points:
- A region is **not affected by the parent's Scaling / Alignment / Rotation** (only the region's own transformations)
- However, the scale is determined by **the same scale factor as the parent** (i.e. the scale of the parent image and the scale of the region match)
- If the crops of the parent image and the region **do not intersect, the region is not drawn** (the intuition that "only what falls within the visible range appears")

### 5.14.9 Tips for Use

- **Initial position of a new region**: It is placed by default at a position that matches the parent-side fill position. By simply setting FillMode to Transparent, you get the result of "cutting the region part out of the parent image"
- **Dragging on the canvas**: The region's **orange frame** represents the selected region. Change the Offset with `Shift + drag`
- **Eyedropper**: Clicking on the parent image's thumbnail samples the color at that position and automatically switches FillMode to Custom
