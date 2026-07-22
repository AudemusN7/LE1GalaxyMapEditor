# Legendary Explorer Core PCC Integration

## Objective

Replace module-side CSV and loose-texture workflows with direct LE1 PCC access through Legendary Explorer Core (LEC).

The completed editor must:

- load and save the six galaxy-map partial 2DAs in a selected seek-free PCC;
- derive module identity and mount information from the containing DLC;
- keep editor-only module metadata outside the DLC folder;
- resolve global TLK strings using Legendary Explorer's cached TLK selection;
- read and render vanilla and modded `Texture2D` exports from PCC files; and
- remove obsolete loose module textures and most bundled map textures.

Do not add a CSV-to-PCC migration workflow. Existing CSV users can import their tables with Legendary Explorer.

## LEC source and dependency

The local LEC source is:

```text
C:\Users\ryana\source\repos\AudemusN7\LegendaryExplorer\LegendaryExplorer\LegendaryExplorerCore
```

Relevant LEC types:

| Area | LEC type |
|---|---|
| Package access | `MEPackageHandler`, `IMEPackage`, `PackageSaver` |
| 2DA access | `LegendaryExplorerCore.Unreal.Classes.Bio2DA` |
| Typed cells | `Bio2DACell` |
| LE1 TLK parsing | `ME1TalkFile`, `LE1TalkFiles` |
| Texture decoding | `LegendaryExplorerCore.Unreal.Classes.Texture2D` |
| DLC metadata | `AutoloadIni`, `MELoadedDLC` |
| Locale values | `MELocalization` |

Both projects target .NET 10. Reference a pinned, tested LEC build and publish `LegendaryExplorerCore.dll`, `LegendaryExplorerCore.pdb`, and its required managed/native runtime dependencies beside the editor. Record the source commit, build version, and binary hashes in a dependency manifest. Do not copy LEC source into this repository and do not dynamically bind to an arbitrary Legendary Explorer installation.

Initialise LEC once through `LegendaryExplorerCoreLib.InitLib`. Build, test, and publish the editor as `win-x64`.

The editor should continue to consume Legendary Explorer's TLK cache because it is a data contract, not a binary dependency on the Legendary Explorer UI.

## Current code boundaries

The primary existing files affected are:

| File | Required change |
|---|---|
| `Services/CsvGalaxyMapLoader.cs` | Retain for embedded BASEGAME data initially; replace module loading with a PCC reader. |
| `Services/GalaxyMapCsvWriter.cs` | Replace module persistence with a PCC writer. |
| `Services/GalaxyMapModuleManifestStore.cs` | Replace DLC-local `module.json` storage with an AppData module profile store. |
| `Services/GalaxyMapWorkspaceStore.cs` | Remember profile identities/PCC locations rather than module folders. |
| `Services/GalaxyMapTextureService.cs` | Replace loose-file links with package/export resolution and decoded texture caching. |
| `Models/GalaxyMapModule.cs` | Store derived DLC/PCC identity and editor-owned profile data separately. Remove loose texture-link metadata. |
| `Models/CsvRowSnapshot.cs` | Generalise source snapshots so PCC cell type/value information can be retained. |
| `Application/WorkspaceWorkflowService.cs` | Open modules from a selected PCC; derive DLC metadata; implement relink/forget semantics. |
| `Application/Editing/EditSessionService.cs` | Commit dirty tables as one PCC transaction; stop writing partial CSVs and staged texture files. |
| `ViewModels/PropertyInspectorViewModel.cs` | Mark known StrRef properties and expose TLK lookup commands. |
| `Services/PlanetAppearanceSchema.cs` | Populate texture choices from package exports instead of hard-coded loose resources. |
| `Services/PlanetAppearanceCodec.cs` | Preserve and resolve full in-memory texture references. |
| `LE1GalaxyMapEditor.csproj` | Add LEC and embed the small resources that remain. Remove obsolete loose texture copying. |

Keep the existing row models, layer composition, validation, reservations, editing history, and view models wherever they are storage-independent.

## Module discovery

The user selects only the PCC containing the galaxy-map partial 2DAs. Do not ask separately for a module or DLC folder.

Given:

```text
DLC_MOD_SpectreLE1\
    AutoLoad.ini
    CookedPCConsole\
        <selected galaxy-map PCC>
```

derive and validate the module as follows:

1. Open the selected file with `MEPackageHandler.OpenLE1Package`.
2. Require an LE1 package containing at least one supported galaxy-map partial export.
3. Require the PCC parent directory to be `CookedPCConsole`.
4. Treat the parent of `CookedPCConsole` as the DLC root.
5. Require `AutoLoad.ini` in the DLC root.
6. Load it with LEC `AutoloadIni`.
7. Derive:
   - tag from the DLC directory name, e.g. `DLC_MOD_SpectreLE1`;
   - display name from `[ME1DLCMOUNT] ModName`;
   - load order from `[ME1DLCMOUNT] ModMount`;
   - CookedPCConsole path from the selected PCC's parent; and
   - galaxy-map PCC path from the selected file.

`ModName` and `ModMount` are source facts. Require a non-empty `ModName` and a valid integer `ModMount`; LEC's typed value alone cannot distinguish a malformed mount from zero. Refresh both from `AutoLoad.ini` and do not make AppData copies authoritative.

## AppData profiles and workspace state

Do not write `module.json` or any other editor file into a DLC folder.

Use the existing `%LocalAppData%\LE1GalaxyMapEditor` root:

```text
%LocalAppData%\LE1GalaxyMapEditor\
    workspace.json
    modules\
        <stable-profile-id>.json
```

A module profile contains editor-owned state only:

```json
{
  "schemaVersion": 1,
  "dlcTag": "DLC_MOD_SpectreLE1",
  "lastKnownDlcPath": "C:/.../DLC_MOD_SpectreLE1",
  "galaxyMapPackage": "CookedPCConsole/BIOG_Spectre_GalaxyMap.pcc",
  "moduleColor": "Purple",
  "tlkLocale": "INT",
  "resourcePackages": [
    "CookedPCConsole/BIOA_NOR10_03_SEM_GM_LAY.pcc"
  ],
  "reservations": {
    "cluster": { "start": 100, "end": 199 },
    "system": { "start": 1000, "end": 1999 },
    "planet": { "start": 10000, "end": 19999 },
    "map": { "start": 1000, "end": 1999 },
    "relay": { "start": 1000, "end": 1999 }
  }
}
```

Use `DLC tag + galaxy-map PCC relative path` as the logical profile identity. A hash of that identity is suitable for the profile filename. Retain the readable identity inside the JSON.

`moduleColor` and registered resource packages are editor-owned. Store resource PCCs as DLC-relative paths, support more than one, and treat them as read-only. Remove the legacy read-only-module mode; linked galaxy-map PCCs are writable, but only the active module is an editing target.

If a mod is moved or reinstalled, selecting its PCC again should match the existing identity and restore reservations and locale. Report collisions instead of merging two installations silently.

Module lifecycle:

| Operation | Workspace | Profile | DLC files |
|---|---|---|---|
| Unlink | Remove | Retain | Untouched |
| Relink | Add | Reuse | Untouched |
| Forget | Remove | Delete | Untouched |

`workspace.json` should reference profile identities and the active module. A known-modules UI can relink profiles whose PCC still exists; Browse PCC handles moved modules.

Do not migrate legacy folder-based workspace entries or DLC-local `module.json` files.

## Galaxy-map 2DA loading

Supported exports:

```text
GalaxyMap_Cluster_part
GalaxyMap_System_part
GalaxyMap_Planet_part
GalaxyMap_PlotPlanet_part
GalaxyMap_Map_part
GalaxyMap_Relay_part
```

Enumerate package exports and match all of:

```text
export.ObjectName.Name == expected name     // case-insensitive
export.ClassName == "Bio2DANumberedRows"
!export.IsDefaultObject
```

Do not depend on parent export, full/instanced path, object instance number, or `UIndex`. A table may be at package root or below an export such as `BIOG_2DA_GalaxyMap_X`.

Zero matching exports means the table is absent and is valid for a partial package. More than one match is ambiguous and must block loading.

For each match:

1. Construct `Bio2DA` from the export.
2. Read numbered row IDs from `RowNames`.
3. Validate the expected canonical columns.
4. Convert each `Bio2DACell` according to its actual type: `TYPE_INT`, `TYPE_FLOAT`, `TYPE_NAME`, or `TYPE_NULL`.
5. Populate the existing `GalaxyMapLayer` and row models.
6. Retain original row order, cell types, values, and export identity in neutral source snapshots.

Maintain a canonical default PCC cell type for every supported column. Existing non-null PCC cell types remain authoritative; use the defaults only for new cells, edited nulls, and overrides derived from embedded BASEGAME CSV rows. Derive and verify this schema against the vanilla tables rather than inferring types from textual values.

`NameText` remains an ordinary editor-only 2DA value. It is unrelated to TLK resolution.

## PCC writing and commit safety

Write only rows owned by the active module layer: new rows and explicit overrides. Never flatten lower-mounted data into the module PCC.

Commit procedure:

1. Compare the PCC against the fingerprint recorded at load time. Use at least length and last-write time; SHA-256 is preferred.
2. Stop and require refresh if another application changed the PCC.
3. Reopen the source PCC and re-resolve exports by object name/class.
4. Apply all dirty table changes through `Bio2DA` and `Write2DAToExport`.
5. Preserve unknown columns and untouched typed cells.
6. Save the complete package to a temporary sibling file.
7. Reopen the temporary PCC and verify the affected exports and rows.
8. Recheck the original fingerprint to close the external-change race.
9. Atomically replace the original PCC.
10. Update that PCC's fingerprint and snapshots and clear its table changes.

All dirty tables in one PCC form one package transaction. Do not save the same PCC independently per table. A multi-module Commit processes each PCC independently: successful PCCs clear while failed PCCs remain staged. Profile and workspace files are separate atomic persistence boundaries; if one fails after a PCC replacement, retain only the failed AppData operation and report partial success. Do not maintain session-long working PCC copies.

The supplied template PCC contains all six exports with one `TYPE_INT`/`TYPE_FLOAT`/`TYPE_NAME` schema-marker row. New-module creation writes a row-empty copy to the chosen `CookedPCConsole` destination, validates it, and creates the AppData profile; the shipped template is never modified. For an existing PCC that lacks a table, block authoring that table rather than silently importing an export during commit.

## TLK lookup

Legendary Explorer stores the selected LE1 global TLKs at:

```text
%AppData%\LegendaryExplorer\LE1LoadedTLKs.JSON
```

The JSON is a list of `(BioTalkFile export UIndex, PCC path)` entries. Load each valid entry with LEC's LE1 TLK parser. Missing/malformed entries should produce diagnostics without preventing other TLKs from loading.

Build an editor-owned lookup index rather than retaining UI dependence on Legendary Explorer's static TLK Manager. Preserve LEC's reverse load-order precedence and override-TLK behaviour. Cache:

```text
locale + StrRef -> text + source PCC + source export + priority
```

Known TLK-backed properties include:

- `Cluster.Name`
- `System.Name`
- `Planet.Name`
- `Planet.Description`
- `Planet.ButtonLabel`
- `PlotPlanet.Name`

Identify these explicitly in property metadata. Do not treat every integer or every property containing `Name` as a StrRef.

Module profiles store a `MELocalization`, defaulting to `INT`. Support `INT`, `DEU`, `ESN`, `FRA`, `ITA`, `JPN`, `POL`, and `RUS`. Changing locale updates profile/presentation state but does not dirty a 2DA.

`LE1LoadedTLKs.JSON` contains Legendary Explorer's current language/gender selection. If the module locale is not present, show it as unavailable; do not silently fall back to INT. Provide a manual cache reload and optionally a debounced file watcher.

### Property inspector presentation

Add a reusable action button beside each TLK-backed numeric property. It opens an anchored popup containing a read-only multiline text box:

- wrap text;
- preserve line breaks;
- allow text selection/copying;
- size dynamically with a maximum height and vertical scrolling;
- show locale and source package/export; and
- provide clear states for null, invalid, unresolved, unavailable-locale, and unavailable-cache values.

Reuse the same control in creation/editing dialogs that expose TLK-backed fields.

## Texture package loading

Vanilla galaxy-map textures are stored in:

```text
BIOA_NOR10_03_GM_LAY.pcc
```

Relevant export groups:

```text
BIOA_GXM10_T       planet textures
BIOA_GalaxyMap_T  cluster textures, galaxy texture, stars background
```

Third-party textures are stored in seek-free packages in the module's `CookedPCConsole`, for example:

```text
BIOA_NOR10_03_SEM_GM_LAY.pcc
```

Each module profile registers the resource PCCs whose `Texture2D` exports should populate texture choices. Existing full references can still resolve their package directly. Registration avoids scanning `CookedPCConsole` and allows unused exports to appear in the picker.

Resolve a full Unreal object reference by taking its package segment and checking directly for:

```text
<CookedPCConsole>\<PackageName>.pcc
```

Do not scan every package in `CookedPCConsole`. Resolve the export by exact full path where available. Use object-name-only matching only as a unique final fallback.

Resolution order follows effective mount order:

1. highest-mounted active module containing the package/export;
2. lower-mounted active modules; and
3. vanilla package.

Locate the vanilla CookedPCConsole through LEC's LE1 game-path detection. A missing game path or package produces a recoverable texture diagnostic and does not block 2DA editing.

For each texture:

1. Open the required PCC.
2. Resolve a texture-compatible export.
3. Construct LEC `Texture2D`.
4. Select the appropriate usable mip (not necessarily the largest for thumbnails).
5. Decode/decompress it off the UI thread.
6. Convert it to a frozen WPF bitmap or renderer upload data.
7. Cache it by effective module, package/export path, mip, and package fingerprint.
8. Dispose the package.

Do not keep package handles open for the session. Invalidate affected cache entries after package changes, refresh, or mount-order changes.

The first implementation reads/selects existing texture exports only. Texture import/replacement remains a Legendary Explorer operation.

## Removal of loose texture metadata

Once package texture resolution is complete, remove the current editor-only loose-file layer:

- `ClusterTextureLinks`;
- `PlanetTextureLinks`;
- `PlanetTextureLink.RelativePath`;
- staged texture file writes;
- module `textures` folders; and
- UI for linking loose preview files.

The authoritative value becomes the in-memory package/export reference already stored in the 2DA or planet appearance columns. Populate Planet Designer texture options from `Texture2D` exports in the vanilla and active module resource packages rather than hard-coded filenames.

## Application resources

After PCC rendering reaches visual parity:

- remove bundled duplicates of vanilla cluster/planet/background textures;
- embed basegame CSVs instead of publishing `resources/data` as loose files;
- embed required meshes and other small immutable renderer assets;
- retain only branding/UI assets that cannot reasonably come from the game; and
- remove the loose `resources` directory if no remaining runtime asset requires it.

Do not delete the existing texture set until automated and visual comparisons confirm equivalent PCC decoding.

## Implementation sequence

1. Add and pin LEC; make build/publish x64-compatible.
2. Add neutral storage/source snapshot abstractions while retaining BASEGAME CSV loading.
3. Add AppData module profile storage and update `workspace.json` references.
4. Replace folder-based module opening with PCC selection and automatic DLC/AutoLoad discovery.
5. Implement `_part` Bio2DA loading and round-trip tests.
6. Implement transactional PCC writing and external-change detection.
7. Add template-PCC module creation after the template is supplied.
8. Implement retained profiles plus unlink, relink, and forget.
9. Implement TLK cache loading, locale indexes, and the property popup.
10. Implement package/reference texture resolution and LEC texture decoding.
11. Replace cluster and Planet Designer loose-texture workflows.
12. Embed remaining immutable resources and remove obsolete loose assets.
13. Update user documentation after the new workflow stabilises.

## Required tests

### Package and 2DA

- export at package root and below a parent export;
- object instance numbers ignored;
- missing and duplicate `_part` exports;
- sparse numbered rows;
- duplicate numbered row IDs rejected;
- null versus zero and all cell types;
- default typing for new cells, edited nulls, and BASEGAME-derived overrides;
- unknown columns preserved;
- new rows and lower-layer overrides only;
- physical-row and override deletion semantics;
- existing row order preserved and new rows appended;
- unchanged package round trip;
- external PCC change before commit;
- external PCC change while the candidate is being prepared;
- failed temporary save/verification without source corruption;
- multi-table commit clears state only after atomic success; and
- multi-PCC partial success clears only completed PCCs.

### Module profiles

- PCC-to-DLC path derivation;
- missing/invalid `AutoLoad.ini`;
- `ModName` and `ModMount` refresh;
- moved/reinstalled DLC profile recovery;
- identity collision;
- unlink retains reservations;
- forget deletes profile only;
- module colour and multiple registered resource PCCs round trip; and
- missing remembered PCC produces a recoverable diagnostic.

### TLK

- malformed cache and missing PCC entries;
- load-order/override precedence;
- locale filtering and unavailable-locale state;
- null, negative, missing, and valid StrRefs;
- multiline display and source attribution; and
- cache reload without application restart.

### Textures

- vanilla package/group resolution;
- mod package resolution by package-name prefix;
- registered resource-package enumeration without directory scanning;
- exact full-path export selection;
- duplicate object-name fallback rejected;
- internal/compressed mip decoding;
- lower-mip thumbnail selection;
- mount-order override;
- cache invalidation after package changes; and
- visual comparison against the current bundled assets.

## Explicit non-goals

- automatic scanning of every PCC in a DLC;
- CSV-to-PCC migration UI;
- writing editor metadata into a DLC;
- editing global TLK content;
- importing/replacing `Texture2D` exports in the first implementation;
- dynamically loading an arbitrary installed LEC version; and
- flattening composed BASEGAME/module rows into one partial package.
