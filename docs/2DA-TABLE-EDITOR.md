# 2DA Table Editor

The 2DA Table Editor provides a spreadsheet-style view of the galaxy-map data currently mounted in the workspace.

![2DA Table Editor GUI, showcasing colour-coded modules, cell selection and invalid value](docs/images/2da-editor.png)

## Open the table view

Choose **View 2DA Tables** at the bottom of the main window. The central editor changes to the table view; it does not open a separate window.

Choose **View Galaxy Map** to return.

## Available tables

The tabs appear in this order:

1. **Cluster**
2. **Relay**
3. **System**
4. **Planet**
5. **PlotPlanet**
6. **Map**

Each tab shows the effective merged table and is headed `GalaxyMap_<table>`.  This mirrors what the installation will look like when installing all modules using **ME3Tweaks Mod Manager's** `2DA Merge` feature.

Rows are sorted by Row ID. The first column stays visible while you scroll horizontally. 

Tip: Hold **Shift** while using the mouse wheel to scroll horizontally.

## Understand cell sources

The displayed value comes from the highest-priority module version of that row.

| Appearance | Meaning |
|---|---|
| Coloured outline | The module supplying the effective value. |
| Filled cell | The value has an uncommitted edit. |
| White outline | Your currently selected cell. |
| Muted cell | Read-only or managed by a dedicated workflow. |
| Dark red cell | The value is invalid. |

The legend summarises this as **filled = uncommitted** and **outline = effective module**.

Hover over a cell to see its source module, how many module versions of the row exist and whether its value differs from a lower source.

![2DA colour-coded rows](docs/images/2da-row.png)
![2DA cell hover tooltip](docs/images/2da-tooltip.png)

## Edit a cell

1. Double-click a cell or press **F2**.
2. Enter the new value.
3. Press **Enter** or **Tab** to apply it.
4. Press **Esc** to cancel the current cell edit.

The footer displays the same editing instructions when the current workspace is writable. With no writable module it displays **Read-only preview** guidance instead.

## Where edits are written

- A row already owned by a writable module continues to edit in that module.
- An inherited row is written to the active module when one is available.
- If there is no active writable target, the editor opens **Choose edit module**.

Editing one cell creates a complete same-ID override row. The cell you changed receives the uncommitted fill; the rest of that row retains the effective values.

## Accepted values

| Column type | Rule |
|---|---|
| Whole number | Enter digits without a decimal part. |
| Decimal number | Must be finite; map coordinates use no more than two decimal places. |
| Optional value | Leave blank when the field supports no value. |
| Packed colour | Enter the signed decimal value expected by the table. |
| Text/token | Enter the literal 2DA token. |

Invalid input stays in edit mode. The cell receives a red border and tooltip explaining the problem.

![Invalid 2DA cell with dark red styling and its validation tooltiptooltip](docs/images/2da-error.png)

## Managed and read-only columns

**Row ID** is read-only in every table. Planet **ActiveWorld** is also managed by the editor rather than edited directly in the grid, as it is generated based on its System/Cluster IDs.

Use the Galaxy Map workflows to create, move or delete rows. The table view does not add/delete rows or manufacture additional columns from malformed source files.

Relationships updated by managed Galaxy Map workflows are reflected in the table immediately.

## Selection and layout

- Drag across cells to select a rectangular range.
- Drag column edges to resize columns.
- Use the horizontal and vertical scrollbars for large tables (like Planet
- Row and column reordering and column sorting are disabled.

The 2DA view shares the main Undo, Redo, Discard and Commit controls. There are no separate table changes to save since it is all representative of the same data.

## See also

- [Workspace and Modules](WORKSPACE-AND-MODULES.md)
- [Shortcuts and Gestures](SHORTCUTS-AND-GESTURES.md)
- [Validation and Errors](VALIDATION-AND-ERRORS.md)
