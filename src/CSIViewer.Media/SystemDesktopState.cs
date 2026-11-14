using CSIViewer.Protocol;

namespace CSIViewer.Media;

/// <summary>Prevents input into a desktop for which this endpoint has no recent successful capture.</summary>
public sealed class SystemDesktopState
{
    private sealed record Snapshot(string Name, long Time);
    private Snapshot? _latest;
    private readonly Func<long> _clock;
    public SystemDesktopState() : this(() => Environment.TickCount64) { }
    internal SystemDesktopState(Func<long> clock) => _clock = clock;
    internal void Captured(string name) => Volatile.Write(ref _latest, new Snapshot(name, _clock()));
    internal bool CanInput(string name)
    {
        var frame = Volatile.Read(ref _latest);
        if (frame is null || name.Length == 0 || !string.Equals(frame.Name, name, StringComparison.Ordinal)) return false;
        var elapsed = _clock() - frame.Time;
        return elapsed is >= 0 and < 3000;
    }

    internal static InputEventPayload? CleanupOnly(InputEventPayload input) => input.Kind switch
    {
        "key" when !input.Down && input.Action is "up" or "keyUp" => input,
        "mouse" when input.Action is "leftUp" or "rightUp" or "middleUp" => input with { HasPosition = false, X = 0, Y = 0 },
        _ => null
    };
}
