# Troubleshooting

Most launch and editing problems come from an incomplete extraction, a missing runtime, a moved module folder or a file locked by another program.

If you spot any other issues, please let me know either with a Discord DM or by opening an Issue here on Github.

[IMAGE: Startup error message showing the saved log location]

## The application does not start

### Install .NET 10

LE1 Galaxy Map Editor requires 64-bit Windows 10/11 and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Install the x64 Desktop Runtime, then launch `LE1GalaxyMapEditor.exe` again. The developer SDK is not required.

### Extract the complete release

Do not move the executable out of the extracted folder by itself. It needs the adjacent DLLs and `resources` folder.

If the error mentions missing BASEGAME data or `resources\data`, extract a fresh copy of the complete release archive.

[IMAGE: Correct extracted release layout showing the executable, rendering DLLs and resources folder]

## Find the startup log

Startup logs are stored in:

`%LocalAppData%\LE1GalaxyMapEditor\Logs`

The latest ten logs are retained. If startup fails, the error window normally includes the path of the saved log.

Include the newest log when reporting a repeatable startup problem.

## A remembered module is missing

The application remembers mounted folders in:

`%LocalAppData%\LE1GalaxyMapEditor\workspace.json`

If a module has moved, BASEGAME remains available and the editor reports the missing path.

Try the following:

1. Restore the module to its previous folder, or use **Open Module** to mount its new location.
2. Choose **Refresh** after the folder is available.
3. If a stale entry continues to return, close the application and rename `workspace.json` to `workspace.backup.json`.
4. Relaunch the editor and reopen the required modules.

Renaming/deleting the workspace file resets remembered mounts; it does not delete module files.

[IMAGE: Missing remembered-module error while BASEGAME remains loaded]

## I cannot edit a row

Check the module bar:

- You need at least one writable module.
- New rows require an active writable module.
- Clicking a module chip only opens its settings; choose **Set as Active** there.
- BASEGAME and read-only source rows are changed through an override, not edited directly.

Use **New Module** to create an authoring module. If a **Choose edit module** window appears, select the writable destination for the override.

## A module opens as read-only

A folder containing `_part` CSVs but no `module.json` is intentionally mounted as a read-only source.

Create a writable module and place overrides there. Do not add a hand-written manifest unless you fully understand the module's intended identity, priority and reserved ranges.

## A module will not mount

Check that:

- the selected folder still exists;
- `module.json` is valid and belongs to this module;
- the module tag uses only letters, numbers, `_` or `-`;
- mount priority is zero or greater;
- reserved range starts and ends are both supplied or both blank;
- reserved ranges do not overlap another mounted module;
- supported CSV names end in `_part.csv`.

A module may contain any subset of the six supported Cluster, Relay, System, Planet, PlotPlanet and Map tables.

## The wrong row is effective

Open the module settings and check **Mount priority**. The highest-priority version of the same table and Row ID becomes effective.

Set module priority to match the DLC mount number and keep priorities unique. Use the module-instance tabs in **PROPERTIES** to compare every module version of the selected row.

After changing files outside the application, choose **Refresh**.

## Commit fails

Common causes include:

- a CSV, `module.json` or texture file is open in another program;
- the module folder is read-only;
- the folder is unavailable or has moved;
- security software is temporarily scanning or locking a new file;
- a changed Planet Shader is blank or duplicates another planet-row version.

Close other programs using the folder, correct the reported issue and choose **Commit** again.

The editor protects each file update separately. If some files were saved before the failure, only the remaining unsaved changes stay on the module bar for retrying.

[IMAGE: Commit error banner with the remaining module changes still available]

## Planet preview is unavailable

Planet Designer first attempts hardware rendering and then software rendering.

If both fail:

1. Update the graphics driver.
2. Confirm that the complete release, including rendering DLLs and Planet Designer resources, was extracted.
3. Restart the application.
4. Continue editing parameters if necessary; the Designer remains usable when its preview is unavailable.

## The preview shows fallback textures

Planet Designer renders its bundled texture library. Unknown custom texture references are preserved but use a fallback image in the preview.

Open the **fallback textures** detail to identify them. Verify those custom assets in LE1 after package integration.

## Refresh or Discard did not reload

When a required mounted module cannot be reloaded, the editor keeps the current in-memory workspace so that your uncommitted work is not silently lost.

Restore the missing folder or correct the module error, then try **Refresh** or **Discard** again.

## Unlink did not delete the module

This is expected. **Unlink Module** removes the folder from the current workspace but never deletes files from disk.

Delete or archive the folder separately only after checking that it is no longer needed.

## The module does not appear in game

LE1 Galaxy Map Editor is only designed to read/write galaxy-map 2DA data; it does not install the module into a game package.

Use Legendary Explorer's package editor and your normal ME3Tweaks Mod Manager workflow to integrate the committed CSVs, textures and any required TLK content.

See [Known Limitations](KNOWN-LIMITATIONS.md) for the full boundary of the editor's responsibilities.
