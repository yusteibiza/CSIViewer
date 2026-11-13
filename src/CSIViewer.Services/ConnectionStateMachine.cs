using CSIViewer.Core;

namespace CSIViewer.Services;

/// <summary>
/// Serializes connection-state transitions and rejects transitions that would
/// leave the session in an incoherent state.
/// </summary>
public sealed class ConnectionStateMachine
{
    private static readonly Dictionary<ConnectionState, ConnectionState[]> Allowed =
        new Dictionary<ConnectionState, ConnectionState[]>
        {
            [ConnectionState.Disconnected] =
            [
                ConnectionState.Connecting,
                ConnectionState.Reconnecting,
                ConnectionState.Disconnecting,
                ConnectionState.Failed
            ],
            [ConnectionState.Connecting] =
            [
                ConnectionState.Authenticating,
                ConnectionState.Connected,
                ConnectionState.Disconnecting,
                ConnectionState.Reconnecting,
                ConnectionState.Disconnected,
                ConnectionState.Failed
            ],
            [ConnectionState.Authenticating] =
            [
                ConnectionState.Connected,
                ConnectionState.Disconnecting,
                ConnectionState.Reconnecting,
                ConnectionState.Disconnected,
                ConnectionState.Failed
            ],
            [ConnectionState.Connected] =
            [
                ConnectionState.Connecting,
                ConnectionState.Disconnecting,
                ConnectionState.Reconnecting,
                ConnectionState.Disconnected,
                ConnectionState.Failed
            ],
            [ConnectionState.Reconnecting] =
            [
                ConnectionState.Connecting,
                ConnectionState.Authenticating,
                ConnectionState.Connected,
                ConnectionState.Disconnecting,
                ConnectionState.Disconnected,
                ConnectionState.Failed
            ],
            [ConnectionState.Disconnecting] =
            [
                ConnectionState.Connected,
                ConnectionState.Reconnecting,
                ConnectionState.Disconnected,
                ConnectionState.Failed
            ],
            [ConnectionState.Failed] =
            [
                ConnectionState.Connecting,
                ConnectionState.Reconnecting,
                ConnectionState.Connected,
                ConnectionState.Disconnecting,
                ConnectionState.Disconnected
            ]
        };

    private readonly object _gate = new();
    private ConnectionState _state = ConnectionState.Disconnected;

    public ConnectionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool TryTransition(ConnectionState next, out string? error)
    {
        lock (_gate)
        {
            if (_state == next)
            {
                error = null;
                return true;
            }

            if (Allowed[_state].Contains(next))
            {
                _state = next;
                error = null;
                return true;
            }

            error = global::CSIViewer.Core.Localization.Format("Transición de conexión no permitida: {0} → {1}.",_state,next);
            return false;
        }
    }
}
