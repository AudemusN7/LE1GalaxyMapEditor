# LE1 Galaxy Map Editor

LE1 Galaxy Map Editor is a Windows tool for creating and editing Mass Effect Legendary Edition galaxy-map modules for LE1.
It combines a visual map editor, a merged 2DA table view and a highly accurate Planet Designer in one workspace.

[IMAGE: LE1 Galaxy Map Editor showing the Hierarchy, Galaxy view, Properties and module bar]

## Core features

- Navigate and edit the Galaxy, Cluster and System maps.
- Add, clone, move and remove Clusters, Systems, planets and system objects.
- Create and redirect Relay connections between Clusters.
- Inspect and edit the six galaxy-map 2DA tables in a merged workspace view.
- Design planet materials with a live preview that closely matches their appearance in LE1.
- Mount several DLC modules, compare their rows and create overrides without changing BASEGAME files.
- Create, open and commit galaxy-map PCCs directly through Legendary Explorer Core.
- Validate IDs, relationships, coordinates, labels, PlotPlanet/Map links and Relay topology.

## Requirements

- 64-bit Windows 10 or Windows 11.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

The full .NET runtime is not included, so install the Desktop Runtime separately and keep the extracted executable, libraries and resources together.

## Documentation Map

- [Getting Started](docs/GETTING-STARTED.md)
- [Workspace and Modules](docs/WORKSPACE-AND-MODULES.md)
- [Galaxy Map Editor](docs/GALAXY-MAP-EDITOR.md)
- [2DA Table Editor](docs/2DA-TABLE-EDITOR.md)
- [Planet Designer](docs/PLANET-DESIGNER.md)
- [Validation and Errors](docs/VALIDATION-AND-ERRORS.md)
- [Shortcuts and Gestures](docs/SHORTCUTS-AND-GESTURES.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [2DA System and Properties Overview](docs/2DA-DOCUMENTATION.md)
- [Known Limitations](docs/KNOWN-LIMITATIONS.md)

## Launching the editor

1. Extract the complete release archive to a folder.
2. Keep the executable, DLLs and `resources` folder together.
3. Run `LE1GalaxyMapEditor.exe`.

BASEGAME data loads automatically. Use **New Module** to create a galaxy-map PCC in an existing DLC, or **Open Module** to link an existing galaxy-map PCC. The PCC must be directly inside the DLC's `CookedPCConsole` folder, alongside a valid `AutoLoad.ini` in the DLC root.

## How edits work

BASEGAME module is read-only. Any edit is automatically written to your edit module as new rows or same-ID overrides.

The Galaxy Map Editor, 2DA Table Editor and Planet Designer share the same uncommitted changes and Undo/Redo history. **Apply appearance** moves a Planet Designer draft into that shared history; it does not write the change to disk.

Use **Commit** to write all uncommitted module changes directly to their galaxy-map PCCs. The active module chooses where new rows and overrides are authored; effective rows follow the DLC mount priority read from `AutoLoad.ini`.

## Limitations

- Galaxy-map 2DA exports are written directly, but the editor does not build a DLC, install a mod or edit TLK strings.
- BASEGAME rows cannot be deleted directly. This is intentional to avoid accidentally mangling your workspace.
- Inherited Relay connections can be redirected, but not broken.
- Planet Designer can resolve registered resource-PCC textures, but unresolved custom references use fallbacks and the preview camera is fixed.
- The 2DA Table Editor cannot add or delete rows.

See [Known Limitations](docs/KNOWN-LIMITATIONS.md) for more details.

# Special Thanks
- SirCxyrtyx: For shader readability/parsing
- DropTheSquid: For glTF mesh export functionality
- 55tumbl: For existing documentation/tutorials for 2DA modification
