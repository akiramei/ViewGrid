# §2 Asset Management

This chapter explains how to import images and how to manage the **variants (logical copies)** derived from imported images.

## 2.4 Importing Images

### 2.4.1 Supported Formats

PNG / JPEG / GIF / WebP / BMP. Transparency (RGBA) is handled correctly only for PNG and WebP. Animated GIFs are not supported (only the first frame is used).

### 2.4.2 Import Methods

**Method A: Drag and drop**
Select multiple files in Explorer and drop them onto the main window.

**Method B: Menu / button**
**File → Add Images...**, or the **+ Add Images** button at the top of the right pane (`Ctrl+O`). The OS-native file picker opens.

![Importing images (file picker)](../images/um/um-02-04-add-images-picker.png)

<!-- CAPTURE
file: docs/en/images/um/um-02-04-add-images-picker.png
size: Actual picker size plus surrounding margin
samples: (no sample images needed, just the picker. However, it is clearer if sample-*.png are visible in the list)
state:
  - Immediately after launching the OpenFilePicker from the menu / button
  - Title: "Select Images" (since this is the English version)
  - Filter: "Images (*.png; *.jpg; ...)"
  - Ideally the picker list shows docs/sample-images/ opened with sample-*.png lined up
caption: The file picker used for importing images
note: The picker title and filter name are visible
-->

### 2.4.3 Behavior After Import

1. The image file itself is copied to `%LocalAppData%\ViewGrid\workspaces\<name>\images\<hash[0..1]>\<hash>.{ext}` (the original file is left untouched)
2. A thumbnail is generated automatically at `thumbnails\<hash[0..1]>\<hash>.webp`
3. For each asset, **1 default variant is created automatically** (it appears in the candidate list right away)

#### Hash-Based Duplicate Detection

Because ViewGrid uses the **SHA256 hash** of the image file itself as the file name, **images with the same byte sequence are treated as the same asset**. In other words:

- Importing the same image twice still results in only one instance in the DB / on disk (and only one asset)
- Even with different file names, identical content is treated as a duplicate (`photo.jpg` and `photo-copy.jpg` are identical if they have the same byte sequence)
- If even 1 byte differs, it is a separate asset

#### Advantages of the Storage Structure

- Files are distributed into subfolders by the first 2 hash digits (`images\3a\3a5c8e...png`), avoiding the Windows Explorer problem of too many files in one folder
- Backup integrity: because the correspondence between the DB and images is uniquely determined by hash, it is not broken by file-by-file copying
- Complete deletion: when an asset is deleted, its variants / placements are cascade-deleted and the image file itself is also removed from disk

## 2.5 Variants (Logical Copies)

### 2.5.1 What Is a Variant

A "settings-differentiated clone" you create when you want to use the same asset with different cropping / rotation / scaling settings. You choose which variant to use at placement time.

Example: if you register the same landscape photo as two variants — **(A) a portrait crop focused on a person** and **(B) a landscape crop showing the full scene** — you can use them separately within the same grid.

### 2.5.2 Creating a New Variant

Select an asset in the candidate list and click the **"New Variant"** button (with a + icon). Often the workflow is to duplicate an existing variant and then edit it.

![Adding a variant](../images/um/um-02-05-add-variant.png)

<!-- CAPTURE
file: docs/en/images/um/um-02-05-add-variant.png
size: 400x600 (crop of the candidate pane, showing both the buttons and an expanded variant)
crop: 0,200,400,600
samples: sample-01 (source asset), 2 variants of sample-01
state:
  - The sample-01.png asset group is expanded (2 variants: "(Unnamed)" and 1 copy with a custom name)
  - The "New Variant" button (with a + icon) is visible at the bottom of the candidate pane
caption: Adding a variant from the candidate list
-->

### 2.5.3 Fork

When you want "only this placement to use a different variant" for an already-placed variant, the Inspector's **Fork this placement into a separate variant** button lets you switch it to an independent variant that duplicates the current one.

→ §4.10 [Fork in the Placement Inspector](04-placements.md)

### 2.5.4 Renaming a Variant

Select a variant in the candidate list → press `F2` for an inline rename. `Enter` confirms, `Esc` cancels.

### 2.5.5 Deleting a Variant

Select a variant in the candidate list and click the **Delete** button. The placements that reference that variant are also removed by cascade (a confirmation dialog is shown).

## 2.6 Structure of the Candidate List

The candidate list is **grouped by asset**.

```
📁 photo-001.jpg (asset name)
   ├─ Variant 1   (3° rotation / crop A)
   └─ Variant 2   (original, unchanged)
📁 photo-002.jpg
   └─ Variant 1
```

- Click an asset row → expand / collapse all its variants
- Click a variant row → the **shared-property editing tab** for that variant opens in the right pane
- Drag a variant onto a cell → creates a placement

→ Placement methods are explained in detail in §4 [Placements](04-placements.md)

### 2.6.1 Persistence of Group Expansion State

The expanded / collapsed state of each asset group is retained **within the session**. It is reset when you switch workspaces or restart the app. Because the instance is preserved when the candidate list is reloaded, the state is maintained within the same session.

### 2.6.2 Sorting of Variant Names

Within each asset, variants are listed in **creation order**. Renaming or editing them does not change their sort position. There is currently no reordering feature.

## 2.7 Thumbnail Cache

The thumbnails in the candidate list and the placement screen are cached in WebP format at `%LocalAppData%\ViewGrid\workspaces\<name>\thumbnails\<hash[0..1]>\<hash>.webp`.

### 2.7.1 Automatic Regeneration

Thumbnails are generated when an asset is imported. After import they are merely read from the cache and are not normally regenerated.

### 2.7.2 Manual Regeneration

If a thumbnail is corrupted or fails to display, you can rebuild the thumbnails for all assets via **Settings → Regenerate All Thumbnails**. A progress dialog appears and can be canceled.

→ §9.26.5 [Thumbnail Regeneration](09-settings.md)
