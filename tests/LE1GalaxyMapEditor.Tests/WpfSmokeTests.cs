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
    private static void WpfViewsComposeAfterLoad()
    {
        WithFixture(folder =>
        {
            var application = new App();
            application.InitializeComponent();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Exception? dispatcherFailure = null;
            application.DispatcherUnhandledException += (_, eventArgs) =>
            {
                dispatcherFailure = eventArgs.Exception;
                eventArgs.Handled = true;
            };

            using var viewModel = new MainViewModel(
                new CsvGalaxyMapLoader(),
                new GalaxyMapTextureService(FindTextureDirectory()),
                new GalaxyMapWorkspaceStore(Path.Combine(folder, "workspace.json")));
            True(viewModel.LoadBuiltIn(), "embedded BASEGAME loads");

            var window = new MainWindow { DataContext = viewModel };
            Compose(window, application.Dispatcher);
            NotNull(window.FindName("MapSquare"), "main map composes");

            var clusterNode = viewModel.HierarchyRoots.Single().Children.First();
            clusterNode.IsSelected = true;
            Compose(window, application.Dispatcher);
            True(viewModel.CurrentViewModel is ClusterViewModel, "cluster navigation composes");

            var systemNode = clusterNode.Children.First();
            systemNode.IsSelected = true;
            Compose(window, application.Dispatcher);
            True(viewModel.CurrentViewModel is SystemViewModel, "system navigation composes");

            window.Close();
            application.Shutdown();
            if (dispatcherFailure is not null)
            {
                throw new InvalidOperationException(
                    $"WPF dispatcher failure: {dispatcherFailure.Message}", dispatcherFailure);
            }
        });
    }
    private static void Compose(
        FrameworkElement element,
        Dispatcher dispatcher,
        double width = 1440,
        double height = 860)
    {
        element.InvalidateMeasure();
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static HierarchyNodeViewModel FindNode(MainViewModel viewModel, Func<GalaxyMapRow, bool> predicate)
    {
        static IEnumerable<HierarchyNodeViewModel> Flatten(IEnumerable<HierarchyNodeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                {
                    yield return child;
                }
            }
        }

        return Flatten(viewModel.HierarchyRoots)
            .Single(node => node.Model is { } model && predicate(model));
    }
}
