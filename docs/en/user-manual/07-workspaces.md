# §7 Workspaces

This section covers the feature for physically separating and switching between multiple workspaces. It prevents accidents where work and hobby assets get mixed together — **not by tags or search filters, but at the level of the DB file**.

## Intended Use Cases

| Case | Recommended configuration |
|---|---|
| **Preventing a work / hobby mix-up** | Separate into two workspaces, `work` and `hobby`. Prevents accidents during screen sharing |
| **Separation by client** | One workspace per project, such as `client-a` / `client-b`. Prevents leakage of assets under a non-disclosure agreement |
| **Separating prototype from production** | Split the stable and experimental versions with `production` / `experiment`. The experimental version can be deleted freely |
| **Archiving by theme** | Archive by period, e.g., `2025-spring` / `2025-summer` |
| **A single user (mixing is fine)** | Use only the default `Default` workspace |

## 7.20 What Is a Workspace?

### 7.20.1 The Point of Physical Separation

With tags or a Project entity within a single DB, a single act of clearing the filter exposes every item, leaving a risk of accidents during screen sharing or when displaying your screen to others. By **separating the DB file itself, the image files, and the thumbnails into separate folders**, ViewGrid shifts the switching model to "restart the app" and structurally prevents mixing.

### 7.20.2 One Workspace, One Process

Because of file-based locking, the same workspace cannot be opened from two processes. If you try to launch a second instance, the **WorkspaceLockedDialog** is shown and you are directed to the existing process.

#### How the Lock Works

- At app startup, the `workspaces/<name>/viewgrid.lock` file is acquired (opened exclusively)
- If you try to open the same workspace in another process, lock acquisition fails and the dialog is shown
- On a normal exit the lock is released; even on a crash, the OS releases the exclusive file handle

#### A Different Workspace Can Be Launched at the Same Time

Even while the `work` workspace is running, you can launch `hobby` in a separate process (they each have a separate DB, so there is no conflict).

### 7.20.3 Storage Structure

```
%LocalAppData%\ViewGrid\
├─ active.json              { "name": "work" }
├─ workspaces.json          [{ "name": "work", "displayName": "Work" }, ...]
└─ workspaces\
    ├─ Default\             existing users are migrated automatically
    │   ├─ viewgrid.db
    │   ├─ images\
    │   └─ thumbnails\
    ├─ work\
    └─ hobby\
```

For users who updated from an older version, `%LocalAppData%\ViewGrid\viewgrid.db` is automatically migrated under `workspaces\Default\` (assets and thumbnails are moved along with it).

## 7.21 Create / Switch / Rename / Delete / Duplicate

Open the `WorkspaceSwitchDialog` from **File → Switch Workspace...**.

![Switch Workspace dialog](../images/um/um-07-21-workspace-switch.png)

<!-- CAPTURE
file: docs/en/images/um/um-07-21-workspace-switch.png
size: actual dialog size (800x600)
samples: (no sample images needed, dialog only)
state:
  - Three workspaces (Default / work / hobby) shown as cards
  - Each card has Switch / Rename / Delete / Duplicate buttons
  - At the bottom, actions for creating a new workspace and importing a zip
  - Default is active and has a highlight border
caption: Switch Workspace dialog
note: The currently active workspace card has a highlight border
-->

### 7.21.1 Switching

The **Switch** button on another workspace card → confirmation dialog → the app restarts automatically. Internally, `active.json` is rewritten, followed by `Process.Start` + `Application.Shutdown()`.

#### Behavior When Switching

1. **Flush of unsaved edits**: completes all drafts and saves in progress
2. **Rewrite `active.json`**: updates the active workspace name
3. **Launch a new process**: starts ViewGrid afresh with the target workspace
4. **Exit the current process**: terminates the current process gracefully

Right after switching, the behavior is the same as a fresh launch, so there is a startup overhead (1 to 2 seconds). Frequent switching is not recommended (it is intentionally designed so that you "cannot switch lightly," to prevent mix-up accidents).

#### Effect on History and Settings

- **Operation history**: held in memory per workspace, so it is lost on a switch (since it is not persisted, the history is not restored even if you switch back to the original workspace)
- **App settings** (theme / language / defaults): shared across workspaces, so they do not change on a switch

### 7.21.2 Creating a New Workspace

The **New Workspace** button at the bottom of the dialog. Enter a name and a display name. The name may only contain alphanumeric characters and hyphens (for folder-name compatibility); the display name may include non-English characters.

#### Naming Rules

| Field | Constraints | Examples |
|---|---|---|
| **Name** (folder name) | Alphanumeric characters + hyphens, no leading / trailing hyphen, 1 to 32 characters | `work`, `client-a-2025`, `test` |
| **Display name** | Any characters, including non-English text, 1 to 64 characters | `Work`, `Client A 2025 Edition`, `Production Environment` |

Because the name is **used directly as the folder name**, changing it later would break the paths to the DB and image files. In ViewGrid, a rename **changes only the display name**, and the name (folder name) is immutable.

Creating a new workspace produces an **empty workspace**. If you want to copy data from an existing one, use **Duplicate**.

### 7.21.3 Renaming

The card menu → Rename. Changes the display name (by design, the folder name is left untouched — important).

### 7.21.4 Deleting

The card menu → Delete. Rather than an immediate deletion, the workspace is moved to `workspaces\.trash\` (a safeguard against mistakes).

#### Confirmation Before Deletion

The confirmation dialog shows the workspace name (display name) and an approximate amount of data inside it. Pressing OK moves it to `.trash`. This cannot be undone, but you can manually restore it from the `.trash` folder (move the whole folder directly under `workspaces\`).

#### Deleting the Active Workspace

If you try to delete the workspace you currently have open, the operation is rejected. Switch to a different workspace first, then delete.

#### Deleting the Last Remaining One

If there is only one workspace, an attempt to delete it is rejected. At least one must remain (create a new one to make deletion possible).

### 7.21.5 Duplicating

The card menu → Duplicate. Copies the entire DB / images / thumbnails to create a new workspace.

#### Uses for Duplication

- **A safe copy of production**: take a snapshot before making large changes
- **Template deployment**: duplicate a "template" workspace that holds a full set of assets, variants, and grids, then derive a per-project copy from it
- **A prototype environment**: duplicate "production" to create an `experiment` workspace

Duplication is a synchronous operation and takes **time proportional to the amount of data** (tens of seconds to several minutes if there are many images). You cannot switch until it completes.

## 7.22 Export / Import (zip Package)

You can package a single workspace into a zip and carry it to another PC or another user.

### 7.22.1 Export

The card menu → Export → specify the destination zip. Packages `viewgrid.db` + `images/` + `thumbnails/` into a single zip.

#### Contents of the zip

```
my-workspace.zip
├── viewgrid.db           SQLite DB (grids / placements / variants / asset metadata)
├── images/<hash[0..1]>/  image files
└── thumbnails/<hash[0..1]>/  thumbnails
```

#### Notes

- The active workspace can also be exported (the DB is opened read-only, and writes are locked)
- Other editing operations are paused while the export is in progress
- It takes time proportional to the amount of data (roughly 10 to 30 seconds for 1 GB of images)

### 7.22.2 Import

The **Import from zip** button at the bottom of the dialog → select a zip. It is imported as a new workspace.

#### Workspace Name on Import

The imported workspace name is the same as the source name (folder name) in the zip. If a workspace with the same name already exists, the import fails with a **duplicate error**. Rename it before importing.

#### After Import

It is merely registered as a new workspace and does not become active automatically. Open it by switching.

### 7.22.3 Security

When extracting the zip, the following are defended against:
- **zip slip**: path traversal (entries containing `../`) is rejected
- **NTFS ADS**: alternate data stream specifications are rejected
- The write-out uses `File.Replace` and is atomic (even if a crash occurs during extraction, no partial write is left behind)

However, since the contents of the zip itself are trusted, **importing a suspicious zip is not recommended** (it cannot protect against the imported image files themselves being malicious).

## 7.23 The .trash/ Folder

Deleted workspaces are moved to `%LocalAppData%\ViewGrid\workspaces\.trash\<name>-<timestamp>\`. They can be deleted manually, but an automatic deletion feature is **not yet implemented** (planned for the future).

### 7.23.1 Timestamp Suffix

The format is `<name>-yyyyMMdd-HHmmss` (e.g., `experiment-20260513-153842`). Deleting under the same name multiple times does not overwrite earlier entries.

### 7.23.2 Restoring

You can restore a workspace by moving its folder from under `.trash` to directly under `workspaces\` (it may be renamed). After restoring, it is not automatically detected at app startup, so you need to **manually edit `workspaces.json`** to register it, or create a new workspace and overwrite its contents.

### 7.23.3 Capacity Management

`.trash` continues to occupy disk space for everything that has been used. If you want to free up disk space, manually delete what you no longer need.

## 7.24 Workspace Resolution at Startup

The workspace selection flow when ViewGrid starts up:

```
1. Read %LocalAppData%\ViewGrid\active.json
2. The specified workspace exists       → make that workspace active
   It does not exist (deleted, etc.)     → make the first entry in workspaces.json active
   workspaces.json is also empty         → create Default automatically and make it active
3. First launch from an older version:
   %LocalAppData%\ViewGrid\viewgrid.db exists  → migrate automatically to workspaces\Default\
4. Acquire the lock file → failure → show WorkspaceLockedDialog → abort startup
5. Build the DI container + display the main window
```

### 7.24.1 Cases Where Migration Runs

- An update from an older version (before the workspaces feature was introduced)
- A case where `%LocalAppData%\ViewGrid\viewgrid.db` has been placed manually

In both cases, the file is safely moved under `workspaces\Default\` (the original file is deleted). Migration runs only once; thereafter, the area under `workspaces\` is used directly.
