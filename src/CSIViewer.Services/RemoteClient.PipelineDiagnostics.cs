using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CSIViewer.Services;

public sealed partial class RemoteClient
{
    private Task _pipelineTask = Task.CompletedTask;
    private int _uiProbePending;

    private static bool TryConsumePipelineDumpRequest(string path)
    {
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch (IOException) { return false; } // collector may still be creating the marker; retry next sample
        catch (UnauthorizedAccessException) { return false; }
    }

    private void StartPipelineDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("CSIVIEWER_PIPELINE_DIAGNOSTICS") != "1") return;
        PipelineTrace.Enable();
        _pipelineTask = Task.Run(() => PipelineDiagnosticsLoopAsync(_lifetimeCts.Token));
    }

    private async Task PipelineDiagnosticsLoopAsync(CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var name = Path.Combine(AppPaths.LogsDirectory, $"pipeline-{Environment.ProcessId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        var header = new
        {
            Type = "identity", Process = Path.GetFileName(Environment.ProcessPath), Pid = Environment.ProcessId,
            Build = typeof(RemoteClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(item => item.Key == "CSIViewerDiagnosticBuild")?.Value ?? "unlabelled",
            Version = typeof(RemoteClient).Assembly.GetName().Version?.ToString(),
            Frequency = Stopwatch.Frequency, OriginTicks = Stopwatch.GetTimestamp(), Utc = DateTimeOffset.UtcNow,
            RingCapacity = 8192, SampleSeconds = 2, MaxFileBytes = 16 * 1024 * 1024,
            Notes = "No pixels/text/keys/passwords. Monotonic clocks are local. CompositionObserved is not hardware display-present acknowledgement."
        };
        long lastDump = 0;
        long lastFaultCount = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Only this background task touches the file. Producers never wait on it.
                await using (var stream = new FileStream(name, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
                                 16 * 1024, FileOptions.Asynchronous))
                {
                    if (stream.Length == 0)
                        await WriteAsync(header).ConfigureAwait(false);
                    if (_state.UiInvoker is not null && Interlocked.CompareExchange(ref _uiProbePending, 1, 0) == 0)
                    {
                        PipelineTrace.Record(PipelineStage.UiProbeQueued);
                        _state.Post(() =>
                        {
                            PipelineTrace.Record(PipelineStage.UiProbeExecuted);
                            Volatile.Write(ref _uiProbePending, 0);
                        });
                    }
                    var snapshot = PipelineTrace.Recorder!.Snapshot(captureExpected: _captureRunning);
                    await WriteAsync(new
                    {
                        Type = "sample", Utc = DateTimeOffset.UtcNow, Snapshot = snapshot,
                        UiDispatcherAttached = _state.UiInvoker is not null, SignalingConnected = _signal?.IsConnected == true,
                        Sessions = _sessions.Values.Select(session => new
                        {
                            session.Id, session.IsController, session.State, session.Transport,
                            LastPeerActivityMs = session.Health.TimeSinceLastActivity(DateTimeOffset.UtcNow).TotalMilliseconds
                        }).ToArray(),
                        CaptureGeneration = Volatile.Read(ref _hostCaptureGeneration),
                        CaptureRunning = _captureRunning, ModernCapture = _usingModernCapture,
                        VideoSettings = new
                        {
                            _state.Settings.Fps, _state.Settings.MaxWidth, _state.Settings.ImageQuality,
                            _state.Settings.PreferModernCapture, _state.Settings.PreferH264
                        },
                        H264EncoderDisabled = _h264EncoderDisabled,
                        ViewerDecodeFallbackRequested = Volatile.Read(ref _videoDecodeFallback) != 0,
                        PendingVisualDemand = Volatile.Read(ref _hostVisualDemandTicks) > Volatile.Read(ref _lastHostCaptureFrameTicks)
                    }).ConfigureAwait(false);
                    var faults = snapshot.Counters.Where(counter => counter.Stage is PipelineStage.GraphicError or PipelineStage.PipelineError or PipelineStage.SessionEnded)
                        .Sum(counter => counter.Count);
                    var request = Path.Combine(AppPaths.LogsDirectory, "pipeline-dump.request");
                    var manual = TryConsumePipelineDumpRequest(request);
                    if (manual || ((snapshot.Suspicions.Length > 0 || faults > lastFaultCount) &&
                        (lastDump == 0 || Stopwatch.GetElapsedTime(lastDump, snapshot.Ticks) >= TimeSpan.FromSeconds(30))))
                    {
                        await WriteAsync(new { Type = "flight-recorder", Manual = manual, Utc = DateTimeOffset.UtcNow, snapshot.Suspicions, Events = PipelineTrace.Recorder.Events() })
                            .ConfigureAwait(false);
                        lastDump = snapshot.Ticks;
                        lastFaultCount = faults;
                    }
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                    async Task WriteAsync(object value)
                    {
                        await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);
                        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
                    }
                }
                if (new FileInfo(name).Length >= 16 * 1024 * 1024)
                {
                    if (File.Exists(name + ".1")) File.Move(name + ".1", name + ".2", overwrite: true);
                    File.Move(name, name + ".1", overwrite: true);
                }
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Pipeline diagnostic writer stopped; runtime pipeline remains unchanged"); }
    }
}
