using Microsoft.Extensions.Logging;
using CSIViewer.Core;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Networking;
using CSIViewer.Protocol;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private readonly ConnectionHealthMonitor _signalingHealth = new();
    private readonly object _heartbeatGate = new();
    private Task? _heartbeatTask;
    private CancellationTokenSource? _heartbeatCts;
    private DateTimeOffset _lastSignalingPingUtc = DateTimeOffset.MinValue;
    private volatile bool _disconnectedNotified;

    private void StartHeartbeatLoop()
    {
        lock (_heartbeatGate)
        {
            if (_heartbeatTask is { IsCompleted: false })
            {
                return;
            }

            _heartbeatCts?.Dispose();
            _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_stopping)
                {
                    return;
                }

                try
                {
                    await HeartbeatTickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // A single failed tick must not stop the watchdog for the
                    // rest of the application's lifetime.
                    _logger.LogError(LogEvents.BackgroundTaskFailed, ex, "Heartbeat tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(LogEvents.BackgroundTaskFailed, ex, "Heartbeat loop failed");
        }
    }

    private async Task HeartbeatTickAsync(CancellationToken cancellationToken)
    {
        var settings = _state.Settings;
        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.HeartbeatIntervalSeconds, 2, 120));
        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.HeartbeatTimeoutSeconds, 5, 600));
        var now = DateTimeOffset.UtcNow;

        if (IsServerOnline)
        {
            if (_signalingHealth.IsStale(now, timeout))
            {
                _logger.LogWarning(
                    "Signaling heartbeat timeout: no data for {Elapsed}",
                    _signalingHealth.TimeSinceLastActivity(now));
                await ForceSignalingReconnectAsync(
                        global::CSIViewer.Core.Localization.Text("El servidor dejó de responder (heartbeat)."))
                    .ConfigureAwait(false);
                return;
            }

            if (now - _lastSignalingPingUtc >= interval)
            {
                _lastSignalingPingUtc = now;
                await SendSignalingPingAsync(now, cancellationToken).ConfigureAwait(false);
            }
        }

        var duePings = new List<Task>();
        foreach (var session in _sessions.Values.ToArray())
        {
            if (session.Health.IsStale(now, timeout))
            {
                _logger.LogWarning(
                    "Session {SessionId} heartbeat timeout after {Elapsed}",
                    session.SessionId,
                    session.Health.TimeSinceLastActivity(now));

                if (session.ControlOwner)
                {
                    _input.ReleaseAll();
                }

                var finalState = _state.IsServerOnline
                    ? ConnectionState.Connected
                    : _state.Settings.AutoConnect
                        ? ConnectionState.Reconnecting
                        : ConnectionState.Failed;

                await EndRemoteSessionAsync(
                        session,
                        global::CSIViewer.Core.Localization.Text("El equipo remoto dejó de responder (heartbeat)."),
                        sendToPeer: _state.IsServerOnline,
                        finalState)
                    .ConfigureAwait(false);

                if (_state.Settings.NotificationsEnabled)
                {
                    _notifications.Show(
                        global::CSIViewer.Core.Localization.Text("Sesión finalizada"),
                        global::CSIViewer.Core.Localization.Text("El equipo remoto dejó de responder."));
                }

                continue;
            }

            if (now - session.LastHeartbeatSentUtc >= interval)
            {
                session.LastHeartbeatSentUtc = now;
                duePings.Add(SendSessionPingAsync(session, now, cancellationToken));
            }
        }

        if (duePings.Count > 0)
        {
            // Send concurrently so one slow session cannot delay the rest.
            await Task.WhenAll(duePings).ConfigureAwait(false);
        }
    }

    private async Task SendSignalingPingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        SignalingClient? signal;
        lock (_connectionLock)
        {
            signal = _signal;
        }

        if (signal is null || !signal.IsConnected)
        {
            return;
        }

        try
        {
            using var timeout = CreateTimeoutToken(
                TimeSpan.FromSeconds(5),
                cancellationToken);
            await signal.SendAsync(
                    ProtocolJson.Create(
                        MessageTypes.Ping,
                        new PingPayload(now.ToUnixTimeMilliseconds())),
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not send signaling heartbeat");
        }
    }

    private async Task SendSessionPingAsync(
        RemoteSession session,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var payload = new PingPayload(now.ToUnixTimeMilliseconds());
        try
        {
            if (session.IsController)
            {
                await SendControlAsync("session.ping", payload, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SendHostControlAsync(session, "session.ping", payload)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not send session heartbeat to {SessionId}", session.SessionId);
        }
    }

    private async Task ForceSignalingReconnectAsync(string reason)
    {
        SignalingClient? signal;
        lock (_connectionLock)
        {
            signal = _signal;
        }

        if (signal is null)
        {
            return;
        }

        await HandleServerConnectionClosedAsync(signal, reason).ConfigureAwait(false);
    }

    private async Task StopHeartbeatLoopAsync()
    {
        Task? task;
        CancellationTokenSource? cts;
        lock (_heartbeatGate)
        {
            task = _heartbeatTask;
            _heartbeatTask = null;
            cts = _heartbeatCts;
            _heartbeatCts = null;
        }

        cts?.Cancel();
        if (task is not null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch
            {
                // The loop is already stopping.
            }
        }

        cts?.Dispose();
    }
}
