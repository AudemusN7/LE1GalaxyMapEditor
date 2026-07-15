# MainViewModel workflow-service refactoring report

## Executive summary

`MainViewModel` is currently 4,199 lines and owns almost every application-level decision: workspace loading, module lifecycle, staged edits, commit/discard, undo/redo, row creation and deletion, relationship cascades, Relay editing, texture staging, validation, hierarchy construction, navigation, inspector coordination, dialogs, and shell status.

The class should be reduced to a presentation coordinator rather than divided into arbitrary partial classes. The safest target is:

- one explicit editor session holding the current workspace and staged-change state;
- a small set of cohesive workflow services which operate on that session and return UI-neutral results;
- a hierarchy/navigation coordinator which remains in the presentation layer;
- a WPF dialog adapter so application workflows do not depend on `Application.Current`, windows, message boxes, file pickers, or the dispatcher;
- `MainViewModel` retaining observable shell properties, command wiring, and projection of workflow results into the UI.

This should be an incremental extraction. Existing behaviour is unusually interconnected and well covered by the current harness; a big-bang rewrite would discard that advantage.

The planned CSV table viewer, Legendary Explorer package/TLK integration, and Planet Designer reinforce this direction, but they require four additions to the original proposal: a query/read-model layer, precise change-impact notifications, explicit external-integration ports, and coalesced preview edit sessions. These should be designed into the refactor now rather than retrofitted after the first roadmap feature.

## Current diagnosis

The problem is not merely the line count. `MainViewModel` has multiple kinds of state with different lifetimes and invariants:

| State currently in `MainViewModel` | Actual owner it implies |
| --- | --- |
| `_workspace`, `_document`, remembered/missing modules | Workspace lifecycle |
| `_dirtyTables`, `_dirtyModuleMetadata`, `_pendingTextures` | Staged edit session / change set |
| `_undoStates`, `_redoStates`, `_editSnapshotCaptured` | Edit history |
| `_nodes`, `_nodesByKey`, `_galaxyRoot`, `_selectedNode` | Hierarchy presentation |
| `_currentCluster`, `_currentSystem`, `_currentViewModel`, row-instance selection | Navigation/selection presentation |
| `_pendingRelaySource`, `_pendingRelayReplacement`, `_pendingRelayTargetModule` | Relay workflow |
| `_coordinateDrag` and shift/grid flags | Map interaction workflow and UI state |
| `_startupDiagnostics`, `_validationTimer`, deferred status | Validation coordination |
| `_isApplying`, `ErrorMessage`, `StatusMessage` | Cross-workflow orchestration |

There are also direct WPF dependencies throughout the class: module, Planet, clone, destination, move, and texture dialogs; folder/file pickers; `MessageBox`; `Application.Current`; and `DispatcherTimer`. These make workflow logic difficult to instantiate without a UI even though the underlying operations are not presentation concerns.

The constructor wiring for `PropertyInspectorViewModel` is another warning sign. It receives a large collection of delegates back into `MainViewModel`, so the inspector is nominally separate while its behaviour remains centralized in the parent.

The most important existing seam is `ExecuteLayerMutation`. It already expresses a transaction-like policy:

1. require a writable layer;
2. capture undo state;
3. back up affected rows;
4. apply a mutation;
5. mark tables dirty;
6. recompose and refresh;
7. restore on an expected failure.

That policy should become the core of an edit-session service rather than be reimplemented by every extracted workflow.

## Roadmap impact review

The proposed feature order remains sensible. Each feature can validate one architectural boundary before the next adds more risk:

1. the CSV viewer exercises read models and session change notifications without changing package files;
2. Legendary Explorer integration exercises external adapters, long-running workflows, and import/export safety;
3. the Planet Designer builds on the established edit-session boundary while adding a specialized codec and renderer.

### Merged CSV table viewer

The table viewer should be a query projection over `EditorSession`, not another owner of CSV data. It must read the same mounted layers, effective document, dirty state, and schemas used by the map and inspector. Re-parsing module CSVs for the grid would create a second source of truth and make live preview unreliable after staged edits.

Add a `TableProjectionService` under an application `Queries` namespace. It should produce immutable, UI-neutral snapshots:

```csharp
public sealed record MergedTableSnapshot(
    GalaxyMapTable Table,
    IReadOnlyList<TableColumn> Columns,
    IReadOnlyList<MergedTableRow> Rows,
    long SessionRevision);

public sealed record MergedTableCell(
    string DisplayValue,
    object? RawValue,
    string EffectiveModuleTag,
    ModuleColor EffectiveModuleColor,
    bool IsStaged,
    IReadOnlyList<CellInstanceValue> OverrideChain);
```

Rows should be sorted by true row ID and columns should come from the canonical schema plus the union of retained unknown columns. The projection must preserve raw values alongside formatted values; otherwise the viewer will accidentally become another formatting/parsing layer.

Cell colouring needs a documented meaning. Because module overrides are complete physical rows, the technically correct effective provenance of every cell is the winning row's module. For comparison value, the UI can additionally distinguish cells whose value differs from the next lower physical instance. That is a derived comparison flag, not a claim that the cell was independently overlaid.

The grid should use row/column virtualization and stable `GalaxyMapRowKey` selection. Selection can synchronize with the hierarchy/inspector through the presentation coordinator without putting DataGrid concerns into workflow services. Initially keep it read-only; if direct grid editing is later added, route edits through `InspectorEditWorkflow`/`EditSessionService` rather than mutating projection cells.

To support efficient live updates, workflow results should report changed tables and row keys. A single opaque full/structural refresh flag is too coarse for rebuilding a large grid on every scalar edit.

### Legendary Explorer package and TLK integration

Legendary Explorer integration should sit behind explicit ports even if its source is C# and can be referenced directly. Shared language reduces interop friction; it does not remove versioning, package-write safety, dependency, threading, licensing, or API-churn concerns.

Recommended ports and workflows:

- `ILegendaryExplorerGateway` for opening packages, extracting/exporting 2DA data, and writing package outputs;
- `ITlkGateway` for reading, resolving, allocating, and writing TLK strings;
- `PackageImportWorkflow` to convert selected package exports into a module/layer and stage or mount it;
- `PackageExportWorkflow` to serialize a chosen committed module into an explicit package output;
- `TlkSyncWorkflow` to synchronize numeric TLK IDs while keeping those raw IDs authoritative in the galaxy-map rows.

LEX-specific package, export, and TLK types should stop at the infrastructure adapter. Application workflows should receive project-owned request/result records so a LEX update does not ripple through ViewModels and domain models.

Package import/export must not be folded into the ordinary `Commit` button by default. CSV/module commit and package mutation have different failure and recovery boundaries. Export should be an explicit workflow with progress, cancellation, preflight validation, backups, and a safe output path; overwriting the source game/package should require a separate deliberate choice. Imported rows should enter the same `EditorSession` and transaction machinery as manually created rows so provenance, validation, undo, and the table viewer remain consistent.

TLK resolution is a read/query concern; TLK allocation and synchronization are write workflows. UI surfaces may show resolved text beside an ID, but must not replace the raw integer field with display text. This distinction will also help the table viewer show both compact raw data and readable values.

These operations may be slow enough to require asynchronous APIs. Application workflows should accept cancellation and progress abstractions while WPF dispatching remains in presentation/infrastructure code.

### Planet Designer and live shader preview

The Planet Designer requires three deliberately separate components:

1. `PlanetAppearanceCodec` in the domain layer to decode CSV appearance cells into typed parameters and encode them back with exact round-trip behaviour;
2. `PlanetDesignerWorkflow` in the application layer to manage a draft, validate it, and commit changes through `EditSessionService`;
3. `IPlanetPreviewRenderer` plus a concrete rendering implementation to own shader assets, GPU resources, camera state, and frame rendering.

The renderer must not write galaxy-map rows and the codec must not depend on WPF or a graphics API. This allows reverse-engineering and round-trip tests to progress before the shader reconstruction is visually complete.

Live material sliders also change the edit-session requirements. Recomposition, validation, and a full undo snapshot on every pointer movement would be wasteful and make Undo unusable. Add a coalesced edit scope:

```csharp
public interface IEditScope : IDisposable
{
    WorkflowResult Preview(IReadOnlyList<FieldChange> changes);
    WorkflowResult Commit();
    void Cancel();
}
```

The designer can update a detached draft and renderer continuously, then stage one atomic row mutation and one undo entry when the user commits or ends an interaction. Cancel restores the original raw values. The table viewer should reflect the staged committed value; showing transient designer drafts elsewhere should be an explicit presentation choice, not leakage into the workspace.

Appearance reverse-engineering should be captured as versioned parameter metadata with confidence/unknown states. Unknown bits and original tokens must survive a no-op decode/encode cycle. Golden parameter vectors and, later, reference image comparisons will be more valuable than binding-heavy UI tests.

### Required architectural adjustments

Before extraction begins, amend the target design in four ways:

- give `EditorSession` a monotonically increasing revision and a single change notification carrying affected tables/row keys;
- establish a query/read-model side alongside mutation workflows;
- define LEX/TLK ports and long-running operation contracts without taking a hard dependency in presentation code;
- make edit transactions support begin/preview/commit/cancel and coalesced undo, even if the first implementation only uses immediate mutations.

## Design principles

### Keep the workspace as the aggregate

`GalaxyMapWorkspace`, its layers, and its effective document already provide the meaningful domain boundary. Workflow services should operate on an editor session containing that workspace; they should not introduce repositories around in-memory collections or copy row-management logic into separate stores.

### One owner for staged-change policy

Only one component should own dirty tables, dirty metadata, pending file payloads, history snapshots, commit, rollback, and target-module resolution. Row, Relay, Planet, and texture workflows should request mutations through it.

If each service gets its own `MarkDirty`, `Recompose`, `Validate`, and exception-recovery sequence, the refactor will reproduce the current coupling across several files and become harder to reason about.

### Workflow services return facts, not ViewModels

Application services should not create `GalaxyViewModel`, `ClusterViewModel`, `SystemViewModel`, `HierarchyNodeViewModel`, brushes, windows, or commands. They should return a small result such as:

```csharp
public sealed record WorkflowResult(
    bool Succeeded,
    string Message,
    GalaxyMapRowKey? SelectionKey = null,
    NavigationTarget? Navigation = null,
    ChangeImpact? Impact = null,
    string? Error = null);

public sealed record ChangeImpact(
    IReadOnlySet<GalaxyMapTable> Tables,
    IReadOnlySet<GalaxyMapRowKey> Rows,
    bool IsStructural);

public readonly record struct NavigationTarget(int? ClusterRowId, int? SystemRowId)
{
    public static NavigationTarget Galaxy => new(null, null);
}
```

`MainViewModel` can then apply selection, navigation, validation scheduling, status, and error presentation in one place.

### Prefer cohesive services over one service per verb

The target is not dozens of 30-line classes. A service is justified when it owns a state machine, a consistency boundary, or a related family of use cases. Pure helpers such as label parsing and CSV-column mapping can remain static domain helpers.

### Keep UI navigation in the presentation layer

Hierarchy node identity, WPF selection, breadcrumbs, inspector tabs, and the current canvas ViewModel are presentation state. Extract them from `MainViewModel`, but do not disguise them as domain services. A `HierarchyNavigationCoordinator` or child ViewModel is a better fit.

## Recommended target structure

The names are illustrative; consistency matters more than the suffix chosen.

```text
Application/
  EditorSession.cs
  WorkflowResult.cs
  WorkspaceWorkflowService.cs
  Queries/
    TableProjectionService.cs
  Editing/
    EditSessionService.cs
    EditChangeSet.cs
    EditHistory.cs
    RowAuthoringWorkflow.cs
    InspectorEditWorkflow.cs
    PlanetRelationshipWorkflow.cs
    RelayWorkflow.cs
    ClusterTextureWorkflow.cs
    PlanetDesignerWorkflow.cs
  Integration/
    PackageImportWorkflow.cs
    PackageExportWorkflow.cs
    TlkSyncWorkflow.cs
  ValidationCoordinator.cs
  Ports/
    IEditorDialogs.cs
    IDeferredScheduler.cs
    ILegendaryExplorerGateway.cs
    ITlkGateway.cs

Domain/
  PlanetAppearance/
    PlanetAppearanceCodec.cs
    PlanetMaterialParameters.cs

Presentation/
  MainViewModel.cs
  HierarchyNavigationCoordinator.cs
  TableViewerViewModel.cs
  PlanetDesignerViewModel.cs
  WpfEditorDialogs.cs
  DispatcherDeferredScheduler.cs

Infrastructure/
  LegendaryExplorerGateway.cs
  TlkGateway.cs

Rendering/
  IPlanetPreviewRenderer.cs
  PlanetPreviewRenderer.cs
```

The existing `Services` directory currently contains useful data and I/O services such as the CSV loader/writer, validator, manifest store, texture service, and workspace store. Those do not need to be renamed immediately. The new `Application` or `Workflows` directory would distinguish multi-step use cases from lower-level services.

## Proposed responsibilities

### 1. `EditorSession`

Owns the current `GalaxyMapWorkspace` reference and exposes its effective document and active module. It also owns or references the session-wide `EditChangeSet` and `EditHistory`.

It should not be a general-purpose bag of callbacks or UI state. Selection, open panels, and ViewModels stay outside it.

Suggested surface:

```csharp
public sealed class EditorSession
{
    public GalaxyMapWorkspace? Workspace { get; internal set; }
    public GalaxyMapDocument? Document => Workspace?.EffectiveDocument;
    public GalaxyMapModule? ActiveModule => Workspace?.ActiveModule;
    public EditChangeSet Changes { get; }
    public EditHistory History { get; }
    public long Revision { get; private set; }
    public event EventHandler<SessionChangedEventArgs>? Changed;
}
```

### 2. `EditSessionService`

This is the central consistency boundary and should be extracted before most authoring workflows.

Own or absorb:

- `ExecuteLayerMutation`;
- `ChooseEditTarget` and `ResolveWritableTarget`, using a dialog/selection port;
- `MarkTableDirty`, `MarkTablesDirty`, `MarkMetadataDirty`, and pending-change calculations;
- `BeginUserEdit`, `EnsureUndoSnapshot`, snapshot capture/restoration, Undo, Redo, and history limits;
- begin/preview/commit/cancel edit scopes and coalesced undo for future high-frequency designer input;
- `CommitPendingChanges` and the successful portions of Discard;
- pending texture payloads as commit artefacts, even if texture preparation belongs elsewhere;
- safe failure recovery and the `_isApplying` guard.

The service should use the existing `GalaxyMapCsvWriter`, `GalaxyMapModuleManifestStore`, `AtomicFileWriter`, and workspace store. It should advance the session revision once per logical mutation and publish one `SessionChanged` notification containing the `ChangeImpact`. Prefer this single explicit notification over a broad event bus or separate change/history/validation event storms.

`EditChangeSet` should be a real type rather than three collections that every caller understands:

```csharp
public sealed class EditChangeSet
{
    public IReadOnlyDictionary<string, IReadOnlySet<GalaxyMapTable>> DirtyTables { get; }
    public IReadOnlySet<string> DirtyModuleMetadata { get; }
    public IReadOnlyCollection<PendingFileWrite> PendingFiles { get; }
    public bool HasChanges { get; }
    public int Count { get; }
}
```

This also provides a natural future home for the multi-file commit journal discussed in `PERFORMANCE.md`.

### 3. `WorkspaceWorkflowService`

Owns workspace and module lifecycle:

- `LoadBuiltIn` and reference-folder loading;
- create/open/mount module operations;
- active-module selection;
- unlinking modules;
- module metadata updates, tag migration, range validation, and reservation inference;
- remembering and restoring module stacks;
- refreshing remembered state and reporting missing/invalid modules.

It should depend on the CSV loader, manifest store, workspace store, and edit session. It should not show dialogs or directly update `MountedModules`, `ModuleBarItems`, `StatusMessage`, or validation collections.

The workflow result can report the new session, active tag, startup diagnostics, and suggested navigation target. `MainViewModel` then refreshes its observable projections.

### 4. `RowAuthoringWorkflow`

Owns structural row operations:

- Add Cluster, System, and Planet row creation;
- cloning rows and optional descendants/linked content;
- deletion of owned rows;
- moving Systems and Planets between parents;
- row ID/label allocation and validation;
- coordinate-drag mutation, unless that later proves large enough for a dedicated map-interaction class.

The new contextual add command should eventually call a neutral method such as:

```csharp
WorkflowResult AddChild(GalaxyMapRowKey? parentKey, PlanetCreationRequest? planetRequest = null);
```

The parent key, not the current ViewModel, determines the target. That preserves the new hierarchy right-click semantics without coupling authoring to navigation.

Creation dialogs should gather a request first. The workflow receives `PlanetCreationRequest`, `CloneContentRequest`, or a move request and performs no WPF work.

### 5. `InspectorEditWorkflow`

Owns semantic edits which cascade beyond one property:

- `ApplyManagedInspectorEdit`;
- Cluster/System/Planet label changes;
- parent changes;
- ActiveWorld, PlotPlanet code, and Relay endpoint recalculation;
- mirrored Planet/PlotPlanet identity and availability fields;
- availability presets;
- managed option data where it is domain-derived.

Ordinary scalar edits can also enter through a single `ApplyScalarEdit` path so `ModelOnPropertyChanged` stops containing override selection, cloning, dirty-column marking, recomposition, refresh, and error recovery.

The inspector should depend on a narrow interface rather than a list of delegates:

```csharp
public interface IInspectorWorkflow
{
    WorkflowResult ApplyEdit(GalaxyMapRowKey key, string propertyName, object? value);
    IReadOnlyList<InspectorActionDescriptor> GetActions(GalaxyMapRowKey key);
}
```

The descriptors should contain labels and neutral request identifiers; command objects can remain in the presentation layer.

### 6. `PlanetRelationshipWorkflow`

Owns the coherent family of optional Planet relationships:

- add/delete PlotPlanet rows;
- add/delete linked Map rows;
- configure landable destinations;
- maintain the linked fields and dirty columns together.

These methods currently span multiple tables and have rollback requirements, so they benefit from a dedicated service using `EditSessionService` transactions. They should not be folded into a generic row repository.

### 7. `RelayWorkflow`

Owns the Relay state machine:

- start/cancel link creation;
- start/cancel redirect;
- accept a target Cluster;
- duplicate/self-link validation;
- inherited-row override rules;
- remove/break eligibility.

Move `_pendingRelaySource`, `_pendingRelayReplacement`, and `_pendingRelayTargetModule` into this service. Expose an immutable state record so the UI can display the prompt and alter map selection behaviour.

```csharp
public sealed record RelayWorkflowState(
    GalaxyMapRowKey? Source,
    int? ReplacementRowId,
    string? TargetModuleTag)
{
    public bool IsActive => Source is not null;
}
```

This extraction is particularly valuable because Relay selection currently intercepts the general hierarchy/map selection path.

### 8. `ClusterTextureWorkflow`

Owns texture import validation, target selection, path generation, byte loading, cache keys, and preview resolution. It should add a `PendingFileWrite` to the edit change set rather than own a second commit mechanism.

The actual `OpenFileDialog` belongs in `IEditorDialogs`. `GalaxyMapTextureService` remains the lower-level resolver/decoder.

### 9. `ValidationCoordinator`

Owns validation execution, startup diagnostic merging, and the 250 ms deferred-validation policy. Replace direct `DispatcherTimer` usage with an `IDeferredScheduler`, implemented with the WPF dispatcher in production and synchronously/fake-time in tests.

It should publish a validation snapshot:

```csharp
public sealed record ValidationSnapshot(
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    int ErrorCount,
    int WarningCount);
```

`MainViewModel` projects this into `ValidationDiagnostics` and panel visibility.

### 10. `HierarchyNavigationCoordinator`

This is a presentation collaborator, not an application service. Move into it:

- building, retargeting, and disposing hierarchy nodes;
- the row/node dictionaries;
- hierarchy and map selection semantics;
- galaxy/cluster/system navigation targets;
- current row instance and inspector selection;
- capture/restore of presentation navigation context.

It can expose `CurrentViewModel`, `CurrentCluster`, `CurrentSystem`, `HierarchyRoots`, and selection-change events. It may still use `GalaxyMapTextureService` to construct the three canvas ViewModels, or receive already resolved image sources from a presentation texture adapter.

Keeping this separate prevents `MainViewModel` from remaining a hidden navigation god after business workflows are extracted.

## Dialog and platform boundary

Introduce `IEditorDialogs` early. A practical initial interface can mirror the existing requests without attempting to invent a universal dialog abstraction:

```csharp
public interface IEditorDialogs
{
    ModuleSetupResult? ConfigureNewModule(ModuleSetupDefaults defaults);
    ModuleSetupResult? ConfigureExistingModule(GalaxyMapModule module);
    string? PickModuleFolder();
    PlanetCreationRequest? CreatePlanet();
    CloneContentRequest? ConfigureClone(GalaxyMapRow source, CloneDefaults defaults);
    GalaxyMapModule? ChooseEditTarget(GalaxyMapRow row, IReadOnlyList<GalaxyMapModule> candidates);
    string? PickClusterTexture();
    LandableDestinationRequest? ConfigureLandableDestination(Planet planet);
    int? ChooseMoveDestination(GalaxyMapRow row, IReadOnlyList<MoveDestination> options);
    bool Confirm(string message);
}
```

`WpfEditorDialogs` owns window parenting and the concrete Window/file-picker classes. Tests use a scripted fake. This removes the current split where some dialogs have delegate fallbacks while others inspect `Application.Current` directly.

Manual composition in `App` is sufficient; a dependency-injection framework would add machinery without solving a current problem.

## Incremental migration plan

Each phase should land with the full harness passing. Do not move methods merely to reduce the headline line count; move their state and tests with them.

### Phase 0 — protect behaviour and add shared contracts

1. Add characterization tests for the highest-risk boundaries not already explicit: partial commit failure with pending textures, undo after module metadata/tag changes, cancellation of target-module selection, and Relay selection cancellation during navigation.
2. Introduce `WorkflowResult`, `NavigationTarget`, `ChangeImpact`, and session-change types.
3. Introduce `IEditorDialogs` and route all existing dialog calls through `WpfEditorDialogs`, without changing the workflows yet.
4. Introduce `IDeferredScheduler` around the existing validation timer.
5. Define the project-owned LEX/TLK port contracts and asynchronous operation result shape, but do not add the LEX dependency yet.

This produces immediate testability gains with very little domain movement.

### Phase 1 — create the edit-session boundary

1. Introduce `EditorSession`, `EditChangeSet`, and `EditHistory` using the existing collections and snapshot semantics.
2. Add the session revision and one change notification carrying affected tables, row keys, and structural status.
3. Move dirty tracking and pending file payloads.
4. Move `ExecuteLayerMutation` and writable-target resolution.
5. Move undo/redo, then commit/discard.
6. Provide the begin/preview/commit/cancel edit-scope contract; immediate edits can use it as a one-shot scope initially.
7. Have `MainViewModel` subscribe to session changes and update `CommitButtonText`, module dirty markers, and command states.

This is the pivotal phase. It must precede the table viewer so that the grid observes a stable session instead of `MainViewModel` internals, and it gives the later Planet Designer the correct transaction shape.

### Phase 2 — extract read models, validation, and navigation presentation

1. Introduce `TableProjectionService` and verify effective values, module provenance, unknown-column union, row-ID ordering, and incremental invalidation in headless tests. The actual grid UI can arrive in the first roadmap feature.
2. Move validation state/timing into `ValidationCoordinator` and invalidate it from `ChangeImpact`.
3. Move node maps, hierarchy lifecycle, and view navigation into `HierarchyNavigationCoordinator`.
4. Keep adapter properties/methods in `MainViewModel` temporarily so XAML bindings and commands do not all change at once.

These consumers then share the same session notification rather than independently requesting full workspace refreshes.

### Phase 3 — extract workspace lifecycle

Move load/create/open/mount/unlink/restore/metadata methods into `WorkspaceWorkflowService`. Return diagnostics, change impact, and navigation hints rather than calling UI refresh methods. Preserve the existing public methods on `MainViewModel` as thin forwards until callers/tests migrate. Keep package/TLK integration as ports at this stage; implement their adapters during the corresponding roadmap feature.

### Phase 4 — extract authoring workflows

Extract in this order:

1. `ClusterTextureWorkflow`;
2. `PlanetRelationshipWorkflow`;
3. `RelayWorkflow`;
4. `RowAuthoringWorkflow`;
5. `InspectorEditWorkflow`.

The order starts with narrower state and leaves the cascade-heavy inspector path until the edit-session API has proven itself. `PlanetDesignerWorkflow` should be implemented with the Planet Designer feature, after `PlanetAppearanceCodec` has round-trip fixtures; the refactor only needs to leave the edit-scope and renderer ports ready.

### Phase 5 — simplify presentation wiring

1. Replace the inspector's delegate bundle with `IInspectorWorkflow` plus presentation descriptors.
2. Remove temporary forwarding methods from `MainViewModel`.
3. Group command-state invalidation around session/navigation snapshots instead of manually raising every command from many code paths.
4. Move shell-only properties into small child ViewModels only where XAML sections have a clear owner; do not fragment them for cosmetic symmetry.
5. Confirm that adding `TableViewerViewModel`, package integration commands, and `PlanetDesignerViewModel` requires no new dependencies on `MainViewModel` internals.

An achievable end state is a `MainViewModel` of roughly 500–800 lines containing construction, observable shell properties, commands, status/error projection, and coordination between navigation and application workflows. Total code may not shrink dramatically; the gain is that each invariant has one owner and can be tested without constructing the entire editor.

## Testing strategy during extraction

Retain a smaller set of end-to-end `MainViewModel`/WPF tests and move detailed use-case coverage to workflow tests.

High-value workflow fixtures are already present in the harness:

- full-row override creation and source immutability;
- reserved-range creation;
- clone/delete/history;
- moving owned rows;
- managed identity cascades;
- PlotPlanet/Map persistence;
- inherited Relay redirect;
- texture staging and nebula systems;
- remembered workspace and missing modules;
- atomic write failure isolation.

For each extracted service, run these tests against the service with fake dialogs/scheduling, then retain one presentation integration test proving commands and bindings call the service correctly.

Add explicit contract tests for:

- mutation rollback leaves the change set and effective document consistent;
- one operation creates exactly one undo entry;
- failed commits clear only successfully written work, matching current retry semantics;
- service results contain stable row keys/IDs rather than model object references invalidated by recomposition;
- structural refreshes rebuild hierarchy while scalar refreshes preserve node identity;
- active-module changes do not recompose unnecessarily;
- deferred validation coalesces repeated scalar edits;
- table projections preserve raw values, order sparse row IDs correctly, union unknown columns, and report effective/changed-value provenance accurately;
- LEX adapters round-trip representative package exports without exposing LEX types above infrastructure and never modify input packages in import tests;
- TLK sync preserves numeric IDs and handles allocation/conflict cases deterministically;
- Planet appearance decode/encode is byte/token stable for no-op edits, while a designer edit produces one undo entry regardless of preview frequency.

Continue running the core benchmark after the edit-session and navigation extractions. The goal is behavioural structure, not speculative replacement of the currently fast composition pipeline.

## Traps to avoid

- **Partial classes as the final answer.** They make the file shorter without changing ownership or coupling.
- **A new god service.** `EditorWorkflowService` with all 4,000 lines merely changes the class name.
- **A mutable `EditorContext` service locator.** Dependencies and invariants become less visible, not more.
- **Services calling each other's UI callbacks.** Use returned results and a single presentation coordinator.
- **ViewModel types in workflow signatures.** Pass keys, requests, and domain models; create ViewModels in presentation code.
- **Duplicated transaction policy.** All staged mutations should pass through `EditSessionService`.
- **Generic repositories/unit-of-work abstractions.** `GalaxyMapWorkspace` and its layers already express the relevant aggregate and override semantics.
- **Replacing snapshot undo prematurely.** It is measured, bounded, and correct. Extract it first; replace it only if later evidence justifies command/delta history.
- **Event storms.** Prefer one session snapshot/change notification to numerous fine-grained cross-service events.
- **A second CSV truth in the table grid.** Project from the session; never re-parse files behind the editor's back.
- **LEX types leaking into the application/UI.** Keep them behind project-owned gateway records so upstream changes remain local.
- **Treating live preview as committed editing.** Designer drafts and renderer parameters are transient until one coalesced edit commits them.
- **Letting the renderer become a data service.** The codec owns translation, the workflow owns edits, and the renderer owns only preview resources/state.
- **Big-bang namespace and folder churn.** Move one responsibility at a time and preserve thin compatibility forwards until its callers are migrated.

## Definition of done

The refactor is meaningfully complete when:

- `MainViewModel` no longer contains CSV/manifest commit logic, workspace restoration algorithms, mutation rollback, history snapshots, Relay state, texture staging, or cascade rules;
- application workflow classes contain no `Application.Current`, Window, `MessageBox`, file-picker, brush, command, or dispatcher references;
- every staged mutation enters through one edit-session transaction boundary;
- the session publishes one revisioned change-impact stream usable by hierarchy, validation, table projections, and future designers;
- hierarchy/navigation state has one presentation owner;
- the merged table read model can be tested and consumed without constructing a WPF DataGrid;
- Legendary Explorer and TLK dependencies can be added behind ports without changing session, domain, or presentation contracts;
- high-frequency Planet Designer preview changes can be coalesced into one atomic staged edit and undo entry;
- the inspector depends on one narrow workflow contract instead of a large delegate bundle;
- workflow tests run without a WPF application;
- the full existing harness and headless WPF composition pass;
- core composition/validation benchmarks remain in the same performance class;
- `MainViewModel` is small enough that adding a feature normally changes one workflow and one presentation binding, not a 4,000-line orchestration class.

That end state would make post-release development substantially safer: feature work can be localized by use case, while the editor's unusual and important CSV override semantics remain centralized and protected.
