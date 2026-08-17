using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LE1GalaxyMapEditor;
using LE1GalaxyMapEditor.Controls;
using LE1GalaxyMapEditor.Converters;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Presentation;
using LE1GalaxyMapEditor.Rendering;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Views;
using LE1GalaxyMapEditor.Workflows;
using LE1GalaxyMapEditor.Workflows.Editing;
using LE1GalaxyMapEditor.Workflows.Ports;
using LE1GalaxyMapEditor.Workflows.Queries;
using LegendaryExplorerCore.Packages;

namespace LE1GalaxyMapEditor.Tests;

internal static partial class Program
{
    private static void InheritedRelayRedirectPersistence()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Relay Redirect", "RELAY_REDIRECT", ModuleColor.Red,
                TestReservations()), "module created");

            var local = viewModel.Document!.Clusters.Single(cluster => cluster.Label == "Cluster03");
            var relay = viewModel.Document.Relays.Single(row => row.RowId == 1);
            FindNode(viewModel, row => row is Cluster cluster && cluster.RowId == local.RowId).IsSelected = true;
            var redirect = viewModel.Inspector.Sections.Single(section => section.Title == "Relay connections")
                .Actions.Single(action => action.Detail.Contains("Relay row 1", StringComparison.Ordinal) &&
                                          action.Label.StartsWith("Redirect", StringComparison.Ordinal));

            var incident = viewModel.Document.GetRelaysForCluster(local)
                .SelectMany(row => new[] { row.StartCluster?.RowId, row.EndCluster?.RowId })
                .Where(rowId => rowId.HasValue).Select(rowId => rowId!.Value).ToHashSet();
            var target = viewModel.Document.Clusters.First(cluster =>
                cluster.RowId != local.RowId && !incident.Contains(cluster.RowId));
            True(viewModel.Document.TryGetRelayCode(target, out var targetCode, out _), "target Relay code resolves");

            redirect.Command.Execute(null);
            FindNode(viewModel, row => row is Cluster cluster && cluster.RowId == target.RowId).IsSelected = true;

            var updated = viewModel.Document.Relays.Single(row => row.RowId == relay.RowId);
            True(updated.StartClusterEncoded == 30000 || updated.EndClusterEncoded == 30000,
                "selected Local Cluster endpoint is preserved");
            True(updated.StartClusterEncoded == targetCode || updated.EndClusterEncoded == targetCode,
                "opposite endpoint is redirected");
            Equal("RELAY_REDIRECT", updated.Origin!.ModuleTag, "redirected Relay provenance");
            Equal(2, viewModel.Workspace!.GetOverrideChain(updated.Key).Count,
                "redirect is represented by a same-row-ID override");
            True(viewModel.HasPendingChanges, "Relay redirect remains staged");
            True(viewModel.CommitPendingChanges(), "Relay redirect commit succeeds");
            True(File.Exists(Path.Combine(viewModel.ActiveModule!.FolderPath!, "GalaxyMap_Relay_part.csv")),
                "Relay override CSV written");
        });
    }

    private static void RememberedModuleWorkspace()
    {
        WithTemporaryDirectory(parent =>
        {
            var settingsPath = Path.Combine(parent, "workspace.json");
            var first = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath));
            True(first.LoadBuiltIn(), "first BASEGAME load");
            True(first.CreateModule(parent, "Remembered Module", "REMEMBERED", ModuleColor.Green,
                TestReservations(), loadOrder: 12), "remembered module created");
            var moduleFolder = first.ActiveModule!.FolderPath!;
            True(File.Exists(settingsPath), "workspace settings written when module is mounted");
            using (var workspaceJson = JsonDocument.Parse(File.ReadAllText(settingsPath)))
            {
                Equal(2, workspaceJson.RootElement.GetProperty("schemaVersion").GetInt32(),
                    "workspace uses location-only schema");
                var rememberedModule = workspaceJson.RootElement.GetProperty("modules")[0];
                SequenceEqual(["folderPath"], rememberedModule.EnumerateObject().Select(property => property.Name),
                    "manifest-backed workspace entry stores only its folder");
                Equal(moduleFolder, rememberedModule.GetProperty("folderPath").GetString()!,
                    "workspace remembers the module folder");
            }

            File.WriteAllText(settingsPath, $$"""
                {
                  "schemaVersion": 1,
                  "activeModuleTag": "REMEMBERED",
                  "modules": [
                    {
                      "name": "Stale workspace name",
                      "tag": "REMEMBERED",
                      "color": "Red",
                      "folderPath": {{JsonSerializer.Serialize(moduleFolder)}},
                      "isReadOnly": false,
                      "loadOrder": 999,
                      "reservations": {},
                      "clusterTextures": {}
                    }
                  ]
                }
                """, new UTF8Encoding(false));

            var restored = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath),
                confirmAction: _ => true);
            True(restored.LoadRememberedWorkspace(), "remembered workspace restores cleanly");
            Equal(1, restored.Workspace!.ModuleLayers.Count, "remembered module count");
            Equal("REMEMBERED", restored.ActiveModule!.Tag, "remembered active module");
            Equal("Remembered Module", restored.ActiveModule.Name,
                "module manifest overrides stale version-one workspace metadata");
            Equal(12, restored.ActiveModule.LoadOrder, "remembered mount priority");
            var restoredModule = restored.ActiveModule;
            restored.Workspace.SetActiveModule(null);
            True(restored.SetActiveModule(restoredModule), "module can be explicitly made active");
            Equal("REMEMBERED", new GalaxyMapWorkspaceStore(settingsPath).Load().ActiveModuleTag!,
                "explicit active-module choice is persisted immediately");
            using (var migratedWorkspaceJson = JsonDocument.Parse(File.ReadAllText(settingsPath)))
            {
                Equal(2, migratedWorkspaceJson.RootElement.GetProperty("schemaVersion").GetInt32(),
                    "version-one workspace migrates when the active choice is saved");
                SequenceEqual(["folderPath"], migratedWorkspaceJson.RootElement.GetProperty("modules")[0]
                        .EnumerateObject().Select(property => property.Name),
                    "migrated manifest-backed entry drops legacy metadata");
            }
            True(restored.UpdateModuleMetadata(
                restored.ActiveModule,
                "Remembered Module Edited",
                "REMEMBERED",
                ModuleColor.Magenta,
                25,
                restored.ActiveModule.Reservations), "module metadata edit stages");
            True(restored.HasPendingChanges, "module metadata waits for Commit");
            True(restored.CommitPendingChanges(), "module metadata commit succeeds");
            var editedManifest = new GalaxyMapModuleManifestStore().Load(moduleFolder);
            Equal(25, editedManifest.LoadOrder, "edited mount priority written to manifest");
            Equal(ModuleColor.Magenta, editedManifest.Color, "edited module colour written to manifest");
            True(restored.UpdateModuleMetadata(restored.ActiveModule, "Transient uncommitted name", "REMEMBERED",
                ModuleColor.Red, 26, restored.ActiveModule.Reservations), "transient metadata stages before refresh");
            True(restored.RefreshRememberedWorkspace(), "Refresh reloads the remembered JSON workspace");
            Equal("Remembered Module Edited", restored.ActiveModule!.Name, "Refresh restores committed manifest data");
            Equal(25, restored.ActiveModule.LoadOrder, "Refresh restores committed mount priority");
            True(!restored.HasPendingChanges, "Refresh clears confirmed transient changes");

            var movedFolder = moduleFolder + "_moved";
            Directory.Move(moduleFolder, movedFolder);
            var missing = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath));
            True(!missing.LoadRememberedWorkspace(), "missing module flags startup failure");
            True(missing.HasError, "missing module raises visible error flag");
            True(missing.ValidationDiagnostics.Any(item => item.Code == "WORKSPACE-MODULE-MISSING"),
                "missing module diagnostic is structured");
            missing.DismissErrorCommand.Execute(null);
            True(!missing.HasError, "error banner can be dismissed without removing its diagnostic");
            True(missing.ValidationDiagnostics.Any(item => item.Code == "WORKSPACE-MODULE-MISSING"),
                "dismissing the banner preserves validation details");
        });
    }

    private static void MountPriorityAndRowInstances()
    {
        WithTemporaryDirectory(parent =>
        {
            var selectedTarget = "MODULE_A";
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")),
                (_, modules) => modules.Single(module => module.Tag == selectedTarget));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Module A", "MODULE_A", ModuleColor.Red,
                TestReservations(), loadOrder: 10), "Module A created");
            True(viewModel.CreateModule(parent, "Module B", "MODULE_B", ModuleColor.Cyan,
                AlternateReservations(), loadOrder: 20), "Module B created");

            FindNode(viewModel, row => row is Cluster { RowId: 1 }).IsSelected = true;
            var x = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            x.Value = "0.21";
            Equal("MODULE_A", viewModel.Document!.ClustersByRowId[1].Origin!.ModuleTag,
                "first override becomes effective");

            var baseTab = viewModel.RowInstanceTabs.Single(tab => tab.Module.IsBaseGame);
            baseTab.SelectCommand.Execute(null);
            selectedTarget = "MODULE_B";
            x = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            x.Value = "0.82";

            Equal("MODULE_B", viewModel.Document.ClustersByRowId[1].Origin!.ModuleTag,
                "higher-priority override wins");
            NearlyEqual(0.82, viewModel.Document.ClustersByRowId[1].X, "higher-priority value");
            Equal(3, viewModel.Workspace!.GetOverrideChain(
                new GalaxyMapRowKey(GalaxyMapTable.Cluster, 1)).Count, "BASEGAME plus two module instances");
            True(FindNode(viewModel, row => row is Cluster { RowId: 1 }).HasMultipleInstances,
                "hierarchy marks multiple instances");
            Equal(3, viewModel.RowInstanceTabs.Count, "comparison tabs include every instance");

            viewModel.RowInstanceTabs.Single(tab => tab.Module.Tag == "MODULE_A").SelectCommand.Execute(null);
            x = viewModel.Inspector.Sections.Single(section => section.Title == "Cluster")
                .Fields.Single(field => field.Name == "X");
            NearlyEqual(0.21, double.Parse(x.Value, CultureInfo.InvariantCulture),
                "lower-priority module instance can be inspected");
            True(viewModel.ValidationDiagnostics.Any(item => item.Code == "ID-MODULE-OVERRIDE"),
                "higher same-row module override is allowed and identified");
        });
    }

    private static void ModuleUnlinkPreservesFiles()
    {
        WithTemporaryDirectory(parent =>
        {
            var settingsPath = Path.Combine(parent, "workspace.json");
            string? confirmation = null;
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath),
                confirmAction: message =>
                {
                    confirmation = message;
                    return true;
                });
            True(viewModel.LoadBuiltIn(), "BASEGAME loads before unlink test");
            True(viewModel.CreateModule(parent, "Unlink Test", "UNLINK_TEST", ModuleColor.Green,
                TestReservations()), "authoring module is created");
            var moduleFolder = viewModel.ActiveModule!.FolderPath!;
            True(viewModel.UpdateModuleMetadata(
                viewModel.ActiveModule,
                "Transient unlink name",
                viewModel.ActiveModule.Tag,
                ModuleColor.Magenta,
                viewModel.ActiveModule.LoadOrder,
                viewModel.ActiveModule.Reservations), "module receives a staged metadata change");
            var stagedModule = viewModel.ActiveModule!;
            True(viewModel.HasPendingChanges, "module is dirty before unlinking");

            True(viewModel.UnlinkModule(stagedModule), "module unlinks successfully");
            Equal(0, viewModel.Workspace!.ModuleLayers.Count, "module layer is removed from memory");
            True(viewModel.ActiveModule is null, "active module clears when no writable fallback exists");
            True(viewModel.HasPendingChanges, "unlink remains staged as a workspace change");
            True(confirmation?.Contains("staged changes", StringComparison.OrdinalIgnoreCase) == true,
                "confirmation warns about staged changes");
            True(Directory.Exists(moduleFolder), "module folder is preserved");
            True(File.Exists(Path.Combine(moduleFolder, GalaxyMapModuleManifestStore.FileName)),
                "module manifest is preserved");
            Equal(1, new GalaxyMapWorkspaceStore(settingsPath).Load().Modules.Count,
                "workspace JSON keeps the module until Commit");
            var removal = viewModel.CreateCommitPreview().Sections
                .Single(section => section.FileName == GalaxyMapWorkspaceStore.FileName)
                .Entries.Single();
            Equal("REMOVE", removal.Badge, "review identifies the staged workspace removal");
            True(viewModel.CommitPendingChanges(), "workspace removal commits");
            Equal(0, new GalaxyMapWorkspaceStore(settingsPath).Load().Modules.Count,
                "workspace JSON forgets the module after Commit");
        });
    }

    private static void ModuleMembershipWaitsForCommit()
    {
        WithTemporaryDirectory(parent =>
        {
            var sourceSettings = Path.Combine(parent, "source-workspace.json");
            var source = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(sourceSettings));
            True(source.LoadBuiltIn(), "source BASEGAME loads");
            True(source.CreateModule(parent, "Open Test", "OPEN_TEST", ModuleColor.Cyan,
                TestReservations()), "source module is created");
            var moduleFolder = source.ActiveModule!.FolderPath!;

            var settingsPath = Path.Combine(parent, "opened-workspace.json");
            CommitPreview? reviewed = null;
            var allowCommit = false;
            var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(settingsPath),
                confirmAction: _ => true,
                commitReviewAction: preview =>
                {
                    reviewed = preview;
                    return allowCommit;
                });
            True(viewModel.LoadBuiltIn(), "target BASEGAME loads");
            True(viewModel.OpenExistingModule(moduleFolder), "existing module opens in memory");
            True(viewModel.HasPendingChanges, "opening stages workspace membership");
            True(!File.Exists(settingsPath), "opening does not create workspace JSON");
            var addition = viewModel.CreateCommitPreview().Sections
                .Single(section => section.FileName == GalaxyMapWorkspaceStore.FileName)
                .Entries.Single();
            Equal("ADD", addition.Badge, "review identifies the staged workspace addition");

            viewModel.CommitCommand.Execute(null);
            NotNull(reviewed, "staged addition opens Review changes");
            True(!File.Exists(settingsPath), "cancelling review does not persist the addition");
            allowCommit = true;
            viewModel.CommitCommand.Execute(null);
            Equal(1, new GalaxyMapWorkspaceStore(settingsPath).Load().Modules.Count,
                "Commit persists the opened module");

            var openedModule = viewModel.ActiveModule!;
            True(viewModel.UnlinkModule(openedModule), "opened module unlinks in memory");
            Equal(1, new GalaxyMapWorkspaceStore(settingsPath).Load().Modules.Count,
                "unlink does not immediately persist the removal");
            var removal = viewModel.CreateCommitPreview().Sections
                .Single(section => section.FileName == GalaxyMapWorkspaceStore.FileName)
                .Entries.Single();
            Equal("REMOVE", removal.Badge, "review identifies the staged workspace removal");
            allowCommit = false;
            viewModel.CommitCommand.Execute(null);
            Equal(1, new GalaxyMapWorkspaceStore(settingsPath).Load().Modules.Count,
                "cancelling review does not persist the removal");
            allowCommit = true;
            viewModel.CommitCommand.Execute(null);
            Equal(0, new GalaxyMapWorkspaceStore(settingsPath).Load().Modules.Count,
                "Commit persists the unlink");
        });
    }

    private static void ModuleTexturesAndNebulaSystems()
    {
        WithTemporaryDirectory(parent =>
        {
            var viewModel = new MainViewModel(new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(parent, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "BASEGAME loads");
            True(viewModel.CreateModule(parent, "Texture Module", "TEXTURE_MODULE", ModuleColor.Purple,
                TestReservations()), "texture module created");

            var nebulaSystem = viewModel.Document!.Systems.First(system => system.ShowNebula == 1 && system.Cluster is not null);
            var cluster = nebulaSystem.Cluster!;
            FindNode(viewModel, row => row is Cluster candidate && candidate.RowId == cluster.RowId).IsSelected = true;
            var sourceTexture = Path.Combine(FindTextureDirectory(), "stars_bg.jpg");
            True(viewModel.StageClusterTexture(cluster, viewModel.ActiveModule!, sourceTexture),
                "JPEG module texture is staged");
            var expectedPath = Path.Combine(viewModel.ActiveModule!.FolderPath!, "textures",
                $"Cluster_{cluster.RowId}_stars_bg.jpg");
            True(!File.Exists(expectedPath), "texture copy waits for Commit");
            True(viewModel.CurrentViewModel is ClusterViewModel { BackgroundTexture: not null },
                "staged Cluster texture previews immediately");
            var stagedTexture = ((ClusterViewModel)viewModel.CurrentViewModel!).BackgroundTexture;
            True(viewModel.CloneRow(cluster, new CloneContentRequest(100, "Cluster99", 0, "Shared Background Cluster", false)),
                "Cluster with the same Background value is cloned");
            True(viewModel.CurrentViewModel is ClusterViewModel sharedView &&
                 ReferenceEquals(stagedTexture, sharedView.BackgroundTexture),
                "matching Background value reuses the linked module texture");

            FindNode(viewModel, row => row is GalaxySystem system && system.RowId == nebulaSystem.RowId).IsSelected = true;
            var systemView = (SystemViewModel)viewModel.CurrentViewModel!;
            True(systemView.UsesNebulaBackground, "ShowNebula system uses Cluster texture");
            NearlyEqual(2, systemView.BackgroundScale, "nebula background is rendered at 200 percent");
            NotNull(systemView.BackgroundTexture, "nebula background texture resolves");

            True(viewModel.CommitPendingChanges(), "texture metadata commit succeeds");
            True(File.Exists(expectedPath), "texture is copied into module textures folder on Commit");
            var reloaded = new GalaxyMapModuleManifestStore().Load(viewModel.ActiveModule.FolderPath!);
            Equal("textures/Cluster_" + cluster.RowId + "_stars_bg.jpg",
                reloaded.ClusterTextureLinks[cluster.RowId], "manifest stores Cluster texture link");
            NotNull(new GalaxyMapTextureService(FindTextureDirectory()).GetModuleClusterTexture(reloaded, cluster.RowId),
                "committed module texture resolves independently of a Cluster row override");

            const string customPlanetPath = "BIOA_TEXTURE_MODULE_T.CustomPlanet01";
            const string customPlanetName = "CustomPlanet01";
            var materialPlanet = viewModel.Document.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
            var designer = viewModel.CreatePlanetDesigner(materialPlanet.Key);
            var originalTextureValues = designer.Groups.SelectMany(group => group.Fields)
                .Where(field => field.IsTexture)
                .ToDictionary(field => field.Definition.Id, field => field.Primary.Value);
            True(designer.LinkModuleTexture(
                    new PlanetTextureLinkRequest(
                        customPlanetPath,
                        sourceTexture,
                        PlanetTextureCategory.Continent | PlanetTextureCategory.Normals)),
                "Planet texture is staged with selected material categories");
            foreach (var field in designer.Groups.SelectMany(group => group.Fields).Where(field => field.IsTexture))
            {
                Equal(originalTextureValues[field.Definition.Id], field.Primary.Value,
                    $"linking a Planet texture preserves {field.Definition.Id}");
            }
            var stagedPlanetLink = viewModel.ActiveModule!.PlanetTextureLinks.Single();
            var stagedPlanetPath = Path.Combine(
                viewModel.ActiveModule.FolderPath!,
                stagedPlanetLink.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            True(!File.Exists(stagedPlanetPath), "Planet preview image waits for Commit");

            True(designer.Groups.Single(group => group.Name == "Continent / Landmass").Fields
                    .Where(field => field.IsTexture)
                    .All(field => field.TextureOptions.Contains(customPlanetName)),
                "Continent category exposes the linked Planet texture by object name");
            True(designer.Groups.Single(group => group.Name == "Normals").Fields
                    .Where(field => field.IsTexture)
                    .All(field => field.TextureOptions.Contains(customPlanetName)),
                "Normals category exposes the linked Planet texture by object name");
            True(designer.Groups.Single(group => group.Name == "Ocean").Fields
                    .Where(field => field.IsTexture)
                    .All(field => !field.TextureOptions.Contains(customPlanetName)),
                "unselected Ocean category hides the linked Planet texture");

            designer.Groups.SelectMany(group => group.Fields)
                .Single(field => field.Definition.Id == "ContinentMask01").Primary.Value = customPlanetName;
            True(designer.TryApply(), "linked Planet texture reference applies to the Planet 2DA draft");
            Equal(customPlanetPath,
                viewModel.Document!.PlanetsByRowId[materialPlanet.RowId].ExtraFields["ContinentMask01"],
                "selecting a Planet texture object name writes its full in-memory path");

            const string renamedPlanetPath = "BIOA_TEXTURE_MODULE_T.RenamedPlanet01";
            viewModel.TableViewer.SelectedTable = GalaxyMapTable.Planet;
            viewModel.TableViewer.RefreshIfNeeded(force: true);
            var continentColumn = viewModel.TableViewer.Columns.ToList().FindIndex(column =>
                column.Name.Equals("ContinentMask01", StringComparison.OrdinalIgnoreCase));
            var planetTableRow = viewModel.TableViewer.Rows.Single(row => row.Key == materialPlanet.Key);
            True(viewModel.TableViewer.CommitCellEdit(planetTableRow, continentColumn, renamedPlanetPath).Succeeded,
                "2DA table can rename a linked Planet texture reference");
            var renamedPlanetLink = viewModel.ActiveModule!.PlanetTextureLinks.Single();
            Equal(renamedPlanetPath, renamedPlanetLink.InMemoryPath,
                "2DA rename updates the linked Planet texture reference");
            Equal(stagedPlanetLink.RelativePath, renamedPlanetLink.RelativePath,
                "2DA rename keeps the linked local Planet image path");

            True(viewModel.CommitPendingChanges(), "Planet texture metadata commit succeeds");
            True(File.Exists(stagedPlanetPath), "Planet preview image is copied into the module on Commit");
            var reloadedPlanetModule = new GalaxyMapModuleManifestStore().Load(viewModel.ActiveModule.FolderPath!);
            Equal(renamedPlanetPath, reloadedPlanetModule.PlanetTextureLinks.Single().InMemoryPath,
                "renamed Planet texture relationship survives manifest reload");

            File.Delete(stagedPlanetPath);
            var staleTextureDesigner = viewModel.CreatePlanetDesigner(materialPlanet.Key);
            var staleOption = staleTextureDesigner.GetLinkedTextureOptions().Single();
            True(!staleOption.IsAvailable,
                "a linked Planet texture reports a missing committed preview file");
            True(staleOption.CanUnlink,
                "a stale Planet texture in a writable module remains removable");
            True(staleOption.ReferenceCount > 0,
                "texture management reports existing Planet-row references before unlinking");
            True(staleTextureDesigner.Groups.SelectMany(group => group.Fields)
                    .Where(field => field.IsTexture && !string.Equals(
                        field.Primary.RawValue,
                        renamedPlanetPath,
                        StringComparison.OrdinalIgnoreCase))
                    .All(field => !field.TextureOptions.Contains("RenamedPlanet01")),
                "a missing linked texture is not offered by unrelated material dropdowns");
            True(staleTextureDesigner.Groups.SelectMany(group => group.Fields)
                    .Where(field => field.IsTexture && string.Equals(
                        field.Primary.RawValue,
                        renamedPlanetPath,
                        StringComparison.OrdinalIgnoreCase))
                    .All(field => field.TextureOptions.Contains("RenamedPlanet01")),
                "a field preserves its visible raw value when it currently references a missing texture");

            staleTextureDesigner.RandomiseCommand.Execute(null);
            True(staleTextureDesigner.Groups.SelectMany(group => group.Fields)
                    .Where(field => field.IsTexture)
                    .All(field => !string.Equals(
                        field.Primary.RawValue,
                        renamedPlanetPath,
                        StringComparison.OrdinalIgnoreCase)),
                "a missing linked texture is excluded from the randomisation pool");
            True(staleTextureDesigner.StatusMessage.Contains("ignored 1 unavailable", StringComparison.OrdinalIgnoreCase),
                "randomisation reports stale texture links instead of silently using them");

            True(staleTextureDesigner.UnlinkModuleTexture(staleOption),
                "a stale Planet texture can be unlinked from the Designer");
            Equal(0, viewModel.ActiveModule!.PlanetTextureLinks.Count,
                "unlinking removes the texture relationship from live module metadata");
            True(viewModel.CommitPendingChanges(), "unlinked Planet texture metadata commits");
            Equal(0, new GalaxyMapModuleManifestStore().Load(viewModel.ActiveModule.FolderPath!)
                    .PlanetTextureLinks.Count,
                "unlinked Planet texture is removed from the persisted manifest");

            var pendingTextureDesigner = viewModel.CreatePlanetDesigner(materialPlanet.Key);
            True(pendingTextureDesigner.LinkModuleTexture(new PlanetTextureLinkRequest(
                    "BIOA_TEXTURE_MODULE_T.PendingRemoval",
                    sourceTexture,
                    PlanetTextureCategory.Ocean)),
                "a replacement Planet texture can be staged for pending-removal coverage");
            var pendingLink = viewModel.ActiveModule!.PlanetTextureLinks.Single();
            var pendingPath = Path.Combine(
                viewModel.ActiveModule.FolderPath!,
                pendingLink.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            True(!File.Exists(pendingPath), "new replacement preview remains pending before Commit");
            True(pendingTextureDesigner.UnlinkModuleTexture(
                    pendingTextureDesigner.GetLinkedTextureOptions().Single()),
                "a not-yet-committed Planet texture can be unlinked");
            True(viewModel.CommitPendingChanges(), "pending texture unlink metadata commits");
            True(!File.Exists(pendingPath),
                "unlinking removes its pending file write instead of creating an orphan on Commit");
        });
    }

    private static void SpectreExpansionModule(string folder)
    {
        var loader = new CsvGalaxyMapLoader();
        var module = new GalaxyMapModule(
            "Spectre Expansion Mod", "SEM", ModuleColor.Purple, folder,
            isReadOnly: true, loadOrder: 1,
            new ModuleIdReservations(
                new RowIdRange(50, 59),
                new RowIdRange(100, 109),
                new RowIdRange(8000, 8068),
                null,
                new RowIdRange(50, 62)));
        var workspace = new GalaxyMapWorkspace(loader.LoadBuiltInLayer(), [loader.LoadPartFolder(folder, module)]);
        var document = workspace.EffectiveDocument;

        Equal(27, document.Clusters.Count, "SEM effective Cluster count");
        Equal(53, document.Systems.Count, "SEM effective System count");
        Equal(302, document.Planets.Count, "SEM effective Planet count");
        Equal(7, document.PlotPlanets.Count, "SEM effective PlotPlanet count");
        Equal(106, document.Maps.Count, "SEM effective Map count");
        Equal(30, document.Relays.Count, "SEM effective Relay count");
        Equal("SEM", document.Relays.Single(row => row.RowId == 1).Origin!.ModuleTag,
            "SEM Relay row 1 overrides BASEGAME");
        Equal(500000, document.Relays.Single(row => row.RowId == 1).StartClusterEncoded,
            "SEM redirects one endpoint to Arcturus Stream");
        Equal(30000, document.Relays.Single(row => row.RowId == 1).EndClusterEncoded,
            "SEM keeps the Local Cluster endpoint");
        Equal(2, workspace.GetOverrideChain(new GalaxyMapRowKey(GalaxyMapTable.Relay, 1)).Count,
            "Relay redirect is a same-ID override");

        var diagnostics = new GalaxyMapValidator().Validate(workspace);
        True(diagnostics.Any(item => item.Code == "PLOT-CODE-MISMATCH" && item.RowId == 8000),
            "SEM PlotPlanet mismatch is detected");
        True(diagnostics.Any(item => item.Code == "TYPE-PLANET-LEVEL-MISSING" && item.RowId == 8000),
            "SEM blank PlanetLevelType is detected");
        True(diagnostics.Any(item => item.Code == "ACTIVEWORLD-MISMATCH"),
            "SEM ActiveWorld inconsistencies are detected");
        True(diagnostics.Any(item => item.Code == "RELAY-DUPLICATE-PAIR"),
            "SEM duplicate Relay pair is detected");
    }

    private static void RealExports(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "GalaxyMap_*.csv").ToDictionary(
            path => Path.GetFileName(path), SnapshotFile, StringComparer.OrdinalIgnoreCase);

        var document = new CsvGalaxyMapLoader().LoadFolder(folder);
        Equal(17, document.Clusters.Count, "real Cluster count");
        Equal(43, document.Systems.Count, "real System count");
        Equal(233, document.Planets.Count, "real Planet count");
        Equal(6, document.PlotPlanets.Count, "real PlotPlanet count");
        Equal(106, document.Maps.Count, "real Map count");
        Equal(17, document.Relays.Count, "real Relay count");
        Equal(16, document.Relays.Count(relay => relay.IsResolved), "real resolved Relay count");

        SequenceEqual([1, 3, 6], document.Clusters.Take(3).Select(cluster => cluster.RowId), "real sparse Cluster IDs");
        Equal(11, document.Clusters[0].ExtraFields.Count, "real Cluster extra column count");
        Equal(10, document.Systems[0].ExtraFields.Count, "real System extra column count");
        Equal(111, document.Planets[0].ExtraFields.Count, "real Planet extra column count");
        Equal(6, document.PlotPlanets[0].ExtraFields.Count, "real PlotPlanet extra column count");

        var localCluster = document.ClustersByRowId[3];
        Equal(2, localCluster.Systems.Count, "Local Cluster system count");
        Equal(11, localCluster.Systems.Sum(system => system.Planets.Count), "Local Cluster object count");

        var sol = document.SystemsByRowId[4];
        Equal(11, sol.Planets.Count, "Sol object count");
        Equal(10, sol.Planets.Count(planet => planet.OrbitRing != 0), "Sol orbit ring count");

        var horseHead = document.ClustersByRowId[7];
        Equal(5, document.GetRelaysForCluster(horseHead).Count, "Horse Head incident Relay rows");
        Equal(4, document.GetRelaysForCluster(horseHead).Count(relay => relay.IsResolved),
            "Horse Head visible Relay connections");

        Equal("Cluster03.jpg",
            GalaxyMapTextureService.ResolveClusterAssetName(document.ClustersByRowId[1].Background)!,
            "Serpent uses its CSV-linked Cluster03 background");
        True(document.Clusters.All(cluster =>
                GalaxyMapTextureService.ResolveClusterAssetName(cluster.Background) is not null),
            "every real Cluster background reference resolves to an asset name");

        var citadel = document.PlanetsByRowId[1];
        NotNull(citadel.PlotPlanet, "Citadel PlotPlanet");
        Equal(10101, citadel.PlotPlanet!.Code, "Citadel PlotPlanet code");
        NotNull(citadel.LinkedMap, "Citadel Map");
        Equal("BIOA_STA00", citadel.LinkedMap!.MapName, "Citadel map name");
        Equal("start_NOR10_03", citadel.LinkedMap.StartPoint, "Citadel start point");

        var inspector = new PropertyInspectorViewModel();
        inspector.Inspect(citadel);
        var otherColumns = inspector.Sections
            .Where(section => section.Title is "Visibility and usability" or "Destination / unused internals" or
                "Legacy event routing" or "Advanced Planet fields")
            .SelectMany(section => section.Fields).ToArray();
        True(inspector.Sections.All(section => section.Title != "Planet appearance"),
            "real appearance columns are reserved for the Planet Designer");
        Equal(94, PlanetAppearanceSchema.Columns.Count, "real appearance schema column count");
        Equal(19, otherColumns.Length, "real nonappearance and interaction column count");

        var ilos = document.PlanetsByRowId[86];
        NotNull(ilos.PlotPlanet, "Ilos PlotPlanet");
        True(ilos.LinkedMap is null, "Ilos has no Map");

        foreach (var (fileName, before) in files)
        {
            var after = SnapshotFile(Path.Combine(folder, fileName));
            Equal(before, after, $"source file remains unchanged: {fileName}");
        }
    }
}
