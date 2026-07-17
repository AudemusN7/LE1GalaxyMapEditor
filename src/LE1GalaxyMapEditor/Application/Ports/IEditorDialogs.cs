using LE1GalaxyMapEditor.Models;

namespace LE1GalaxyMapEditor.Workflows.Ports;

public sealed record ModuleSetupDialogRequest(
    bool SelectParentFolder,
    string FolderPath,
    string SuggestedName,
    string SuggestedTag,
    ModuleIdReservations SuggestedReservations,
    int SuggestedLoadOrder,
    bool IsEditing = false,
    ModuleColor SuggestedColor = ModuleColor.Cyan,
    bool CanSetActive = false,
    bool IsActive = false,
    Func<bool>? SetActiveAction = null,
    Func<bool>? UnlinkAction = null);

public sealed record LandableDestinationDefaults(
    string MapName,
    string StartPoint,
    string EventName,
    int? ButtonLabel,
    bool CanAddPlotPlanet);

public sealed record ModuleSetupResult(
    string Name,
    string Tag,
    ModuleColor Color,
    string FolderPath,
    ModuleIdReservations Reservations,
    int LoadOrder);

public sealed record LandableDestinationRequest(
    string MapName,
    string StartPoint,
    string Event,
    int? ButtonLabel,
    bool AddPlotPlanet);

public sealed record PlanetCreationRequest(
    PlanetCreationTemplate Template,
    string NameText,
    int Name,
    double Scale,
    LandableDestinationRequest? Destination);

public sealed record PlanetShaderNameRequest(
    string PlanetName,
    int PlanetRowId,
    GalaxyMapModule TargetModule,
    string SuggestedName,
    Func<string, string?> Validate);

public sealed record CloneContentRequest(
    int RowId,
    string Label,
    int Name,
    string NameText,
    bool CloneChildren);

public sealed record MoveDestinationOption(
    int RowId,
    string Label,
    string Detail,
    string CurrentLabel,
    string ResultingLabel)
{
    public override string ToString() => Label;
}

public interface IEditorDialogs
{
    ModuleSetupResult? ConfigureModule(ModuleSetupDialogRequest request);
    string? PickModuleFolder();
    PlanetCreationRequest? CreatePlanet();
    CloneContentRequest? ConfigureClone(GalaxyMapRow source, int suggestedId, string suggestedLabel);
    GalaxyMapModule? ChooseEditTarget(
        GalaxyMapRow row,
        IReadOnlyList<GalaxyMapModule> candidates,
        GalaxyMapModule? activeModule);
    string? ChoosePlanetShaderName(PlanetShaderNameRequest request);
    string? PickClusterTexture();
    LandableDestinationRequest? ConfigureLandableDestination(LandableDestinationDefaults defaults);
    MoveDestinationOption? ChooseMoveDestination(
        GalaxyMapRow source,
        IReadOnlyList<MoveDestinationOption> options);
    bool Confirm(string message);
}
