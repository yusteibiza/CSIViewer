namespace CSIViewer.Protocol;

/// <summary>
/// Tracks keys and mouse buttons that were pressed by the remote controller.
/// If a session is lost, <see cref="GetReleaseEvents"/> returns the synthetic
/// release events needed to avoid leaving the host with stuck input.
/// </summary>
public sealed class PressedInputState
{
    private readonly object _gate = new();
    private readonly Dictionary<ushort, InputEventPayload> _pressedKeys = new();
    private readonly HashSet<string> _pressedButtons = new(StringComparer.Ordinal);

    public int PressedKeyCount
    {
        get
        {
            lock (_gate)
            {
                return _pressedKeys.Count;
            }
        }
    }

    public int PressedButtonCount
    {
        get
        {
            lock (_gate)
            {
                return _pressedButtons.Count;
            }
        }
    }

    public void Observe(InputEventPayload input)
    {
        if (input is null)
        {
            return;
        }

        lock (_gate)
        {
            if (string.Equals(input.Kind, "key", StringComparison.Ordinal))
            {
                var scanCode = (ushort)Math.Clamp(input.ScanCode, 0, ushort.MaxValue);
                if (input.Down)
                {
                    _pressedKeys[scanCode] = input;
                }
                else
                {
                    _pressedKeys.Remove(scanCode);
                }

                return;
            }

            if (!string.Equals(input.Kind, "mouse", StringComparison.Ordinal))
            {
                return;
            }

            switch (input.Action)
            {
                case "leftDown":
                    _pressedButtons.Add("left");
                    break;
                case "leftUp":
                    _pressedButtons.Remove("left");
                    break;
                case "rightDown":
                    _pressedButtons.Add("right");
                    break;
                case "rightUp":
                    _pressedButtons.Remove("right");
                    break;
                case "middleDown":
                    _pressedButtons.Add("middle");
                    break;
                case "middleUp":
                    _pressedButtons.Remove("middle");
                    break;
            }
        }
    }

    public IReadOnlyList<InputEventPayload> GetReleaseEvents()
    {
        lock (_gate)
        {
            if (_pressedKeys.Count == 0 && _pressedButtons.Count == 0)
            {
                return Array.Empty<InputEventPayload>();
            }

            var events = new List<InputEventPayload>(_pressedKeys.Count + _pressedButtons.Count);
            foreach (var pressed in _pressedKeys.Values)
            {
                events.Add(pressed with
                {
                    Action = "keyUp",
                    Down = false,
                    HasPosition = false,
                    MonitorIndex = 0
                });
            }

            foreach (var button in _pressedButtons)
            {
                events.Add(new InputEventPayload(
                    Kind: "mouse",
                    Action: button + "Up",
                    X: 0,
                    Y: 0,
                    Button: 0,
                    Delta: 0,
                    KeyCode: 0,
                    ScanCode: 0,
                    Down: false,
                    MonitorIndex: 0,
                    HasPosition: false));
            }

            _pressedKeys.Clear();
            _pressedButtons.Clear();
            return events;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pressedKeys.Clear();
            _pressedButtons.Clear();
        }
    }
}
