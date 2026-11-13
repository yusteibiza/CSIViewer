using CSIViewer.Core;

namespace CSIViewer.Services;

/// <summary>
/// Thread-safe admission and control-lease manager for host sessions.
/// Multiple controllers may view the desktop, but only one may own input.
/// </summary>
public sealed class SessionManager
{
    public sealed record Entry(
        Guid SessionId,
        string ControllerId,
        SessionPermissions RequestedPermissions,
        DateTimeOffset CreatedUtc);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _pending = new();
    private readonly Dictionary<Guid, Entry> _active = new();
    private Guid? _controlOwner;

    public int MaxSessions { get; set; } = 10;

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _active.Count;
            }
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public Guid? ControlOwner
    {
        get
        {
            lock (_gate)
            {
                return _controlOwner;
            }
        }
    }

    public bool TryAddPending(
        Guid sessionId,
        string controllerId,
        SessionPermissions requestedPermissions,
        out string? reason)
    {
        requestedPermissions = SessionPermissionPolicy.Sanitize(requestedPermissions);
        lock (_gate)
        {
            if (_pending.ContainsKey(sessionId) || _active.ContainsKey(sessionId))
            {
                reason = "duplicate_session";
                return false;
            }

            if (_pending.Values.Any(x => x.ControllerId == controllerId) ||
                _active.Values.Any(x => x.ControllerId == controllerId))
            {
                reason = "controller_busy";
                return false;
            }

            if (_pending.Count + _active.Count >= Math.Max(1, MaxSessions))
            {
                reason = "host_full";
                return false;
            }

            _pending[sessionId] = new Entry(
                sessionId,
                controllerId,
                requestedPermissions,
                DateTimeOffset.UtcNow);
            reason = null;
            return true;
        }
    }

    public bool TryActivate(
        Guid sessionId,
        SessionPermissions requestedPermissions,
        out SessionPermissions grantedPermissions,
        out bool controlOwner,
        out string? reason)
    {
        requestedPermissions = SessionPermissionPolicy.Sanitize(requestedPermissions);
        lock (_gate)
        {
            if (!_pending.Remove(sessionId, out var pending))
            {
                grantedPermissions = SessionPermissions.None;
                controlOwner = false;
                reason = "not_pending";
                return false;
            }

            grantedPermissions = requestedPermissions;
            reason = null;
            controlOwner = false;
            if (requestedPermissions.HasFlag(SessionPermissions.ControlInput))
            {
                if (_controlOwner is null)
                {
                    _controlOwner = sessionId;
                    controlOwner = true;
                }
                else
                {
                    grantedPermissions &= ~SessionPermissions.ControlInput;
                    reason = "control_in_use";
                }
            }

            _active[sessionId] = pending with
            {
                RequestedPermissions = grantedPermissions
            };
            return true;
        }
    }

    public bool TryGetPending(Guid sessionId, out Entry entry)
    {
        lock (_gate)
        {
            return _pending.TryGetValue(sessionId, out entry!);
        }
    }

    public bool TryGetActive(Guid sessionId, out Entry entry)
    {
        lock (_gate)
        {
            return _active.TryGetValue(sessionId, out entry!);
        }
    }

    public bool TryRemove(Guid sessionId, out bool wasControlOwner, out int remaining)
    {
        lock (_gate)
        {
            wasControlOwner = _controlOwner == sessionId;
            if (wasControlOwner)
            {
                _controlOwner = null;
            }

            var removed = _pending.Remove(sessionId);
            removed |= _active.Remove(sessionId);
            remaining = _active.Count;
            return removed;
        }
    }

    public IReadOnlyList<Guid> ActiveSessionIds
    {
        get
        {
            lock (_gate)
            {
                return _active.Keys.ToArray();
            }
        }
    }

    public IReadOnlyList<Guid> PendingSessionIds
    {
        get
        {
            lock (_gate)
            {
                return _pending.Keys.ToArray();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            _active.Clear();
            _controlOwner = null;
        }
    }
}
