using System.Text;
using CSIViewer.Core;
using CSIViewer.Protocol;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private const int MaxClipboardBytes = 1024 * 1024;
    private const string ClipboardOrigin = "CSIViewer.RemoteText.v1";
    private static readonly UTF8Encoding ClipboardEncoding = new(false, true);
    private bool _clipboardWatching; // UI thread only
    private int _clipboardReadVersion;
    private int _clipboardReadRunning;
    private int _clipboardReadDirty;
    private long _audioPacketsReceived;
    private long _clipboardUpdatesReceived;
    public long AudioPacketsReceived => Interlocked.Read(ref _audioPacketsReceived);
    public long ClipboardUpdatesReceived => Interlocked.Read(ref _clipboardUpdatesReceived);
    private readonly SemaphoreSlim _featuresGate = new(1, 1);
    private sealed record FeaturesPayload(bool Audio, bool Clipboard);

    public bool IsAudioEnabled => GetPrimarySession()?.AudioEnabled == true;
    public bool IsClipboardEnabled => GetPrimarySession()?.ClipboardEnabled == true;

    /// <summary>Opt in for the current controller session only; never persists clipboard contents.</summary>
    public async Task ConfigureSessionFeaturesAsync(bool audio, bool clipboard, CancellationToken cancellationToken = default)
    {
        await _featuresGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = GetPrimarySession();
            if (session is null || !session.IsController || session.State != ConnectionState.Connected) return;
            audio &= session.Permissions.HasFlag(SessionPermissions.Audio);
            clipboard &= session.Permissions.HasFlag(SessionPermissions.Clipboard);
            SetSessionFeatures(session, audio, clipboard);
            var sent = await SendEncryptedWithResultAsync(session, SessionFrameKind.Control,
                ControlJson.Create("session.features", new FeaturesPayload(audio, clipboard)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!sent)
            {
                SetSessionFeatures(session, false, false);
                throw new IOException(Localization.Text("No se pudieron activar el audio y el portapapeles."));
            }
        }
        finally { _featuresGate.Release(); }
    }

    private void SetSessionFeatures(RemoteSession session, bool audio, bool clipboard)
    {
        session.ClipboardEnabled = clipboard && session.Permissions.HasFlag(SessionPermissions.Clipboard);
        if (!session.ClipboardEnabled) Interlocked.Exchange(ref session.PendingClipboardText, null);
        session.AudioEnabled = audio && session.Permissions.HasFlag(SessionPermissions.Audio);
        if (!session.AudioEnabled) Interlocked.Exchange(ref session.AudioStream, null)?.Dispose();
        if (session.AudioEnabled && session.AudioStream is null)
        {
            var stream = new SystemAudioStream();
            session.AudioStream = stream;
            if (!session.IsController)
            {
                try
                {
                    stream.StartCapture(async (packet, _) =>
                    {
                        if (session.AudioEnabled && _sessions.ContainsKey(session.Id))
                            // Disabling audio must not cancel a WebSocket send and abort all sessions.
                            await SendEncryptedAsync(session, SessionFrameKind.Audio, packet, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    }, ex => FeatureAudioFailed(session, ex));
                }
                catch (Exception ex) { FeatureAudioFailed(session, ex); }
            }
        }
        RefreshClipboardWatcher();
    }

    private void FeatureAudioFailed(RemoteSession session, Exception ex)
    {
        session.AudioEnabled = false;
        // Avoid disposing a capture synchronously from its own RecordingStopped callback.
        var failedStream = Interlocked.Exchange(ref session.AudioStream, null);
        _ = Task.Run(() => failedStream?.Dispose());
        _logger.LogWarning("System audio unavailable ({Type})", ex.GetType().Name);
        _state.Post(() => _state.LastError = Localization.Text("No se pudo abrir el audio del sistema. Comprueba el dispositivo de sonido."));
        if (!session.IsController && _sessions.ContainsKey(session.Id))
            _ = SendHostControlAsync(session, "session.audio_unavailable", new { });
    }

    private bool TryHandleFeaturesControl(ControlEnvelope envelope, RemoteSession session)
    {
        if (envelope.Type == "session.features")
        {
            if (!session.IsController && session.State == ConnectionState.Connected)
            {
                var options = ControlJson.Payload<FeaturesPayload>(envelope);
                if (options is not null) SetSessionFeatures(session, options.Audio, options.Clipboard);
            }
            return true;
        }
        if (envelope.Type == "session.audio_unavailable")
        {
            if (session.IsController && session.AudioEnabled)
                FeatureAudioFailed(session, new InvalidOperationException());
            return true;
        }
        return false;
    }

    private bool HandleFeatureFrame(RemoteSession session, SessionFrameKind kind, byte[] payload)
    {
        if (kind == SessionFrameKind.Audio)
        {
            if (session.IsController && session.AudioEnabled && session.Permissions.HasFlag(SessionPermissions.Audio))
            {
                try
                {
                    if (AudioPacket.TryRead(payload, out _, out _, out _, out _))
                    {
                        session.AudioStream?.Play(payload);
                        Interlocked.Increment(ref _audioPacketsReceived);
                    }
                }
                catch (Exception ex) { FeatureAudioFailed(session, ex); }
            }
            return true;
        }
        if (kind != SessionFrameKind.Clipboard) return false;
        if (!session.ClipboardEnabled || !session.Permissions.HasFlag(SessionPermissions.Clipboard) || payload.Length > MaxClipboardBytes) return true;
        string text;
        try { text = ClipboardEncoding.GetString(payload); }
        catch (DecoderFallbackException) { return true; }
        // Keep at most one pending UI apply per session. A peer flooding text
        // updates must not create an unbounded dispatcher queue.
        Interlocked.Exchange(ref session.PendingClipboardText, text);
        QueueClipboardApply(session);
        return true;
    }

    private void QueueClipboardApply(RemoteSession session)
    {
        if (Interlocked.CompareExchange(ref session.ClipboardApplyQueued, 1, 0) != 0) return;
        _state.Post(() => ApplyPendingClipboard(session));
    }

    private void ApplyPendingClipboard(RemoteSession session)
    {
        var text = Interlocked.Exchange(ref session.PendingClipboardText, null);
        try
        {
            if (text is null || !session.ClipboardEnabled || !_sessions.ContainsKey(session.Id)) return;
            var package = new DataPackage();
            package.SetText(text);
            // Preserve this marker on the clipboard to suppress echoes even
            // when ContentChanged notifications are delayed or repeated.
            package.SetData(ClipboardOrigin, "remote");
            if (Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(package,
                new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }))
                Interlocked.Increment(ref _clipboardUpdatesReceived);
        }
        catch (Exception) { /* Clipboard can be temporarily owned by another application. */ }
        finally
        {
            Volatile.Write(ref session.ClipboardApplyQueued, 0);
            if (session.ClipboardEnabled && Volatile.Read(ref session.PendingClipboardText) is not null)
                QueueClipboardApply(session);
        }
    }

    private void RefreshClipboardWatcher() => _state.Post(() =>
    {
        var needed = Volatile.Read(ref _disposed) == 0 && _sessions.Values.Any(x => x.ClipboardEnabled);
        if (needed == _clipboardWatching) return;
        if (needed) Windows.ApplicationModel.DataTransfer.Clipboard.ContentChanged += OnClipboardContentChanged;
        else Windows.ApplicationModel.DataTransfer.Clipboard.ContentChanged -= OnClipboardContentChanged;
        _clipboardWatching = needed;
        Interlocked.Increment(ref _clipboardReadVersion);
        Volatile.Write(ref _clipboardReadDirty, 0);
    });

    private async void OnClipboardContentChanged(object? sender, object e)
    {
        Interlocked.Increment(ref _clipboardReadVersion);
        Volatile.Write(ref _clipboardReadDirty, 1);
        if (Interlocked.CompareExchange(ref _clipboardReadRunning, 1, 0) != 0) return;
        Volatile.Write(ref _clipboardReadDirty, 0);
        try
        {
            var version = Volatile.Read(ref _clipboardReadVersion);
            var targets = _sessions.Values.Where(x => x.ClipboardEnabled && x.Permissions.HasFlag(SessionPermissions.Clipboard)).ToArray();
            if (targets.Length == 0) return;
            var view = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (view.Contains(ClipboardOrigin) || !view.Contains(StandardDataFormats.Text)) return;
            var text = await view.GetTextAsync();
            if (version != Volatile.Read(ref _clipboardReadVersion) || text.Length > MaxClipboardBytes ||
                ClipboardEncoding.GetByteCount(text) > MaxClipboardBytes) return;
            var payload = ClipboardEncoding.GetBytes(text);
            foreach (var session in targets)
                if (session.ClipboardEnabled && _sessions.ContainsKey(session.Id))
                    await SendEncryptedAsync(session, SessionFrameKind.Clipboard, payload);
        }
        catch (Exception) { /* Never log clipboard contents or retain them in history. */ }
        finally
        {
            Volatile.Write(ref _clipboardReadRunning, 0);
            if (Volatile.Read(ref _clipboardReadDirty) != 0 && _clipboardWatching)
                OnClipboardContentChanged(null, EventArgs.Empty);
        }
    }
}
