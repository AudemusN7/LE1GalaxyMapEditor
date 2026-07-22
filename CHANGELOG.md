# Changelog

## Unreleased

### PCC authoring with Legendary Explorer Core

- Replaced the loose `_part.csv` authoring workflow with direct loading and writing of LE1 galaxy-map PCC exports through Legendary Explorer Core.
- Added creation of a row-empty galaxy-map PCC inside an existing DLC's `CookedPCConsole` folder. Only tables with reserved ID ranges are included initially; Commit adds a missing supported export when that table is later used.
- Added discovery of DLC identity, display name and mount priority from the containing DLC folder and `AutoLoad.ini`.
- Added editor-owned module profiles under `%LocalAppData%\LE1GalaxyMapEditor\modules`, so the editor no longer adds `module.json` or other metadata files to the DLC.
- Added direct opening, unlinking, relinking and forgetting of PCC-backed module profiles.
- Added safe PCC replacement, temporary-package verification and detection of packages changed outside the editor.
- Added read-only resource-PCC registration for resolving custom `Texture2D` references in map and Planet Designer previews.
- Added TLK lookup from mounted DLC `GlobalTlk` PCCs and Legendary Explorer's selected LE1 TLK cache. TLK content remains read-only.
- Changed intentional same-ID rows in higher-mounted modules from collision errors to informational overrides.
- Added validation preventing reserved ranges from covering ranges or existing row IDs in lower-mounted layers.
- Allowed a System scale of zero and improved PCC numeric cell typing and round-trip fidelity.

## 1.2.1 — 19 July 2026

This release is a substantial expansion over the original `v1.0.0` release. It adds the Planet Designer, a merged 2DA editor, safer editing workflows, stronger validation and a much more complete module-management experience.

### Highlights

- Added the Planet Designer with a live LE1-style planet preview.
- Added material editing, HDR and packed-colour pickers, appearance templates, copy/paste appearance and guarded appearance randomisation.
- Added support for custom planet textures, texture linking, texture management and unlinking.
- Added a merged 2DA Table Editor for viewing and editing galaxy-map tables across mounted modules.
- Added clearer source-module indicators, cell tooltips, invalid-value feedback and staged-change highlighting in the table editor.
- Added a commit review window showing changed values, new and deleted rows, module metadata and resource files before anything is written.
- Added safer shared Undo/Redo, Discard, Refresh and shutdown handling across the Galaxy Map Editor, 2DA editor and Planet Designer.
- Added support for opening and unlinking modules while keeping workspace changes staged until they are reviewed and committed.
- Added stronger protection against partial saves and failed commits, with clearer diagnostics and retryable changes.
- Added stricter validation for IDs, relationships, coordinates, labels, ActiveWorld values, PlotPlanet/Map links and Relay connections.
- Added globally unique Cluster label selection and enforcement of the game-supported Cluster, System and Planet label ranges.
- Added improved map rendering, including coordinate grids, relay lines, system orbits, object scaling and clearer planet/object visuals.
- Added contextual creation, cloning, moving, deleting and relationship-editing workflows for galaxy-map content.

### Planet Designer

- Added a production planet preview renderer using recovered LE1 planet and corona assets.
- Added lighting, post-processing, corona, stars, cloud-speed and preview-display controls.
- Added vanilla appearance presets and personal templates.
- Added guarded randomisation that keeps related material values coherent and limits extreme results.
- Added support for copying and pasting appearances between planets.
- Added custom texture linking, category selection, availability checks and module-aware texture management.
- Added safe draft editing so changes can be applied, discarded or cancelled before entering the shared editor history.

### Galaxy Map and 2DA Editing

- Added a complete merged view of the six galaxy-map 2DA tables.
- Added full-row override creation when editing inherited or BASEGAME rows.
- Added module-aware row comparison and effective-value display.
- Added reserved-range-aware row creation and automatic label/ID suggestions.
- Added better handling for relays, landable destinations, PlotPlanet links and Map links.
- Added global Cluster label coordination, including collision checks against currently mounted modules.

### Workspace and Reliability

- Refactored the main editor into dedicated editing, workspace, validation, navigation and presentation workflows.
- Improved module manifests, remembered workspaces and missing-module diagnostics.
- Improved composition and refresh behaviour so existing selections and projections are preserved where possible.
- Improved atomic file writing and isolation of partially successful multi-module commits.
- Added clearer error, warning and information diagnostics throughout the application.
- Added performance improvements and targeted invalidation to reduce unnecessary UI refreshes.

### Documentation and Quality

- Added a complete documentation set covering setup, workspace management, map editing, 2DA editing, Planet Designer, validation, shortcuts and troubleshooting.
- Added documentation screenshots and internal notes covering UI theming and planet rendering research.
- Added extensive regression tests for data safety, lifecycle behaviour, refresh, commit failure, workspace handling and rendering-related behaviour.
- Added interaction and Planet Designer benchmark projects.

### Requirements

- Windows 10 or Windows 11, 64-bit.
- .NET 10 Desktop Runtime.
