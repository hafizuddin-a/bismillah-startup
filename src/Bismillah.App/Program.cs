using System.Windows;

namespace Bismillah;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static int Main(string[] args)
    {
        // started by the old version to finish an update: no UI at all
        if (args.Length >= 4 && args[0] == "--apply-update")
            return Updater.ApplyUpdate(int.Parse(args[1]), args[2], args[3]);

        // CI helpers: draw frames to PNG files / put the real windows on screen, then exit
        if (args.Length >= 2 && args[0] == "--render-frames") return HeadlessRender.Run(args[1]);
        if (args.Length >= 2 && args[0] == "--render-gif-frames") return DemoScene.Run(args[1]);
        if (args.Length >= 2 && args[0] == "--smoke") return HeadlessRender.Smoke(args[1]);
        if (args.Length >= 2 && args[0] == "--smoke-host") return HeadlessRender.SmokeHost(args[1]);

        // only one copy at a time (an update's relaunch waits a moment for the old copy to release this)
        _singleInstance = new Mutex(true, @"Local\Bismillah.SingleInstance", out bool first);
        if (!first && !args.Contains("--after-update")) return 0;
        if (!first) { _singleInstance.Dispose(); _singleInstance = new Mutex(true, @"Local\Bismillah.SingleInstance", out first); if (!first) return 0; }

        Log.Write($"started {Updater.CurrentVersion} ({string.Join(' ', args)}) from {Environment.ProcessPath}; PC up {TimeSpan.FromMilliseconds(Environment.TickCount64):h\\:mm\\:ss}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("crashed: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("background task failed: " + e.Exception); e.SetObserved(); };

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log.Write("error: " + e.Exception); e.Handled = true; };      // keep running; the log says why
        AppHost? host = null;
        app.Startup += (_, _) => host = new AppHost(() => app.Dispatcher.BeginInvoke(() => app.Shutdown()), args.Contains("--after-update"), args.Contains("--login"));
        app.Exit += (_, _) => host?.Dispose();
        return app.Run();
    }
}
