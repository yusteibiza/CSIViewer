using System.Diagnostics;
using System.Threading;

namespace CSIViewer.Core;

public enum PipelineStage
{
    CaptureBegin, CaptureDone, CaptureDelivered, EncodeWaiting, EncodeBegin, EncodeDone,
    SendWaiting, SendBegin, SendDone, VideoReceived, DecodeQueued, DecodeBegin, DecodeDone,
    UiSubmitted, CompositionObserved, UiProbeQueued, UiProbeExecuted,
    InputSent, InputReceived, InputWindowsAccepted, InputRejected, VisualDemand,
    WindowState, CaptureState, GraphicError, PipelineError, RecoverySuppressed, FrameDropped, CaptureCallback,
    CursorObserved, LocalPointerMove, CaptureConversionBegin, CaptureConversionDone, InputSendCompleted,
    SessionStarted, SessionEnded, JpegEncodeBegin, JpegEncodeDone, EncodeFailed,
    UiViewState, UiFrameOffered, UiRenderQueued, UiRenderBegin, UiRenderDone,
    CaptureContentSampled, DecodedContentSampled, CaptureContentChanged, DecodedContentChanged
}

public sealed record PipelineEvent(long Sequence, long Ticks, PipelineStage Stage, long Frame,
    Guid Session, long A, long B, Guid Trace);
public sealed record PipelineCounter(PipelineStage Stage, long Count, long LastTicks, long LastFrame);
public sealed record PipelineSnapshot(long Ticks, PipelineCounter[] Counters, string[] Suspicions);

/// <summary>Bounded, payload-free flight recorder. Producers perform no I/O or waits.</summary>
public sealed class PipelineRecorder(int capacity = 8192)
{
    private readonly PipelineEvent?[] _ring = new PipelineEvent[Math.Clamp(capacity, 32, 65536)];
    private readonly long[] _counts = new long[Enum.GetValues<PipelineStage>().Length];
    private readonly long[] _last = new long[Enum.GetValues<PipelineStage>().Length];
    private readonly long[] _frames = new long[Enum.GetValues<PipelineStage>().Length];
    private readonly long[] _pendingSince = new long[Enum.GetValues<PipelineStage>().Length];
    private long _sequence;
    private long _frameSequence;
    public long NextFrame() => Interlocked.Increment(ref _frameSequence);

    public void Record(PipelineStage stage, long frame = 0, Guid session = default, long a = 0, long b = 0,
        Guid trace = default, long? ticks = null)
    {
        var now = ticks ?? Stopwatch.GetTimestamp();
        var index = (int)stage;
        if (stage is PipelineStage.CaptureBegin or PipelineStage.CaptureConversionBegin or PipelineStage.EncodeBegin
            or PipelineStage.JpegEncodeBegin or PipelineStage.SendWaiting or PipelineStage.SendBegin
            or PipelineStage.DecodeBegin or PipelineStage.UiProbeQueued or PipelineStage.UiRenderBegin)
            Interlocked.CompareExchange(ref _pendingSince[index], now, 0);
        void Complete(PipelineStage begin)
        {
            var pending = Interlocked.Read(ref _pendingSince[(int)begin]);
            if (pending != 0 && pending <= now)
                Interlocked.CompareExchange(ref _pendingSince[(int)begin], 0, pending);
        }
        switch (stage)
        {
            case PipelineStage.CaptureDone: Complete(PipelineStage.CaptureBegin); break;
            case PipelineStage.CaptureConversionDone: Complete(PipelineStage.CaptureConversionBegin); break;
            case PipelineStage.EncodeDone:
                Complete(PipelineStage.EncodeBegin);
                Complete(PipelineStage.JpegEncodeBegin); // successful H264 may supersede an earlier JPEG failure
                break;
            case PipelineStage.JpegEncodeDone: Complete(PipelineStage.JpegEncodeBegin); break;
            case PipelineStage.SendDone:
                Complete(PipelineStage.SendWaiting);
                Complete(PipelineStage.SendBegin);
                break;
            case PipelineStage.DecodeDone: Complete(PipelineStage.DecodeBegin); break;
            case PipelineStage.UiProbeExecuted: Complete(PipelineStage.UiProbeQueued); break;
            case PipelineStage.UiRenderDone: Complete(PipelineStage.UiRenderBegin); break;
        }
        Interlocked.Increment(ref _counts[index]);
        Interlocked.Exchange(ref _last[index], now);
        Interlocked.Exchange(ref _frames[index], frame);
        var sequence = Interlocked.Increment(ref _sequence);
        Volatile.Write(ref _ring[(int)(sequence % _ring.Length)],
            new PipelineEvent(sequence, now, stage, frame, session, a, b, trace));
    }

    public PipelineEvent[] Events()
    {
        var end = Interlocked.Read(ref _sequence);
        var result = new List<PipelineEvent>(_ring.Length);
        for (var index = 0; index < _ring.Length; index++)
        {
            var item = Volatile.Read(ref _ring[index]);
            if (item is not null && item.Sequence > end - _ring.Length && item.Sequence <= end) result.Add(item);
        }
        return result.OrderBy(item => item.Sequence).ToArray();
    }

    public PipelineSnapshot Snapshot(long? ticks = null, bool captureExpected = true)
    {
        var now = ticks ?? Stopwatch.GetTimestamp();
        var stages = Enum.GetValues<PipelineStage>();
        var counters = stages.Select(stage => new PipelineCounter(stage,
            Interlocked.Read(ref _counts[(int)stage]), Interlocked.Read(ref _last[(int)stage]),
            Interlocked.Read(ref _frames[(int)stage]))).ToArray();
        long Last(PipelineStage stage) => counters[(int)stage].LastTicks;
        bool Old(long stamp) => stamp != 0 && Stopwatch.GetElapsedTime(stamp, now) > TimeSpan.FromSeconds(5);
        var suspects = new List<string>();
        void Outstanding(PipelineStage begin, PipelineStage end, string suspicion)
        {
            var pending = Interlocked.Read(ref _pendingSince[(int)begin]);
            if (Last(begin) > Last(end) && Old(pending)) suspects.Add(suspicion);
        }
        if (captureExpected)
        {
            Outstanding(PipelineStage.CaptureBegin, PipelineStage.CaptureDone, "capture-or-GPU-copy-in-flight");
            Outstanding(PipelineStage.CaptureConversionBegin, PipelineStage.CaptureConversionDone, "capture-format-conversion-in-flight");
        }
        Outstanding(PipelineStage.EncodeBegin, PipelineStage.EncodeDone, "encoder-in-flight");
        Outstanding(PipelineStage.JpegEncodeBegin, PipelineStage.JpegEncodeDone, "JPEG-encode-without-completion");
        if (Last(PipelineStage.EncodeFailed) > Last(PipelineStage.EncodeDone) &&
            Old(Interlocked.Read(ref _pendingSince[(int)PipelineStage.EncodeBegin])))
            suspects.Add("encoder-attempts-failing-without-success");
        Outstanding(PipelineStage.SendBegin, PipelineStage.SendDone, "transport-send-in-flight");
        Outstanding(PipelineStage.SendWaiting, PipelineStage.SendDone, "encrypted-send-queue");
        Outstanding(PipelineStage.DecodeBegin, PipelineStage.DecodeDone, "decoder-in-flight");
        Outstanding(PipelineStage.UiProbeQueued, PipelineStage.UiProbeExecuted, "UI-dispatcher-not-answering");
        Outstanding(PipelineStage.UiRenderBegin, PipelineStage.UiRenderDone, "UI-render-operation-in-flight");
        if (captureExpected && Last(PipelineStage.VisualDemand) > Last(PipelineStage.CaptureDone) && Old(Last(PipelineStage.VisualDemand)))
            suspects.Add("capture-quiet-after-visual-demand-not-proof-of-static-desktop");
        if (Last(PipelineStage.DecodeDone) > Last(PipelineStage.UiSubmitted) && Old(Last(PipelineStage.DecodeDone)))
            suspects.Add("decoded-frame-not-submitted-or-view-hidden");
        if (Last(PipelineStage.UiSubmitted) > Last(PipelineStage.CompositionObserved) && Old(Last(PipelineStage.UiSubmitted)))
            suspects.Add("composition-not-observed-or-window-hidden-not-display-present-proof");
        return new PipelineSnapshot(now, counters, suspects.ToArray());
    }
}

public readonly record struct PipelineFrameContext(long Frame, Guid Session);

/// <summary>Disabled unless explicitly configured; shared by capture, services and UI.</summary>
public static class PipelineTrace
{
    private static PipelineRecorder? _recorder;
    private static readonly AsyncLocal<PipelineFrameContext> FrameContext = new();
    public static bool Enabled => Volatile.Read(ref _recorder) is not null;
    public static PipelineRecorder? Recorder => Volatile.Read(ref _recorder);
    public static PipelineFrameContext Current { get => FrameContext.Value; set => FrameContext.Value = value; }
    public static void Enable() => Interlocked.CompareExchange(ref _recorder, new PipelineRecorder(), null);
    public static long NextFrame() => Recorder?.NextFrame() ?? 0;
    public static void Record(PipelineStage stage, long frame = 0, Guid session = default, long a = 0, long b = 0, string? trace = null)
    {
        var recorder = Recorder;
        if (recorder is null) return;
        var context = Current;
        recorder.Record(stage, frame != 0 ? frame : context.Frame, session != Guid.Empty ? session : context.Session,
            a, b, Guid.TryParse(trace, out var id) ? id : Guid.Empty);
    }
}
