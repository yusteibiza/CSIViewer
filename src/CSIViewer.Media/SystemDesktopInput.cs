using CSIViewer.Protocol;

namespace CSIViewer.Media;

public sealed class SystemDesktopInput : IHostInput, IDisposable
{
    private readonly SystemDesktopThread _thread = new();
    private readonly InputInjector _input = new();
    private readonly SystemDesktopState _state;
    public SystemDesktopInput(SystemDesktopState? state = null) => _state = state ?? new SystemDesktopState();

    public bool Send(InputEventPayload input, MonitorBounds? monitor = null) => Send(input, monitor, out _);

    public bool Send(InputEventPayload input, MonitorBounds? monitor, out InputInjectionDiagnostics? diagnostics)
    {
        diagnostics = null;
        try
        {
            var result = _thread.Invoke(changed =>
            {
                if (changed) _input.ReleaseAll();
                var effective = _state.CanInput(_thread.DesktopName) ? input : SystemDesktopState.CleanupOnly(input);
                if (effective is null) return (sent: false, data: (InputInjectionDiagnostics?)null);
                var currentBounds = monitor;
                if (monitor is { } previous)
                {
                    var active = MonitorService.GetMonitors().FirstOrDefault(x => x.X == previous.X && x.Y == previous.Y);
                    if (active is not null) currentBounds = new MonitorBounds(active.X, active.Y, active.Width, active.Height);
                }
                var sent = _input.Send(effective, currentBounds, out var data);
                return (sent, data);
            });
            diagnostics = result.data;
            return result.sent;
        }
        catch { return false; } // Denied desktop access never falls back to another desktop.
    }

    public int ReleaseAll()
    {
        try { return _thread.Invoke(_ => _input.ReleaseAll()); }
        catch { return 0; }
    }

    public void ClearPressedState() { try { _thread.Invoke(_ => { _input.ClearPressedState(); return true; }); } catch { } }
    public void Dispose() { ReleaseAll(); _thread.Dispose(); }
}
