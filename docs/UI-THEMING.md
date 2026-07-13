# UI theming guide

Most application-wide colours and fonts live near the top of:

`src/LE1GalaxyMapEditor/App.xaml`

Changing a shared resource there updates every control which refers to that resource. Close the application before rebuilding it so Windows is not holding the executable open.

## Main colour palette

The most useful resources are:

| Resource | Used for |
| --- | --- |
| `AppBackgroundBrush` | Main application and popup backgrounds |
| `PanelBrush` | Hierarchy and property panels |
| `PanelRaisedBrush` | Buttons and raised controls |
| `BorderBrush` | Panel, field and button outlines |
| `TextBrush` | Normal text |
| `MutedTextBrush` | Secondary labels and explanatory text |
| `AccentBrush` | Cyan headings, focus outlines and important controls |
| `AccentDimBrush` | Selected rows and subtle highlights |
| `DangerBrush` | Errors and destructive actions |
| `WarningBrush` | Validation warnings and conflict markers |

Colours use hexadecimal notation:

```xml
<SolidColorBrush x:Key="AccentBrush" Color="#47B4D5" />
```

The six digits are `RRGGBB`. An optional first pair controls opacity, producing `#AARRGGBB`. For example, `#8047B4D5` is the accent colour at approximately 50% opacity.

## Application-wide font

The default font is controlled by the implicit `TextBlock` style in `App.xaml`:

```xml
<Style TargetType="TextBlock">
    <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
    <Setter Property="FontFamily" Value="Segoe UI" />
</Style>
```

Change `Segoe UI` to another font installed on Windows. To establish an application-wide base size, add:

```xml
<Setter Property="FontSize" Value="12" />
```

Some headings deliberately specify their own `FontSize`, `FontWeight` or `Foreground`, so those local values take priority over the global style.

## Changing one specific piece of text

Open the XAML file containing it and edit that element directly. Common locations are:

- `MainWindow.xaml` — top bar, hierarchy, breadcrumbs, property inspector and diagnostics.
- `Views/GalaxyView.xaml` — Galaxy-view labels and status box.
- `Views/ClusterView.xaml` — Cluster-view labels and status box.
- `Views/SystemView.xaml` — System-view labels and legend.
- `Views/ModuleSetupWindow.xaml` — module creation/editing popup.
- `Views/ModuleTargetWindow.xaml` — edit-target popup.
- `Views/CloneContentWindow.xaml` — cloning popup.
- `Views/ColorPickerWindow.xaml` — colour picker.

For example:

```xml
<TextBlock Text="HIERARCHY"
           FontSize="12"
           FontWeight="Bold"
           Foreground="{StaticResource AccentBrush}" />
```

Use a shared brush reference where possible. A direct colour such as `Foreground="#FFFFFF"` is suitable only when that element genuinely needs a unique colour.

## Control-specific styling

`App.xaml` also contains the global templates for `Button`, `ComboBox`, `ComboBoxItem`, `ScrollBar`, `ContextMenu`, `MenuItem`, `TextBox`, `ListBox` and `ListBoxItem`.

The named `ChromeButtonStyle` controls standard buttons. `MainWindow.xaml` contains a few styles used only by the editor shell:

- `PanelHeaderStyle`
- `InspectorTextBoxStyle`
- `InspectorActionButtonStyle`

Be careful when changing a `ControlTemplate`: it controls behaviour and hit-testing as well as appearance. Palette, padding and font changes are low-risk; structural template changes should be followed by the headless checks.

## Checking a change

From the project folder:

```powershell
dotnet build LE1GalaxyMapEditor.sln -c Release --no-restore
dotnet run --project tests\LE1GalaxyMapEditor.Tests\LE1GalaxyMapEditor.Tests.csproj -c Release --no-build
```

If the build says the executable is in use, close the running editor and repeat the command.

