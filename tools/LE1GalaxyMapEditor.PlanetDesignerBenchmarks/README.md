# Planet Designer observational benchmark

Run from the repository root:

```powershell
dotnet run --project tools/LE1GalaxyMapEditor.PlanetDesignerBenchmarks -c Release
```

The tool measures Planet Designer view-model construction, window construction,
pre-show layout, first-preview latency, renderer construction, first-frame cost,
steady-frame time and managed allocation. It also reports preview activity while
the window is hidden or minimised and the request-to-dispatch-to-frame counts for
a short preview-request burst.

Timing and allocation values are observational because they depend on the GPU,
driver, DPI, machine load and build environment. The only pass/fail gates are
hardware-independent lifecycle invariants: successful first-frame creation,
renderer disposal when the window closes, timer shutdown and no frames after
close.
