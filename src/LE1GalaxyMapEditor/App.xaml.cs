using System.IO;
using System.Windows;
using System.Windows.Threading;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;

namespace LE1GalaxyMapEditor;

public partial class App : Application
{
    private readonly StartupPerformanceTrace _startupTrace = new();

    public App()
    {
        var executable = Environment.ProcessPath;
        var assemblyPath = typeof(App).Assembly.Location;
        var imageSizes = new List<string>(2);
        if (executable is not null && File.Exists(executable))
        {
            imageSizes.Add($"Executable: {new FileInfo(executable).Length:N0} bytes");
        }

        if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
        {
            imageSizes.Add($"Managed assembly: {new FileInfo(assemblyPath).Length:N0} bytes");
        }

        _startupTrace.Mark(
            "App constructor entered",
            imageSizes.Count == 0 ? null : string.Join("; ", imageSizes));
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _startupTrace.Mark("App.OnStartup entered");

        try
        {
            var viewModel = LoadViewModel(e.Args);

            MainWindow editor;
            using (_startupTrace.Measure("Construct MainWindow and apply DataContext"))
            {
                editor = new MainWindow { DataContext = viewModel };
            }

            editor.ContentRendered += (_, _) => _startupTrace.Mark("MainWindow first rendered");
            MainWindow = editor;

            using (_startupTrace.Measure("Show MainWindow"))
            {
                editor.Show();
            }

            await Dispatcher.InvokeAsync(
                () => _startupTrace.Mark("MainWindow dispatcher idle; editor usable"),
                DispatcherPriority.ContextIdle);

            // Persist timings away from the measured startup path.
            _ = _startupTrace.SaveAsync();
        }
        catch (Exception exception)
        {
            _startupTrace.Mark("Startup failed", exception.GetType().Name);
            var logPath = await _startupTrace.SaveAsync(exception);
            var traceDetail = string.IsNullOrWhiteSpace(logPath)
                ? string.Empty
                : $"\n\nStartup diagnostics: {logPath}";
            MessageBox.Show(
                $"LE1 Galaxy Map Editor could not start.\n\n{exception.Message}{traceDetail}",
                "Startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private MainViewModel LoadViewModel(IReadOnlyList<string> arguments)
    {
        var textures = new GalaxyMapTextureService((cacheKey, duration) =>
            _startupTrace.RecordDuration("Decode background texture", duration, cacheKey));
        MainViewModel viewModel;
        using (_startupTrace.Measure("Construct MainViewModel"))
        {
            viewModel = new MainViewModel(new CsvGalaxyMapLoader(), textures);
        }

        using (_startupTrace.Measure("Load BASEGAME and remembered modules"))
        {
            viewModel.LoadRememberedWorkspace();
        }

        if (arguments.Count > 0 && Directory.Exists(arguments[0]))
        {
            using (_startupTrace.Measure("Load command-line folder", arguments[0]))
            {
                viewModel.LoadFolder(arguments[0]);
            }
        }

        return viewModel;
    }
}
