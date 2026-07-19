# Workspace and Modules

The workspace lets you view BASEGAME and several mod modules together while keeping their files separate.

[IMAGE: Module bar showing BASEGAME, mounted modules, priorities, the active-module pen and an amber uncommitted-change dot]

## Key terms

| Term | Meaning |
|---|---|
| BASEGAME | The built-in, read-only LE1 galaxy-map data. |
| Writable module | A module the editor can update. |
| Active module | The currently active module that edits will apply to by default. |
| Override | A complete row with the same table and Row ID as a lower source row. |
| Effective row | The version currently used after module priority is applied. |
| Uncommitted change | An edit held in-memory by the application but not yet written to the module files via `Commit`. |

## Priority and row overrides

Each module has a **Mount priority**. When several modules contain the same table and Row ID, the highest-priority version becomes effective.

Set each module's priority to match its DLC mount number. Use unique priorities and avoid giving two authoring modules the same Row ID unless you deliberately intend one to replace the other. Cross-module ID collisions are reported by validation.

The active module and the highest-priority module are separate concepts. Making a module active does not move it above other modules.

[IMAGE: Two module versions of one row with the module-instance tabs visible in Properties]

If two modules edit the same instance, it will display `≋` in the hierarchy. Use the module-instance tabs at the top of **PROPERTIES** to compare them.

## Module indicators

| Indicator | Meaning |
|---|---|
| **BASEGAME READ-ONLY** | Built-in game data; never written by the editor. |
| Module colour | Identifies rows and values originating from that module. |
| Pen on a module chip | The current active module. |
| Amber dot | The module has uncommitted changes. |
| `Priority [?]` | The module's current mount priority. |

Click a module chip to edit its settings. Use **Set as Active** when you want it to receive new content.

## New Module and Open Module

**New Module** creates a writable authoring module. Its folder and `module.json` are created immediately; table files appear when their first changes are committed.

**Open Module** mounts an existing folder:

- A folder with `module.json` uses its saved name, tag, priority, ranges and writable setting.
- A folder containing supported `_part` CSVs without `module.json` is mounted as a read-only source.

A module may contain only the tables it actually changes. It does not need to contain all six galaxy-map CSVs.

## Reserved ID ranges

Reserved ranges control which IDs a writable module can allocate for new content. Starts and ends are inclusive.
IDs are the row names in 2DA, written as numerical values in Column A (2DA import in Package Editor turns A into the Row Number, which is the ID).

Separate ranges are available for:

- Cluster;
- System;
- Planet / PlotPlanet;
- Map;
- Relay.

Leave both fields blank when the module will not create rows in that table. A range cannot be changed so that it excludes rows the module has already created.

If you attempt to perform an edit that requires creating rows that are marked blank in your module, it will not let you until you specify a range. This is to prevent accidental row creation.

## Choosing where an edit goes

The destination depends on the row and editor surface:

- A writable module's existing row continues to edit in that module.
- Editing a read-only row through **PROPERTIES** asks you to **Choose edit module**.
- Applying a Planet Designer appearance to a read-only row also asks for a module.
- Editing an inherited 2DA cell uses the active module when one is available; otherwise it asks you to choose.
- New Clusters, Systems, planets and system objects use the active module.

The editor creates a complete same-ID row in the chosen module. The lower source row remains unchanged.

[IMAGE: Choose edit module window showing writable module names, tags and priorities]

## Shared uncommitted changes

The Galaxy Map Editor, 2DA Table Editor and Planet Designer share edit memory.

| Control | Result |
|---|---|
| **Undo** | Reverses the most recent staged change, regardless of which editor made it. |
| **Redo** | Restores the most recently undone change. |
| **Discard** | Reloads the committed workspace and removes every uncommitted change. |
| **Commit (?)** | Reviews, then writes all uncommitted changes across all writable modules. |
| **Refresh** | Reloads BASEGAME and remembered modules; asks before discarding pending work. |

Commit's number counts groups of changed data, not rows or individual field edits.
Example: If you move the position of an object on the map, it will appear as `Commit [2]` as the X and Y column data was edited.

Choosing **Commit** opens a fixed-size review window before anything is written. It lists changed CSV fields with their committed and staged values, identifies new or deleted rows, and includes module metadata and staged resource files. New rows are kept compact, showing their internal name and tree relationship rather than every added field. Long lists scroll within the window. Choose **Commit changes** to continue or **Cancel** to leave every change staged.

[IMAGE: Undo, Redo, Discard and Commit controls with uncommitted changes present]

Undo and Redo history is shared and limited. It is cleared after Commit, Discard, Refresh or Unlink Module.

## Commit and recovery

Commit uses protected file updates so a failed write does not leave a half-written CSV. Each file is protected separately rather than treating the whole workspace as one operation.

If one file cannot be written, earlier files may already have been saved. The remaining changes stay available so you can resolve the problem and choose **Commit** again.

Closing the application with uncommitted changes offers **Commit**, **Discard** and **Cancel**.

## Refresh and Unlink Module

**Refresh** reloads the workspace modules from disk. If you have uncommitted changes, the editor asks before removing them.

**Unlink Module** removes a module from this workspace. It does not delete the module folder or its files. Any uncommitted changes for that module are discarded and the shared Undo/Redo history is cleared.

Deleting an override has a similar layering effect as unlinking a module: the lower-priority version becomes visible again rather than being deleted.

## See also

- [Getting Started](GETTING-STARTED.md)
- [2DA Table Editor](2DA-TABLE-EDITOR.md)
- [Validation and Errors](VALIDATION-AND-ERRORS.md)
- [Known Limitations](KNOWN-LIMITATIONS.md)
