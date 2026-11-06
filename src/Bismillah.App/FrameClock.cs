using System.Diagnostics;
using System.Windows.Media;

namespace Bismillah;

/// <summary>Calls back once per screen refresh with the seconds elapsed since it started.</summary>
internal sealed class FrameClock : IDisposable
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly Action<double> _tick;
    private bool _running = true;

    public FrameClock(Action<double> tick)
    {
        _tick = tick;
        CompositionTarget.Rendering += OnRendering;
    }

    public double Elapsed => _watch.Elapsed.TotalSeconds;

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_running) _tick(_watch.Elapsed.TotalSeconds);
    }

    public void Dispose()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }
}
