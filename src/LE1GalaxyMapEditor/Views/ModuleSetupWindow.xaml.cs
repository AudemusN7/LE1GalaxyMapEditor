using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using LE1GalaxyMapEditor.Models;
using LE1GalaxyMapEditor.Infrastructure;
using LE1GalaxyMapEditor.Workflows.Ports;
using Microsoft.Win32;

namespace LE1GalaxyMapEditor.Views;

public partial class ModuleSetupWindow : Window
{
    private readonly bool _selectParentFolder;
    private readonly Func<bool>? _setActiveAction;
    private readonly Func<bool>? _unlinkAction;
    private bool _tagWasEdited;

    public ModuleSetupWindow(
        bool selectParentFolder,
        string folderPath,
        string suggestedName,
        string suggestedTag,
        ModuleIdReservations suggestedReservations,
        int suggestedLoadOrder = 1,
        bool isEditing = false,
        ModuleColor suggestedColor = ModuleColor.Cyan,
        bool canSetActive = false,
        bool isActive = false,
        Func<bool>? setActiveAction = null,
        Func<bool>? unlinkAction = null)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _selectParentFolder = selectParentFolder;
        _setActiveAction = setActiveAction;
        _unlinkAction = unlinkAction;
        ColorBox.ItemsSource = Enum.GetValues<ModuleColor>().Where(color => color != ModuleColor.BaseGameBlue);
        ColorBox.SelectedItem = suggestedColor == ModuleColor.BaseGameBlue ? ModuleColor.Cyan : suggestedColor;
        FolderBox.Text = folderPath;
        NameBox.Text = suggestedName;
        TagBox.Text = suggestedTag;
        LoadOrderBox.Text = suggestedLoadOrder.ToString();
        ApplyRanges(suggestedReservations);

        if (isEditing)
        {
            HeadingText.Text = "EDIT MODULE";
            ExplanationText.Text = "Changes to module metadata are staged until Commit changes is pressed.";
            FolderLabel.Text = "Module folder";
            FolderBox.IsReadOnly = true;
            BrowseButton.Visibility = Visibility.Collapsed;
            AcceptButton.Content = "Apply changes";
            SetActiveButton.Visibility = canSetActive ? Visibility.Visible : Visibility.Collapsed;
            SetActiveButton.IsEnabled = canSetActive && !isActive;
            SetActiveButton.ToolTip = isActive
                ? "This is the active editing module."
                : "Use this module as the target for new rows and overrides.";
            UnlinkButton.Visibility = unlinkAction is null ? Visibility.Collapsed : Visibility.Visible;
            UnlinkButton.ToolTip = "Remove this module from the workspace without deleting its files.";
        }

        if (!selectParentFolder && !isEditing)
        {
            HeadingText.Text = "MOUNT READ-ONLY MODULE";
            ExplanationText.Text = "The selected _part CSVs will be mounted above lower layers without being modified.";
            FolderLabel.Text = "Source folder";
            FolderBox.IsReadOnly = true;
            BrowseButton.Visibility = Visibility.Collapsed;
            AcceptButton.Content = "Mount module";
        }
    }

    public ModuleSetupResult? Result { get; private set; }

    private void SetActiveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_setActiveAction?.Invoke() != true)
        {
            ErrorText.Text = "The module could not be made active.";
            return;
        }

        SetActiveButton.IsEnabled = false;
        SetActiveButton.ToolTip = "This is the active editing module.";
        ErrorText.Text = string.Empty;
    }

    private void UnlinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_unlinkAction?.Invoke() == true)
        {
            DialogResult = false;
        }
    }

    private void ApplyRanges(ModuleIdReservations reservations)
    {
        SetRange(ClusterStartBox, ClusterEndBox, reservations.Cluster);
        SetRange(SystemStartBox, SystemEndBox, reservations.System);
        SetRange(PlanetStartBox, PlanetEndBox, reservations.Planet);
        SetRange(MapStartBox, MapEndBox, reservations.Map);
        SetRange(RelayStartBox, RelayEndBox, reservations.Relay);
    }

    private static void SetRange(TextBox startBox, TextBox endBox, RowIdRange? range)
    {
        startBox.Text = range?.Start.ToString() ?? string.Empty;
        endBox.Text = range?.End.ToString() ?? string.Empty;
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the parent folder for galaxy-map modules" };
        if (Directory.Exists(FolderBox.Text))
        {
            dialog.InitialDirectory = FolderBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void NameBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_tagWasEdited && TagBox is not null)
        {
            TagBox.Text = ToTag(NameBox.Text);
        }
    }

    private void TagBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded && TagBox.IsKeyboardFocusWithin)
        {
            _tagWasEdited = true;
        }
    }

    private void AcceptButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = RequireText(NameBox.Text, "Enter a module display name.");
            var tag = RequireText(TagBox.Text, "Enter a module tag.").ToUpperInvariant();
            if (!Regex.IsMatch(tag, "^[A-Z0-9_-]+$", RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException("The tag may contain only letters, numbers, underscores, and hyphens.");
            }

            if (ColorBox.SelectedItem is not ModuleColor color || color == ModuleColor.BaseGameBlue)
            {
                throw new InvalidOperationException("Choose one of the eight authoring-module colours.");
            }

            var selectedFolder = RequireText(FolderBox.Text, "Choose a module folder.");
            if (!Directory.Exists(selectedFolder))
            {
                throw new InvalidOperationException("The selected folder does not exist.");
            }

            var reservations = new ModuleIdReservations(
                ReadRange("Cluster", ClusterStartBox, ClusterEndBox),
                ReadRange("System", SystemStartBox, SystemEndBox),
                ReadRange("Planet", PlanetStartBox, PlanetEndBox),
                ReadRange("Map", MapStartBox, MapEndBox),
                ReadRange("Relay", RelayStartBox, RelayEndBox));

            if (!int.TryParse(LoadOrderBox.Text.Trim(), out var loadOrder) || loadOrder < 0)
            {
                throw new InvalidOperationException("Mount priority must be a whole number of zero or greater.");
            }

            Result = new ModuleSetupResult(name, tag, color, selectedFolder, reservations, loadOrder);
            DialogResult = true;
        }
        catch (InvalidOperationException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private static RowIdRange? ReadRange(string table, TextBox startBox, TextBox endBox)
    {
        var startText = startBox.Text.Trim();
        var endText = endBox.Text.Trim();
        if (startText.Length == 0 && endText.Length == 0)
        {
            return null;
        }

        if (!int.TryParse(startText, out var start) || !int.TryParse(endText, out var end) || start < 0 || end < start)
        {
            throw new InvalidOperationException($"Enter a valid inclusive {table} row range.");
        }

        return new RowIdRange(start, end);
    }

    private static string RequireText(string value, string message)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();

    private static string ToTag(string value)
    {
        var tag = Regex.Replace(value.Trim().ToUpperInvariant(), "[^A-Z0-9_-]+", "_");
        return tag.Trim('_', '-');
    }
}
