# Shortcuts and Gestures

This page collects the keyboard controls, mouse gestures and less obvious interactions used throughout the application.

[IMAGE: Galaxy Map Editor with a context menu, map drag coordinates, module indicators and resizable pane edges highlighted]

## Keyboard reference

### Galaxy Map Editor

| Input | Result |
|---|---|
| **Shift + left-drag** | Moves a selected map marker. |
| **Esc** during marker movement | Cancels the movement. |
| **Esc** during Relay destination selection | Cancels the Relay operation. |

### 2DA Table Editor

| Input | Result |
|---|---|
| **F2** | Edits the selected cell. |
| **Enter** | Applies the value. |
| **Tab** | Applies the value and advances. |
| **Esc** | Cancels the current cell edit. |
| **Shift + mouse wheel** | Scrolls horizontally. |

### Planet Designer

| Input | Result |
|---|---|
| **Ctrl+S** | Runs **Apply appearance**. |
| **Ctrl+Z** | Uses the shared staged Undo history. |
| **Ctrl+Y** | Uses the shared staged Redo history. |
| **Esc** | Closes the Designer, prompting when its draft has changes. |

The main editor does not provide global Ctrl+S, Ctrl+Z or Ctrl+Y shortcuts. Use the top-bar **Commit**, Undo and Redo controls.

## Map clicks

| Input | Result |
|---|---|
| Single-click an object | Selects it. |
| Double-click a Cluster | Opens the Cluster. |
| Double-click a System | Opens the System. |
| Double-click an eligible planet | Opens Planet Designer. |
| Right-click blank map space | Opens the add menu for the current map level. |

In Relay destination-selection mode, single-click a Cluster to choose it. Double-click navigation is suppressed until the Relay operation finishes or is cancelled.

## Hierarchy interactions

- Double-clicking an eligible planet opens Planet Designer.
- Right-clicking a row opens actions appropriate to its type and ownership.
- Search matches names, types, module tags, Row IDs and hierarchy paths.
- `≋` beside a row means that at least two diferent modules contain an instance of it.

## Map context menus

| Object | Commands |
|---|---|
| Cluster | **Clone Cluster…**, **Delete Cluster…** |
| System | **Clone System…**, **Move to Cluster…**, **Delete System…** |
| Planet/system object | **Open Planet Designer...** when eligible, **Clone Planet / object…**, **Move to System…**, **Delete Planet / object…** |

Relevant add commands also appear on blank map space and suitable hierarchy rows.

Read-only ownership may disable or remove actions that cannot be represented safely in a module override.

[IMAGE: Context menus for a Cluster, System and planet/system object shown side by side]

## Properties interactions

- Text fields normally apply the change when they lose focus.
- **Enter** applies a valid text value and moves to the next field.
- Invalid values keep focus and show a red border and tooltip.
- Expanders open groups of related fields.
- The pane scrolls independently when a group is longer than the window.
- Module-instance tabs switch between module versions of the selected row.

Changing the selected module version does not change module priority or the active editing module.

## Planet Designer interactions

- Click a planet to switch the Designer target. A changed draft prompts **Apply**, **Discard** or **Cancel** first.
- Double-click a personal template to use it.
- Right-click a planet or preset for **Copy Appearance** or **Paste Appearance** where available.
- Appearance copy/paste uses an internal application clipboard, not the Windows clipboard.
- Colour wheels support clicking and dragging.
- **Reset clouds** resets cloud animation, not material values.
- Deleting a personal template is immediate and has no confirmation prompt.

## Module-bar interactions

Clicking a module chip opens its settings; it does not make that module active. Choose **Set as Active** in the module window.

| Indicator | Meaning |
|---|---|
| Pen/accented chip | Active writable module. |
| Amber dot | Uncommitted changes. |
| Module colour | Source of the effective row or value. |
| Priority | Current mount priority. |

## Panes and overlays

- Drag the dividers between **HIERARCHY**, map and **PROPERTIES** to resize them.
- Planet Designer's three panes are also resizable.
- Drag **VALIDATION DIAGNOSTICS** by its header to move it.
- Drag the diagnostic panel's bottom-right corner to resize it.

Clicking a row-linked diagnostic selects the affected content. File-level diagnostics have no specific row to select.

## Colours and states

| Colour/state | Meaning |
|---|---|
| Orange map object | Current selection. |
| Module colour | Where the effective object, row or value originates. |
| Red Line | Relay connection. |
| Amber | Warning or uncommitted module indicator. |
| Red | Error, invalid field or destructive action. |
| Accent/cyan | Informational diagnostic. |
| Filled 2DA cell | Uncommitted cell value. |
| Outlined 2DA cell | Effective source module. |

Hover over controls, values and indicators for more detail. Many tooltips include field meaning, source-module information or the reason an action is unavailable.
