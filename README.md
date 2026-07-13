# LE1 Galaxy Map Editor

A Windows WPF editor for the Mass Effect 1 Legendary Edition galaxy-map 2DA tables exported by Legendary Explorer.

See [UI theming](docs/UI-THEMING.md) for changing fonts, text sizes and colours, and [performance notes](docs/PERFORMANCE.md) for the current responsiveness assessment and optimisation roadmap.

## Current feature set

- Loads the six supplied vanilla tables from the deployed, read-only `resources/data` folder.
- Displays the map as Galaxy, Cluster, and System views with synchronised hierarchy, canvas, breadcrumbs, and property inspector.
- Uses the supplied opaque galaxy, Cluster, and System textures, a square normalised canvas, and an optional 40x40 reference grid.
- Renders Relay lines, orbit rings, asteroid belts, and placeholder symbols for other system objects.
- Keeps every known and unknown CSV column. The unnamed first column remains the true, potentially sparse 2DA row ID.
- Mounts additional Legendary Explorer `_part` tables above BASEGAME, including DLC-style expansions such as Spectre Expansion Mod.
- Creates writable authoring modules with a name, tag, colour, explicit mount priority, and optional separate reserved ID ranges for Cluster, System, Planet, Map, and Relay rows.
- Creates new Clusters and Systems inside those reserved ranges. The `+P` workflow offers Generic planet, Ringed planet, Asteroid belt, Hidden anomaly, and Anomaly / ship templates with verified structural defaults.
- Clones Clusters, Systems, and Planets from hierarchy or map context menus, optionally including children and linked rows.
- Stages confirmed deletion of writable-module Clusters, Systems, Planets, PlotPlanet rows, and Map rows.
- Adds PlotPlanet data, linked Map rows, and Relay connections. Any new or existing Planet can use the optional landable-destination workflow to configure its persistent level, StartPoint, Remote Event, use-button TLK, and optional mirrored PlotPlanet row together.
- Stages property, row, Relay, texture, and module-metadata edits in memory until **Commit** is pressed.
- Provides Undo, Redo, and Discard controls for staged edits.
- Groups high-value properties first and moves appearance, experimental, legacy-event, and unused fields into clearly labelled sections. Property tooltips distinguish confirmed behaviour, vanilla observations, and experimental fields.
- Uses independent checkboxes for availability Conditional/Parameter flags, offers a one-click `1 / 974 / 1` Always preset, provides labelled dropdowns for relationships and type fields, and uses a HEX/RGBA colour wheel for packed signed 32-bit values.
- Automatically maintains derived relationships when numbered Cluster/System/Planet labels or parents change: descendant ActiveWorld IDs, linked PlotPlanet codes, and Relay endpoint codes follow the edit. Planet and linked PlotPlanet identity/availability fields remain mirrored.
- Remembers mounted module locations and the active editing module, then restores the workspace on startup with visible errors for missing or invalid paths. Manifest-backed metadata is always loaded from each module's own `module.json`.
- Marks rows with multiple physical instances and provides module tabs in the property panel for comparison and lower-layer editing.
- Imports module-owned Cluster PNGs and uses the Cluster texture at 200% scale for Systems whose `ShowNebula` value is `1`.
- Shows structured, clickable validation results for IDs, ranges, ordering, relationships, coordinates, encoded labels, ActiveWorld values, PlotPlanet/Map links, and Relay topology.

## Safe module editing

BASEGAME and mounted source modules are never written to. The editor composes detached working rows from the mounted stack.

When a BASEGAME row is edited, the editor always asks which writable module should receive it—even when only one module is available. That module receives a complete row with the same table and row ID. Unchanged CSV tokens are retained rather than needlessly reformatted.

All changes remain in memory first. The top-bar **Commit** button writes every dirty module; closing with pending changes offers Commit, Discard, or Cancel. Mount priority determines the effective instance when several modules contain the same table and row ID. The active editing module does not silently jump to the top of the load order.

Each writable module lives in its own named folder containing:

- `module.json`
- `GalaxyMap_Cluster_part.csv`
- `GalaxyMap_System_part.csv`
- `GalaxyMap_Planet_part.csv`
- `GalaxyMap_PlotPlanet_part.csv`
- `GalaxyMap_Map_part.csv`
- `GalaxyMap_Relay_part.csv`

The six CSV files are optional and are created only when their table is first committed. Rows are always written in ascending numerical ID order with the canonical BASEGAME columns, UTF-8 BOM, CRLF records, and an unnamed first header. Module-owned images are stored under that module's `textures` folder and linked through `module.json`.

Each individual file update uses a flushed temporary file in the destination folder followed by an atomic replacement. A failed write therefore cannot leave a partially written CSV. A commit spanning several files is not a single cross-file transaction: if a later replacement fails, earlier files remain committed and the still-dirty module can be safely retried.

## Module workflow

1. Start the application; the deployed read-only BASEGAME tables load automatically.
2. Choose **New Module**.
3. Enter its display name, unique tag, colour, mount priority, and the non-overlapping row ranges it actually needs. Leave unused tables blank.
4. Edit BASEGAME properties, use the `+C`, `+S`, and `+P` buttons, or right-click existing map content to clone it.
5. Review the dirty marker on the module and press **Commit** when ready to update its CSVs.
6. On future starts, the remembered module stack is restored automatically.

Click a module in the top bar to change its metadata or mount priority. Use **Set as Active** in that dialog to choose which writable module receives new rows and overrides; the active module has a blue outline in the top bar. Rows supplied by more than one layer show a comparison marker in the hierarchy; their property header exposes one tab per physical module instance.

An unmanifested folder of `_part` CSVs can also be mounted read-only. The editor suggests ID ranges from rows that do not override the current stack; these may be reviewed before mounting.

BASEGAME objects are blue. New and overridden rows use their module colour. Selected objects remain orange. Relay topology lines remain red. Asteroid-belt particles retain their neutral sandstone colour while the triangular anchor uses its module colour.

## Relay behaviour

Relay endpoints use the numeric suffix of `Cluster.Label` multiplied by 10,000. They do not use the Cluster table row ID.

Connections owned entirely by the active module can be broken. Connections inherited from BASEGAME or a mounted module cannot be deleted because no verified 2DA tombstone representation is known.

Instead, choose **Redirect connection** on an inherited Relay, then click its new destination Cluster. The editor writes a Relay override with the original row ID and preserves the selected endpoint. This matches Spectre Expansion Mod's replacement of the original Local Cluster connection with its Arcturus Stream route.

## Running the application

Open `LE1GalaxyMapEditor.sln` in Visual Studio, or run:

```powershell
dotnet run --project .\src\LE1GalaxyMapEditor\LE1GalaxyMapEditor.csproj
```

The project targets .NET 10 for Windows and uses WPF with MVVM.

The latest ten startup timing traces are retained under `%LocalAppData%\LE1GalaxyMapEditor\Logs`; these distinguish pre-application/antivirus delay from CSV loading, module restoration, WPF window construction and first usable input. BASEGAME and remembered-module preparation now run directly before the main window because measured application work is well below the delay previously caused by antivirus scanning.

## Headless checks

The test harness has no external test-framework dependency:

```powershell
dotnet run --project .\tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj
```

Real vanilla exports and a DLC `_part` folder can also be checked:

```powershell
dotnet run --project .\tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj -- `
  --real "C:\Path\To\Vanilla Exports" `
  --sem "C:\Path\To\SEM"
```

Coverage includes deployed-resource integrity, source immutability, staged/committed editing, remembered and missing modules, load-order composition, row-instance comparison, manifests, full-row overrides, raw-token preservation, atomic CSV output, reserved-range creation, Planet template defaults, dependency cascades, PlotPlanet and Map persistence, Relay redirection, module textures, ShowNebula backgrounds, inspector metadata, dark-theme resources, validation, real SEM composition, navigation, and headless WPF layout.

## Deliberately deferred

- General clipboard copy/paste
- Deleting BASEGAME or read-only module rows (the partial 2DA format has no verified tombstone representation)
- TLK resolution
- Accurate shader previews
- Direct Legendary Explorer integration
- A verified deletion representation for inherited Relay rows
