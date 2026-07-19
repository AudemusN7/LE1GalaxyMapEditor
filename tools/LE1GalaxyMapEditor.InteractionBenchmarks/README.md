# Editor interaction observational benchmark

Run from the repository root:

```powershell
dotnet run --project tools/LE1GalaxyMapEditor.InteractionBenchmarks -c Release
```

The tool measures steady scalar-edit latency and allocation, composition and
session-revision counts, hierarchy and inspector notification fan-out, structural
refresh work, merged Planet-table projection, table view-model identity, and
deferred-validation coalescing.

Timings and allocations are observational rather than pass/fail limits. Stable
semantic and call-count boundaries are protected by the Phase 0 tests in
`tests/LE1GalaxyMapEditor.Tests`.
