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
        if (args.Length >= 2 && args[0] == "--smoke") return HeadlessRender.Smoke(args[1]);
        if (args.Length >= 2 && args[0] == "--smoke-host") return HeadlessRender.SmokeHost(args[1]);

        // only one copy at a time (an update's relaunch waits a moment for the old copy to release this)
        _singleInstance = new Mutex(true, @"Local\Bismillah.SingleInstance", out bool first);
        if (!first && !args.Contains("--after-update")) return 0;
        if (!first) { _singleInstance.Dispose(); _singleInstance = new Mutex(true, @"Local\Bismillah.SingleInstance", out first); if (!first) return 0; }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppHost? host = null;
        app.Startup += (_, _) => host = new AppHost(() => app.Dispatcher.BeginInvoke(() => app.Shutdown()), args.Contains("--after-update"));
        app.Exit += (_, _) => host?.Dispose();
        return app.Run();
    }
}
