# Known Limitations

This page describes current, verified boundaries of LE1 Galaxy Map Editor. It is not a roadmap.

## Platform and release

- The application requires 64-bit Windows 10 or Windows 11 and .NET 10.
- The complete extracted release must remain together; the executable cannot run without its adjacent resources and DLLs.
- The full .NET runtime is not bundled.

## Game-package integration

- The editor writes galaxy-map module CSVs, linked textures and module metadata.
- It does not write PCC files, build a DLC or install content into LE1.
- It does not resolve or edit TLK text.
- Use Legendary Explorer and the normal ME3Tweaks Mod Manager workflow after committing.

## BASEGAME and read-only deletion

BASEGAME and mounted read-only source rows cannot be deleted because the partial 2DA format has no verified deletion marker.

Deleting a writable override reveals the lower-priority source row instead of removing the underlying object.

Deleting module-owned Clusters and Systems can remove their module-owned descendants, but cannot remove descendants belonging to another source module.

Only a Relay owned solely by the active writable module can be broken. An BASEGAME Relay can be redirected by creating a same-ID override, but it cannot be deleted outright.

## 2DA Table Editor

- Rows cannot be added or deleted directly in the grid.
- Row IDs and managed relationship fields are read-only.
- Extra malformed columns are not manufactured as editable workspace columns. These shouldn't be possible to import at all, however.
- Structural editing must use the Galaxy Map workflows.

## Planet Designer

The preview is a highly accurate, but not 100% perfect representative of LE1's rendered planet appearance. There are very minor differences in terms of postprocessing, HDR implementation, corona etc. Treat it as an accurate approximation, but not gospel.

- The camera is fixed to the in-game planet view; there is no pan, rotation or zoom.
- Only bundled vanilla textures can be displayed directly.
- Unknown custom texture references are preserved but shown with fallback textures.
- If both hardware and software rendering fail, material parameters remain editable without a preview.
- Copy/Paste Appearance uses an internal application clipboard rather than the Windows clipboard.

## Module priority and ID collisions

Use a module priority matching the DLC mount number and keep mounted priorities unique.

The effective workspace can resolve same-ID rows by priority, but collisions between separate authoring modules are reported as validation errors. Avoid them unless the higher module is intentionally replacing the lower one and you have reviewed the result carefully.

New content must fit inside the authoring module's reserved ID ranges.

## Validation does not block every Commit

The main diagnostic list is advisory. The editor can sometimes write data that remains invalid for LE1, so review errors and warnings before packaging.

Changed Planet Shader names are a specific exception: Commit requires them to be non-empty and unique across planet-row versions.

`PlanetLevelType` values 3, 5 and 7 are known not to display correctly in LE1 and produce a warning.

## Commit boundaries

Each file uses a protected update, but a Commit covering several files or modules is not one all-or-nothing operation.

If a later write fails, earlier files may already be saved. Correct the problem and Commit the remaining unsaved changes again.

## Undo and workspace reloads

Undo/Redo history is shared across all editor surfaces and is limited in size.

History is cleared after Commit, Discard, Refresh, Unlink Module or a partially successful Commit. **Discard** and **Refresh** apply to the workspace rather than only the currently selected object.

## Linked Planet rows

Some delete operations involving linked Planet, PlotPlanet and Map rows require those rows to be owned by compatible writable modules.

When an inherited linked row cannot be removed safely, leave it in place or create a supported override rather than editing the source files manually.

## See also

- [Troubleshooting](TROUBLESHOOTING.md)
- [Validation and Errors](VALIDATION-AND-ERRORS.md)
- [Workspace and Modules](WORKSPACE-AND-MODULES.md)
