using System.Diagnostics;
using Microsoft.Extensions.Logging;
using CSIViewer.Core;
using CSIViewer.Media;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private int _hostCaptureGeneration;
    private long _hostCaptureStartedTicks;
    private long _lastHostCaptureFrameTicks;
    private long _hostVisualDemandTicks;
    private volatile bool _usingModernCapture;

    /// <summary>Minimizing/hiding the host changes the desktop, not the session lifetime.</summary>
    public void NotifyHostWindowVisibilityChanged() => MarkHostVisualChange();

    private void MarkHostVisualChange()
    {
        if (!_usingModernCapture || Volatile.Read(ref _captureViewerCount) == 0) return;
        var frame = Volatile.Read(ref _lastHostCaptureFrameTicks);
        var demand = Volatile.Read(ref _hostVisualDemandTicks);
        // Keep the FIRST outstanding change. Continuous input must not keep
        // postponing the timeout when the capture has stopped responding.
        if (demand == 0 || demand <= frame)
            Interlocked.CompareExchange(ref _hostVisualDemandTicks, Stopwatch.GetTimestamp(), demand);
    }

    private void OnHostCaptureFrame(CaptureFrame frame, int generation)
    {
        if (generation != Volatile.Read(ref _hostCaptureGeneration) || Volatile.Read(ref _captureViewerCount) == 0) return;
        Volatile.Write(ref _lastHostCaptureFrameTicks, Stopwatch.GetTimestamp());
        _ = SendVideoFrameAsync(frame);
    }

    private void CheckHostCaptureProgress()
    {
        if (!_captureRunning || !_usingModernCapture || _systemLoginDesktop) return;
        var now = Stopwatch.GetTimestamp();
        var started = Volatile.Read(ref _hostCaptureStartedTicks);
        var frame = Volatile.Read(ref _lastHostCaptureFrameTicks);
        var demand = Volatile.Read(ref _hostVisualDemandTicks);
        var missingFirstFrame = frame == 0 && started != 0 && Stopwatch.GetElapsedTime(started, now) >= TimeSpan.FromSeconds(8);
        // WGC may legitimately be quiet on an unchanged desktop. Only treat
        // silence as a stall after a host visibility change or real input.
        var stalledAfterChange = demand > frame && demand != 0 &&
            Stopwatch.GetElapsedTime(demand, now) >= TimeSpan.FromSeconds(4) &&
            (frame == 0 || Stopwatch.GetElapsedTime(frame, now) >= TimeSpan.FromSeconds(4));
        if (missingFirstFrame || stalledAfterChange)
            ScheduleModernCaptureFallback(_activeCaptureMonitor?.Index ?? 0,
                Volatile.Read(ref _hostCaptureGeneration), "No completed capture frames after a desktop change/startup", null);
    }

    private void ScheduleModernCaptureFallback(int monitorIndex, int generation, string reason, Exception? error)
    {
        if (generation != Volatile.Read(ref _hostCaptureGeneration) || !_usingModernCapture ||
            Volatile.Read(ref _captureViewerCount) == 0 ||
            Interlocked.CompareExchange(ref _modernCaptureFallbackScheduled, 1, 0) != 0) return;
        _modernCaptureDisabled = true;
        _logger.LogWarning(error, "Switching Windows.Graphics.Capture to GDI. Reason={Reason} Generation={Generation}", reason, generation);
        _state.Post(() => _state.LastError = Localization.Text("Captura moderna sin respuesta; recuperando la imagen con GDI."));
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, _lifetimeCts.Token).ConfigureAwait(false);
                if (generation == Volatile.Read(ref _hostCaptureGeneration) &&
                    Volatile.Read(ref _captureViewerCount) > 0)
                    await StartHostCaptureAsync(monitorIndex, expectedGeneration: generation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "Capture recovery failed"); }
            finally { Volatile.Write(ref _modernCaptureFallbackScheduled, 0); }
        });
    }
}
