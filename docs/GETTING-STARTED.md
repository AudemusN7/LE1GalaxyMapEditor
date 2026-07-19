# Getting Started

This guide takes you from the release archive to your first committed galaxy-map edit.

## Requirements

- 64-bit Windows 10 or Windows 11.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

You need the Desktop Runtime, not the larger developer SDK. The runtime is not bundled with the application.

## First Startup

On first launch, the editor loads its built-in **BASEGAME READ-ONLY** data. On later launches it also restores the module folders remembered in your workspace.

[IMAGE: First launch showing BASEGAME READ-ONLY, the module bar and the New Module and Open Module controls]

## Create an authoring module

Choose **New Module**, then complete the module details:

| Field | What to enter |
|---|---|
| **Display name** | The readable name shown in the editor. |
| **Module tag** | A unique short tag using letters, numbers, `_` or `-`.  I recommend using your DLC name `eg. DLC_MOD_GalaxyMap`.|
| **Map colour** | The colour used to identify this module's rows and values. |
| **Mount priority** | Use the same number as your DLC's mount. |
| Reserved ranges | Inclusive ID ranges available for new rows. Leave unused ranges blank. |

Planet and PlotPlanet share the same reserved range. Reserved ranges must not overlap another mounted module's ranges.

[IMAGE: New Module window showing Display name, Module tag, Map colour, Mount priority and reserved ID ranges]

Creating the module immediately creates its folder and `module.json`. Galaxy-map content is not written until you use **Commit**.

## Alternate: Import an existing module

If you already have an existing DLC mod or need to relink a module, choose **Open Module**

Select the folder that contains your exported CSV files and open it. It will (if it does not already exist) import and link your module via the `module.json` file.

## Make your first edit

1. Select a Cluster, System, planet or system object in the **HIERARCHY** or map view.
2. Change a value in **PROPERTIES**.
3. If the row comes from BASEGAME or another read-only source, choose the writable module that should receive the override.
4. Review the uncommitted-change indicator on the module bar.
5. Choose **Commit**, review the staged changes, then choose **Commit changes** to write the module files.

[IMAGE: Selected BASEGAME planet with the Choose edit module window open]

You can also begin with **Add Cluster**, **Add System** or **Add Planet/Object**. New content is created inside the active module's reserved ID ranges.
You may also right click on any Cluster, System or Planet/Object and **Clone** it. This will create an exact copy of it inside your module, and optionally all the children of a cluster/system as well.

## Before committing

Check the validation summary at the bottom of the window. Errors are red, warnings are amber and information messages use the interface accent colour.

General diagnostics do not automatically block **Commit**. Resolve reported problems before testing the module in game.

## Where settings are stored

| Item | Location |
|---|---|
| Remembered workspace | `%LocalAppData%\LE1GalaxyMapEditor\workspace.json` |
| Startup logs | `%LocalAppData%\LE1GalaxyMapEditor\Logs` |
| Personal Planet templates | `%LocalAppData%\LE1GalaxyMapEditor\PlanetTemplates` |

The latest ten startup logs are retained automatically.

## Next steps

- Learn how priorities and overrides work in [Workspace and Modules](WORKSPACE-AND-MODULES.md).
- Start authoring in the [Galaxy Map Editor](GALAXY-MAP-EDITOR.md).
- If the application does not start, see [Troubleshooting](TROUBLESHOOTING.md).
