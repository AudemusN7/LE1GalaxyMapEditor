using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using LE1GalaxyMapEditor;
using LE1GalaxyMapEditor.Rendering;
using LE1GalaxyMapEditor.Services;
using LE1GalaxyMapEditor.ViewModels;
using LE1GalaxyMapEditor.Views;

namespace LE1GalaxyMapEditor.PlanetDesignerBenchmarks;

internal static class Program
{
    private const int SteadyFrameIterations = 20;
    private static readonly TimeSpan ActivityObservationWindow = TimeSpan.FromMilliseconds(350);

    [STAThread]
    private static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Planet Designer benchmark failed: {exception}");
            return 1;
        }
    }

    private static void Run()
    {
        Console.WriteLine("LE1 Galaxy Map Editor Planet Designer observational benchmark");
        Console.WriteLine($"Runtime: {Environment.Version}; process: {(Environment.Is64BitProcess ? "x64" : "x86")}");
        Console.WriteLine("Timings are observational. This tool deliberately applies no machine-dependent limits.");
        Console.WriteLine();

        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();

        var setupStarted = Stopwatch.GetTimestamp();
        var editor = new MainViewModel(new CsvGalaxyMapLoader());
        Require(editor.LoadBuiltIn(), "Embedded BASEGAME data must load before measuring the designer.");
        var planet = editor.Document!.Planets.First(PlanetAppearanceCodec.IsAppearanceCapable);
        Console.WriteLine($"Benchmark planet: row {planet.RowId}, {planet.DisplayName}");
        Console.WriteLine($"Workspace setup: {ElapsedMilliseconds(setupStarted),8:N2} ms (reported for context)");

        ForceFullCollection();
        var launchAllocationStart = GC.GetTotalAllocatedBytes(precise: true);
        var viewModelStarted = Stopwatch.GetTimestamp();
        var designerViewModel = editor.CreatePlanetDesigner(planet.Key);
        var viewModelMilliseconds = ElapsedMilliseconds(viewModelStarted);
        var afterViewModelAllocation = GC.GetTotalAllocatedBytes(precise: true);

        var constructionStarted = Stopwatch.GetTimestamp();
        var window = new PlanetDesignerWindow(designerViewModel);
        var constructionMilliseconds = ElapsedMilliseconds(constructionStarted);
        var afterConstructionAllocation = GC.GetTotalAllocatedBytes(precise: true);

        var preparationStarted = Stopwatch.GetTimestamp();
        window.PrepareForFirstShow();
        var preparationMilliseconds = ElapsedMilliseconds(preparationStarted);
        var afterPreparationAllocation = GC.GetTotalAllocatedBytes(precise: true);

        var showStarted = Stopwatch.GetTimestamp();
        window.Show();
        var showReturnMilliseconds = ElapsedMilliseconds(showStarted);
        var afterShowReturnAllocation = GC.GetTotalAllocatedBytes(precise: true);
        Require(
            PumpDispatcherUntil(
                () => window.Diagnostics.Snapshot().FramesPresented > 0,
                TimeSpan.FromSeconds(15)),
            "The renderer did not present its first frame within 15 seconds.");
        var firstPreviewMilliseconds = ElapsedMilliseconds(showStarted);
        var afterFirstPreviewAllocation = GC.GetTotalAllocatedBytes(precise: true);
        var initial = window.Diagnostics.Snapshot();
        Require(initial.RendererInitializationsCompleted == 1, "One renderer must complete initialisation.");
        Require(initial.RendererInitializationFailures == 0, "Renderer initialisation must not fail.");

        Console.WriteLine();
        Console.WriteLine("Launch stages");
        Console.WriteLine($"  Designer view model:       {viewModelMilliseconds,8:N2} ms");
        Console.WriteLine($"  Window construction:       {constructionMilliseconds,8:N2} ms");
        Console.WriteLine($"  PrepareForFirstShow:        {preparationMilliseconds,8:N2} ms");
        Console.WriteLine($"  Show() return:              {showReturnMilliseconds,8:N2} ms");
        Console.WriteLine($"  Show to first GPU preview:  {firstPreviewMilliseconds,8:N2} ms");
        Console.WriteLine($"  Frames at first preview:    {initial.FramesPresented,8:N0}");
        Console.WriteLine("  Managed allocation (process-wide; renderer initialisation uses a worker thread)");
        Console.WriteLine($"    Designer view model:      {(afterViewModelAllocation - launchAllocationStart) / 1024d,8:N1} KiB");
        Console.WriteLine($"    Window construction:      {(afterConstructionAllocation - afterViewModelAllocation) / 1024d,8:N1} KiB");
        Console.WriteLine($"    PrepareForFirstShow:       {(afterPreparationAllocation - afterConstructionAllocation) / 1024d,8:N1} KiB");
        Console.WriteLine($"    Show() call interval:      {(afterShowReturnAllocation - afterPreparationAllocation) / 1024d,8:N1} KiB");
        Console.WriteLine($"    Show return to preview:    {(afterFirstPreviewAllocation - afterShowReturnAllocation) / 1024d,8:N1} KiB");
        Console.WriteLine($"    Total measured launch:     {(afterFirstPreviewAllocation - launchAllocationStart) / 1024d,8:N1} KiB");

        window.Hide();
        var beforeHidden = window.Diagnostics.Snapshot();
        PumpDispatcherFor(ActivityObservationWindow);
        var hiddenActivity = window.Diagnostics.Snapshot() - beforeHidden;
        Require(hiddenActivity.TimerTicks == 0, "A hidden designer must stop its preview timer.");
        Require(hiddenActivity.RenderAttempts == 0, "A hidden designer must not attempt preview renders.");
        Require(hiddenActivity.FramesPresented == 0, "A hidden designer must not present preview frames.");
        Require(hiddenActivity.ScheduledPreviewDispatches == 0,
            "A hidden designer must not dispatch queued preview work.");
        PrintActivity("Hidden window", hiddenActivity, ActivityObservationWindow);

        window.Show();
        window.WindowState = WindowState.Minimized;
        var beforeMinimized = window.Diagnostics.Snapshot();
        PumpDispatcherFor(ActivityObservationWindow);
        var minimizedActivity = window.Diagnostics.Snapshot() - beforeMinimized;
        Require(minimizedActivity.TimerTicks == 0, "A minimised designer must stop its preview timer.");
        Require(minimizedActivity.RenderAttempts == 0, "A minimised designer must not attempt preview renders.");
        Require(minimizedActivity.FramesPresented == 0, "A minimised designer must not present preview frames.");
        Require(minimizedActivity.ScheduledPreviewDispatches == 0,
            "A minimised designer must not dispatch queued preview work.");
        PrintActivity("Minimised window", minimizedActivity, ActivityObservationWindow);

        window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        Require(
            PumpDispatcherUntil(() => window.IsActive, TimeSpan.FromSeconds(3)),
            "The designer did not reactivate after restoring its window.");

        var activityBlocker = new Window
        {
            Width = 1,
            Height = 1,
            Left = -10_000,
            Top = -10_000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        activityBlocker.Show();
        activityBlocker.Activate();
        Require(
            PumpDispatcherUntil(() => !window.IsActive, TimeSpan.FromSeconds(3)),
            "The designer did not become inactive for the activity gate.");
        var beforeInactive = window.Diagnostics.Snapshot();
        PumpDispatcherFor(ActivityObservationWindow);
        var inactiveActivity = window.Diagnostics.Snapshot() - beforeInactive;
        Require(inactiveActivity.TimerTicks == 0, "An inactive designer must stop its preview timer.");
        Require(inactiveActivity.RenderAttempts == 0, "An inactive designer must not attempt preview renders.");
        Require(inactiveActivity.FramesPresented == 0, "An inactive designer must not present preview frames.");
        Require(inactiveActivity.ScheduledPreviewDispatches == 0,
            "An inactive designer must not dispatch queued preview work.");
        PrintActivity("Inactive window", inactiveActivity, ActivityObservationWindow);

        var beforeReactivation = window.Diagnostics.Snapshot();
        activityBlocker.Close();
        window.Activate();
        Require(
            PumpDispatcherUntil(
                () => window.Diagnostics.Snapshot().ScheduledPreviewDispatches -
                      beforeReactivation.ScheduledPreviewDispatches >= 1,
                TimeSpan.FromSeconds(3)),
            "The designer did not schedule a current frame after reactivation.");
        var reactivationActivity = window.Diagnostics.Snapshot() - beforeReactivation;
        Require(reactivationActivity.ScheduledPreviewOperations == 1,
            "Reactivation must schedule exactly one current preview operation.");
        Require(reactivationActivity.ScheduledPreviewDispatches == 1,
            "Reactivation must dispatch exactly one current preview operation.");

        designerViewModel.PerformanceMode = false;
        PumpDispatcherFor(TimeSpan.FromMilliseconds(150));

        const int burstSize = 8;
        var beforeBurst = window.Diagnostics.Snapshot();
        for (var index = 0; index < burstSize; index++)
        {
            designerViewModel.RequestPreview();
        }
        PumpDispatcherUntil(
            () => window.Diagnostics.Snapshot().ScheduledPreviewDispatches -
                  beforeBurst.ScheduledPreviewDispatches >= 1,
            TimeSpan.FromSeconds(5));
        var burstActivity = window.Diagnostics.Snapshot() - beforeBurst;
        Require(burstActivity.PreviewRequests == burstSize,
            "Every explicit preview request must remain observable.");
        Require(burstActivity.ScheduledPreviewOperations == 1,
            "A synchronous preview-request burst must schedule one dispatcher operation.");
        Require(burstActivity.ScheduledPreviewDispatches == 1,
            "A synchronous preview-request burst must execute one dispatcher operation.");
        Console.WriteLine();
        Console.WriteLine($"Preview-request burst ({burstSize} requests; current scheduling behaviour)");
        Console.WriteLine($"  Requests observed:          {burstActivity.PreviewRequests,8:N0}");
        Console.WriteLine($"  Operations scheduled:       {burstActivity.ScheduledPreviewOperations,8:N0}");
        Console.WriteLine($"  Dispatches executed:        {burstActivity.ScheduledPreviewDispatches,8:N0}");
        Console.WriteLine($"  Frames presented:           {burstActivity.FramesPresented,8:N0}");
        Console.WriteLine($"  Busy renders skipped:       {burstActivity.RenderSkipsBusy,8:N0}");

        var material = designerViewModel.CreateRenderMaterial();
        var options = designerViewModel.CreatePreviewOptions();
        window.Close();
        var atClose = window.Diagnostics.Snapshot();
        Require(
            atClose.RendererDisposals == atClose.RendererInitializationsCompleted,
            "Every successfully initialised window renderer must be disposed when the window closes.");
        PumpDispatcherFor(TimeSpan.FromMilliseconds(200));
        var afterCloseActivity = window.Diagnostics.Snapshot() - atClose;
        Require(afterCloseActivity.TimerTicks == 0, "The preview timer must stop after the window closes.");
        Require(afterCloseActivity.ScheduledPreviewDispatches == 0,
            "No queued preview operation may dispatch after the window closes.");
        Require(afterCloseActivity.FramesPresented == 0, "No preview frame may be presented after close.");
        Console.WriteLine();
        Console.WriteLine("Lifecycle gates");
        Console.WriteLine($"  Renderer initialisations:   {atClose.RendererInitializationsCompleted,8:N0}");
        Console.WriteLine($"  Renderer disposals:         {atClose.RendererDisposals,8:N0}");
        Console.WriteLine($"  Frames after close:         {afterCloseActivity.FramesPresented,8:N0}");
        Console.WriteLine($"  Timer ticks after close:    {afterCloseActivity.TimerTicks,8:N0}");

        MeasureRenderer(material, options);
        application.Shutdown();
    }

    private static void MeasureRenderer(PlanetRenderMaterial material, PlanetPreviewOptions options)
    {
        Console.WriteLine();
        Console.WriteLine("Renderer and frame-copy observations at 960x540");
        ForceFullCollection();

        var constructionStarted = Stopwatch.GetTimestamp();
        var renderer = new PlanetPreviewRenderer(960, 540);
        var constructionMilliseconds = ElapsedMilliseconds(constructionStarted);

        var firstAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var firstFrameStarted = Stopwatch.GetTimestamp();
        var firstFrame = renderer.Render(material, options);
        var firstFrameMilliseconds = ElapsedMilliseconds(firstFrameStarted);
        var firstFrameAllocated = GC.GetAllocatedBytesForCurrentThread() - firstAllocationStart;
        Require(firstFrame.BgraPixels.Length == 960 * 540 * 4, "The first frame must contain complete BGRA pixels.");

        renderer.Render(material, options, 1);
        ForceFullCollection();
        var independentSamples = new double[SteadyFrameIterations];
        var independentAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < SteadyFrameIterations; index++)
        {
            var started = Stopwatch.GetTimestamp();
            renderer.Render(material, options, index + 2);
            independentSamples[index] = ElapsedMilliseconds(started);
        }
        var independentAllocated = GC.GetAllocatedBytesForCurrentThread() - independentAllocationStart;
        Array.Sort(independentSamples);

        var reusablePixels = new byte[960 * 540 * 4];
        var reusableFrame = renderer.Render(material, options, reusablePixels, 1);
        Require(ReferenceEquals(reusablePixels, reusableFrame.BgraPixels),
            "The reusable renderer path must return the caller-owned pixel buffer.");
        ForceFullCollection();
        var reusableSamples = new double[SteadyFrameIterations];
        var reusableAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < SteadyFrameIterations; index++)
        {
            var started = Stopwatch.GetTimestamp();
            renderer.Render(material, options, reusablePixels, index + 2);
            reusableSamples[index] = ElapsedMilliseconds(started);
        }
        var reusableAllocated = GC.GetAllocatedBytesForCurrentThread() - reusableAllocationStart;
        Array.Sort(reusableSamples);

        var disposalStarted = Stopwatch.GetTimestamp();
        renderer.Dispose();
        var disposalMilliseconds = ElapsedMilliseconds(disposalStarted);

        Console.WriteLine($"  Renderer construction:      {constructionMilliseconds,8:N2} ms");
        Console.WriteLine($"  First frame:                {firstFrameMilliseconds,8:N2} ms");
        Console.WriteLine($"  First-frame allocation:     {firstFrameAllocated / 1024d,8:N1} KiB");
        Console.WriteLine($"  Independent median:         {Percentile(independentSamples, 0.50),8:N2} ms/frame");
        Console.WriteLine($"  Independent p95:            {Percentile(independentSamples, 0.95),8:N2} ms/frame");
        Console.WriteLine($"  Independent allocation:     {independentAllocated / (double)SteadyFrameIterations / 1024,8:N1} KiB/frame");
        Console.WriteLine($"  Reusable-buffer median:      {Percentile(reusableSamples, 0.50),8:N2} ms/frame");
        Console.WriteLine($"  Reusable-buffer p95:         {Percentile(reusableSamples, 0.95),8:N2} ms/frame");
        Console.WriteLine($"  Reusable-buffer allocation:  {reusableAllocated / (double)SteadyFrameIterations / 1024,8:N1} KiB/frame");
        Console.WriteLine($"  Renderer disposal:          {disposalMilliseconds,8:N2} ms");
    }

    private static void PrintActivity(
        string label,
        PlanetDesignerWindowDiagnosticSnapshot activity,
        TimeSpan observationWindow)
    {
        Console.WriteLine();
        Console.WriteLine($"{label} activity over {observationWindow.TotalMilliseconds:N0} ms (observational)");
        Console.WriteLine($"  Timer ticks:                {activity.TimerTicks,8:N0}");
        Console.WriteLine($"  Render attempts:            {activity.RenderAttempts,8:N0}");
        Console.WriteLine($"  Frames presented:           {activity.FramesPresented,8:N0}");
        Console.WriteLine($"  Frames/second equivalent:   {activity.FramesPresented / observationWindow.TotalSeconds,8:N1}");
    }

    private static bool PumpDispatcherUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition())
        {
            return true;
        }

        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) =>
        {
            if (!condition() && stopwatch.Elapsed < timeout)
            {
                return;
            }

            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return condition();
    }

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
