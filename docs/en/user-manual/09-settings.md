# §9 Settings

This section covers the Settings dialog, which controls the behavior of the app as a whole. Settings are **shared across all workspaces** (because they are per-user preferences).

## Where settings are stored and how they behave

- Physical path: `%LocalAppData%\ViewGrid\settings.json`
- Save timing: each item is **written immediately** when changed (with debounce). There are no OK / Cancel buttons.
- Values are **preserved** when switching workspaces (since these are personal-preference settings, you cannot use different themes for a work workspace versus a hobby workspace).
- Settings are retained when the app is uninstalled (manually delete the entire `%LocalAppData%\ViewGrid\` folder for a full reset).

## 9.26 App Settings dialog

Open the `SettingsDialog` via **File → Settings...**. There are no OK / Cancel buttons; changes are saved immediately.

![Settings dialog](../images/um/um-09-26-settings-dialog.png)

<!-- CAPTURE
file: docs/en/images/um/um-09-26-settings-dialog.png
size: Actual dialog size (520x560, only the portion visible from the top)
samples: (no sample images needed, dialog only)
state:
  - Immediately after opening the Settings dialog (default size 520x560)
  - The 3 sections visible from the top: Language / Display (theme + accent color)
  - Language: Follow system (Default) selected
  - Theme: Follow system (Default) selected
  - Accent color: Blue (default value) selected
caption: Settings dialog (top half: Language / Display sections)
note: Shown opened at the default 520x560. The remaining sections (Defaults / Save behavior / Advanced / Settings import-export) appear when scrolling. Either add a note in the body text such as "scroll within the dialog to..." or add a separate capture of the bottom half.
-->

### 9.26.1 Appearance

#### Theme

A choice of three: Light / Dark / System. Applied immediately (no app restart needed).

| Value | Behavior |
|---|---|
| **Light** | Light background + dark text (default) |
| **Dark** | Dark background + light text |
| **System** | Follows the OS theme setting (Windows 11 Settings → Personalization → Colors → Mode) |

The grid lines and similar elements in the sample images use colors chosen to be readable in both themes, but the output PNG from PhotoBoard has a fixed appearance that is **independent of the theme** (since it is the final output intended as printed material).

#### Accent color

Six presets. Applied to key UI elements such as button highlight color, selection borders, and link color.

| Preset | Color (approximate) | Feel / use |
|---|---|---|
| **Blue** | #2563EB (default) | Calm and businesslike |
| **Sky** | #0EA5E9 | Slightly brighter and fresher |
| **Emerald** | #059669 | Green tones, natural |
| **Violet** | #7C3AED | Creative |
| **Amber** | #D97706 | Warm |
| **Rose** | #E11D48 | Vivid |

Color presets are shown as small dots and selected by clicking. The change is applied immediately, and once clicked it is persisted to the database.

### 9.26.2 Defaults

You can change the initial values used when creating a new Variant or AutoCrop. Existing Variants are not affected.

#### Default scaling

This is the initial scaling mode for the "default Variant" that is created automatically when a new Asset is imported. Choose from six modes (see [§5.11.1](05-shared-properties.md)).

| Situation | Recommended default |
|---|---|
| Many images of the same size | "Original size" |
| Aspect ratios vary widely | "Keep aspect ratio (contain)" (default) |
| Mainly for SNS thumbnails | "Keep aspect ratio (cover / crop)" |

#### Default AutoCrop preset

The target-color preset selected first when you turn AutoCrop on. Choose from White / Black / Transparent (Custom is excluded).

If you mostly work with scanned images choose White, if you mostly work with transparent PNGs choose Transparent, and so on, to reduce the number of clicks needed day to day.

### 9.26.3 Auto-save

#### Default behavior (OFF)

Draft edits in the Inspector or in the property editing tabs are flushed automatically when you **press the Save button** or **switch to another item**. In a normal workflow, even if you forget to press Save, the edit is recovered when you switch.

#### When turned ON

Edits are saved automatically **with debounce** every time a value changes (even with continuous input, they are batched into a single save).

| Benefits | Drawbacks |
|---|---|
| Fewer accidents from forgetting to press Save | Undetermined intermediate values during editing are also added to history |
| The draft ● badge clears frequently, giving peace of mind | Intermediate values from numeric input pollute the history (more Undo steps) |
| Better resilience against crashes | Higher memory consumption for history |

#### Recommendation

- **People who prefer careful editing**: leave it at the default OFF
- **People who use numeric input heavily and do not mind history pollution**: ON
- Because this is treated as experimental, some residual race-condition risk remains (see HANDOFF/pending-designs.md for details).

### 9.26.4 Language

Choose one of three (radio buttons):

| Value | Behavior |
|---|---|
| **system** | Follows the OS language setting. `ja-*` (e.g. `ja-JP`) means Japanese; anything else means English |
| **ja** | Fixed to Japanese |
| **en** | Fixed to English |

#### Scope of immediate switching

The change is **applied immediately across the entire UI without an app restart** (supported by i18n Phases 1 to 3+). Specifically:

✅ Switched immediately:
- Menu bar / status bar / hints
- Settings dialog / About dialog / license information
- Titles and buttons of the various dialogs
- The labels in Grid Properties / Inspector / placement information
- Labels that go through Converters (scaling modes and the like are applied the next time they are re-evaluated)

⚠️ Items where the old wording remains until reloaded:
- Variant display names in the candidate list (the Asset name itself does not change; only the display format is applied on a later load)
- The description of operation history entries (fixed in the language at the time of execution; not updated when the language is switched)

⚠️ Strings stored in the database that are language-independent:
- Grid names / Variant names / numbered strings such as "Variant 1" for the default Variant — these are persisted in the language used at creation time, and are not renumbered when the language is switched.

#### Working after switching the language

After switching the language, if any old wording remains and looks out of place, perform a full reload by **restarting the app** or **switching workspaces**.

### 9.26.5 Regenerating thumbnails

The **Regenerate all thumbnails** button regenerates the thumbnails of every Asset. A progress dialog is shown and can be cancelled. Restarting the app is recommended after completion.

→ §10.29 [Troubleshooting (thumbnail inconsistencies)](10-reference.md)

#### When to run it

Thumbnails are generated automatically on import, so regeneration is normally unnecessary. Run it in the following cases:

- **Thumbnails are corrupted**: some thumbnails in the candidate list or placement preview appear blank, as a black rectangle, or remain an old image
- **A crash occurred during import**: the app crashed and left incomplete thumbnails
- **After manually deleting thumbnail files**: to restore consistency after manual operations by a developer

#### Time required

About 100 to 300 ms per Asset. As a rough guide, 100 Assets takes about 10 to 30 seconds. A large number can take several minutes. It can be cancelled partway through.

#### After completion

The completion screen of the progress dialog displays "Restart recommended". Restarting the app is more reliable because it clears the old thumbnail cache held in memory.

## 9.27 Importing and exporting settings (JSON)

### 9.27.1 Export

Press the **Export...** button, then specify the destination JSON file. The entire current AppSettings is written out to a single JSON file.

#### Example output

```json
{
  "Theme": "Light",
  "AccentColor": "Blue",
  "Language": "ja",
  "DefaultScalingMode": "UniformContain",
  "AutoCropDefaultPreset": "White",
  "EnableAutoSave": false,
  "LastOpenedGridId": "..."
}
```

> **Environment-specific values** such as `LastOpenedGridId` cannot be used as-is on another PC, but if the corresponding Grid does not exist, the app automatically falls back to the "first Grid".

### 9.27.2 Import

Press the **Import...** button, then select a JSON file. The contents of the file completely replace AppSettings, and the dialog display is updated immediately as well.

#### Behavior on import

- If the whole file cannot be parsed, it is rejected ("The JSON is empty or invalid")
- If it can be parsed, the current settings are **completely replaced** (it is not a partial merge)
- The accent color / theme / language are applied immediately
- If there is an unknown enum value, it is rejected with an exception

#### Recovery on failure

If the import fails, the current settings are not changed. If the settings end up corrupted and the app can no longer start, manually delete `%LocalAppData%\ViewGrid\settings.json` so it is regenerated with default values.

### 9.27.3 JSON format

- `WriteIndented = true`, so the file can be hand-edited by a person
- Enums are represented as strings (labels such as `Fill` are stable and compatible across versions)
- Default file name: `viewgrid-settings.json`

#### Cross-version compatibility

Even if a ViewGrid version upgrade adds new settings keys, an old JSON file can still be **imported as-is** (the new keys are filled in with default values). Conversely, if a new JSON file is imported into an older version, the unknown keys are ignored.

### 9.27.4 Use cases

- Sharing settings between different PCs or different users
- Backing up settings for a settings reset
- Standardizing themes / distributing presets within a team

Unlike a workspace (image / Grid data), settings.json is just a text configuration file, so it can be sent casually by email or chat.
