# Performance notes

## Measured core-pipeline baseline

A Release-build microbenchmark now lives in
`tools/LE1GalaxyMapEditor.CoreBenchmarks`. The initial baseline used the 422-row
BASEGAME dataset plus eight synthetic mounted modules which each override roughly
one quarter of every table. Thirty warmed iterations produced these medians on
the development machine:

| Operation | Median | P95 | Allocation per operation |
| --- | ---: | ---: | ---: |
| Load the built-in layer | 27.77 ms | 36.04 ms | 35,072 KiB |
| Compose BASEGAME plus eight modules | 4.09 ms | 6.84 ms | 3,904 KiB |
| Rebuild effective relationships | 0.08 ms | 0.10 ms | 68 KiB |
| Validate the effective document | 0.79 ms | 1.16 ms | 829 KiB |
| Validate the complete workspace | 2.14 ms | 2.54 ms | 1,654 KiB |

These figures show that the isolated data pipeline does not account for several
seconds of interactive delay. Repeating the pipeline during hierarchy and
inspector reconstruction, full-workspace undo snapshots, WPF layout, synchronous
startup, and first-use asset decoding are higher-priority targets. Composition
and validation should not be substantially redesigned without a new measurement
which demonstrates a real bottleneck at a larger module scale.

The benchmark is deliberately a diagnostic executable rather than a test. It
never creates or displays the WPF application window. Run it independently with:

```powershell
dotnet run --project tools\LE1GalaxyMapEditor.CoreBenchmarks\LE1GalaxyMapEditor.CoreBenchmarks.csproj -c Release
```

After sharing immutable CSV snapshot storage across row clones and sharing
per-table parser metadata, a second run produced the following medians:

| Operation | Median | Allocation per operation | Allocation change |
| --- | ---: | ---: | ---: |
| Load the built-in layer | 27.55 ms | 32,218 KiB | -8.1% |
| Compose BASEGAME plus eight modules | 3.85 ms | 3,370 KiB | -13.7% |

Elapsed time is effectively unchanged; this reinforces that neither operation
is the source of a multi-second pause, but each refresh now produces less garbage
for the runtime to collect. Original CSV values remain immutable and shared;
each row clone still owns an independent dirty-column set.

Undo-state cloning was measured separately after the snapshot-sharing change:

| Writable module stack | Physical rows copied | Median | P95 | Allocation/history entry |
| --- | ---: | ---: | ---: | ---: |
| One SEM-sized synthetic module | 106 | 0.80 ms | 1.04 ms | 777 KiB |
| Eight-module stress stack | 844 | 7.05 ms | 8.17 ms | 6,235 KiB |

The clone is not a major source of immediate input latency for an ordinary
module. It did reveal a long-session retention risk: one hundred entries would
be roughly 76 MiB for the smaller case and 609 MiB for the stress case. History
is now bounded to 50 entries and approximately 64 MiB per undo/redo stack. This
provides a predictable ceiling without replacing the proven snapshot semantics.

## Resource packaging result

BASEGAME CSVs and map backgrounds are deployed in `resources/data` and
`resources/textures` beside the application rather than embedded in the WPF
assembly. With the converted background images, the Release output measured:

- published application DLL: 1,289,216 bytes;
- 22 deployed texture files: 20,877,612 bytes (19.91 MiB);
- six deployed BASEGAME CSV files: 178,875 bytes (approximately 175 KiB).

Besides making those assets replaceable and independently inspectable, this
substantially reduces the binary which Windows and antivirus software must scan
before managed startup begins.

## Improvements applied in this pass

- Startup proceeds directly into the main window. The temporary loading window
  was removed after traces proved that the long blank delay occurred before
  managed application code, while the editor's own startup completed quickly.
- Startup stages are recorded after the editor becomes usable under
  `%LOCALAPPDATA%\LE1GalaxyMapEditor\Logs`. The trace includes process age, which
  helps distinguish application work from CLR, filesystem and antivirus delay
  before `App` is entered.
- BASEGAME CSVs and large backgrounds are deployed beside the executable rather
  than embedded in the managed assembly.
- Immutable CSV header/original-token storage is shared across row snapshots.
  Dirty-column sets remain independent, reducing loader allocation by 8.1% and
  composition allocation by 13.7% in the synthetic benchmark.
- Ordinary scalar/coordinate edits can retarget the existing hierarchy instead
  of destroying every tree node. Full rebuilding remains available for structural
  changes.
- Whole-workspace validation is deferred by 250 ms while ordinary edits are in
  progress, preventing every keystroke from immediately rebuilding diagnostics.
- Undo and redo history are bounded by entry count and estimated retained size.
- Changing the active authoring module no longer recomposes the galaxy. Active-module selection does not affect mount priority, so that work was redundant.
- Remembered modules are loaded as a batch and the effective document is composed once after the complete load-order stack is available. Previously every mounted module triggered another full composition.
- Several edit paths which had already recomposed their workspace no longer immediately repeat the same composition during UI refresh.

These changes preserve CSV and mounted-layer semantics. Startup model work is
completed before the main window binds to it; interactive mutations remain owned
by the WPF dispatcher.

## Observed startup traces

The pre-exclusion trace did not enter the application's trace constructor until
process age 33.17 seconds. Once managed startup began, the editor reached its
first usable dispatcher idle in another 1.56 seconds. This first trace was
captured through the test executable, but the location of the delay is still
decisive: it happened before `App` existed and therefore could not be caused by
CSV parsing, WPF layout or texture decoding.

After the workspace was excluded from Avast scanning, the actual editor entered
the trace constructor at process age 43.6 ms and became usable at process age
1.32 seconds. Within that clean launch, the loading window itself occupied about
the first 0.55 seconds; BASEGAME loading, galaxy-image decoding and remembered
module restoration together used roughly 0.21 seconds. The loading window was
therefore removed rather than retained as overhead for an external delay which
no longer occurs.

## Main remaining causes of sluggishness

The headless measurements rule out raw CSV loading, composition, relationship
linking and validation as individual multi-second operations at the tested scale.
The next real launch will produce the evidence needed to divide the remaining
time between pre-application process startup, module restoration, workspace
preparation, main-window/XAML construction, first WPF layout and image
decoding.

Structural edits still require document recomposition and hierarchy replacement.
The data portion is measured in milliseconds, but recreating and laying out a
large WPF visual tree may still be visible. Initial texture decoding also has a
real cost, although it happens during the measured startup path and decoded
images use a bounded strong LRU cache instead of weak references.

There is one correctness caveat rather than a performance bottleneck: every CSV
replacement is individually atomic, but a commit spanning several tables is not
one filesystem transaction. Dependency-safe write order reduces the chance of a
broken reference if the process stops midway, but a disk or permissions failure
can still leave some requested tables committed and later tables pending.

## Recommended optimisation pass

The next decisions should be driven by cold and warm startup traces from the
actual machine:

1. Compare process age at `App` entry with the trace clock. A large gap before
   managed code points to antivirus, runtime or cold filesystem work.
2. If `MainWindow` construction/first render dominates, profile WPF layout,
   templates, bindings and hierarchy virtualization rather than changing CSV
   services.
3. If remembered-module preparation dominates only with a substantially larger
   stack, rerun the core benchmark at that row count before designing incremental
   composition or cached validation indexes.
4. Consider a staged multi-file commit journal/rollback mechanism before direct
   package integration raises the cost of partial commits.
5. Revisit command/delta undo only if the bounded snapshot history proves too
   restrictive in real editing sessions.

An incremental composer was deliberately not introduced in this pass. The
current eight-module composition median is below 5 ms; changing override identity
and relationship semantics to improve that figure would add considerably more
correctness risk than measurable benefit.
