# Testing

The test project uses MSTest categories so routine changes do not run PCC, filesystem and WPF coverage unnecessarily.

## Routine development

Run the fast behavioural suite:

```powershell
dotnet test tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj --filter TestCategory=Fast
```

Choose the narrow category relevant to a larger change:

```powershell
dotnet test tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj --filter TestCategory=Integration
dotnet test tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj --filter TestCategory=Pcc
dotnet test tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj --filter TestCategory=Wpf
```

Run every category only for release candidates or broad changes:

```powershell
dotnet test tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj
```

`Fast` tests should cover pure domain rules, parsing, defaults and short in-memory workflows. Use `Integration` for filesystem or complete editor-session behaviour, `Pcc` for Legendary Explorer package I/O, and `Wpf` only for application/view composition. Decorative styling is not a regression-test contract.

New tests should protect a demonstrated failure mode or a stable user-facing/data-safety contract. Avoid assertions that merely repeat the current implementation, exact cosmetic values, or unrelated checks bundled into an existing test.
