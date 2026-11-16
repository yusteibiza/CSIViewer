using System.Diagnostics;
using CSIViewer.Core;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class PipelineRecorderTests
{
    [TestMethod]
    public void SuccessfulH264OutputDoesNotLeaveHistoricalJpegFailurePending()
    {
        var recorder = new PipelineRecorder();
        var now = Stopwatch.Frequency * 100;
        recorder.Record(PipelineStage.EncodeBegin, ticks: now - Stopwatch.Frequency * 20);
        recorder.Record(PipelineStage.JpegEncodeBegin, ticks: now - Stopwatch.Frequency * 20);
        recorder.Record(PipelineStage.EncodeFailed, ticks: now - Stopwatch.Frequency * 20);
        recorder.Record(PipelineStage.EncodeDone, ticks: now);
        Assert.IsEmpty(recorder.Snapshot(now).Suspicions);
        recorder.Record(PipelineStage.JpegEncodeBegin, ticks: now + 1);
        CollectionAssert.DoesNotContain(recorder.Snapshot(now + 2).Suspicions, "JPEG-encode-without-completion");
    }

    [TestMethod]
    public void RepeatedEncodeAttemptsDoNotHideMissingCompletionOrFailures()
    {
        var recorder = new PipelineRecorder();
        var now = Stopwatch.Frequency * 100;
        recorder.Record(PipelineStage.EncodeBegin, ticks: now - Stopwatch.Frequency * 20);
        recorder.Record(PipelineStage.JpegEncodeBegin, ticks: now - Stopwatch.Frequency * 20);
        for (var seconds = 19; seconds >= 0; seconds--)
        {
            recorder.Record(PipelineStage.EncodeBegin, ticks: now - Stopwatch.Frequency * seconds);
            recorder.Record(PipelineStage.JpegEncodeBegin, ticks: now - Stopwatch.Frequency * seconds);
            recorder.Record(PipelineStage.EncodeFailed, ticks: now - Stopwatch.Frequency * seconds);
        }
        var snapshot = recorder.Snapshot(now);
        CollectionAssert.Contains(snapshot.Suspicions, "encoder-in-flight");
        CollectionAssert.Contains(snapshot.Suspicions, "JPEG-encode-without-completion");
        CollectionAssert.Contains(snapshot.Suspicions, "encoder-attempts-failing-without-success");
        recorder.Record(PipelineStage.JpegEncodeDone, ticks: now + 1);
        recorder.Record(PipelineStage.EncodeDone, ticks: now + 1);
        Assert.IsEmpty(recorder.Snapshot(now + 1).Suspicions);
    }

    [TestMethod]
    public void ViewerOrStoppedCaptureDoesNotReportCaptureStallAfterVisualDemand()
    {
        var recorder = new PipelineRecorder();
        var now = Stopwatch.Frequency * 100;
        recorder.Record(PipelineStage.CaptureBegin, ticks: now - Stopwatch.Frequency * 20);
        recorder.Record(PipelineStage.VisualDemand, ticks: now - Stopwatch.Frequency * 10);
        Assert.IsEmpty(recorder.Snapshot(now, captureExpected: false).Suspicions);
        CollectionAssert.Contains(recorder.Snapshot(now).Suspicions, "capture-or-GPU-copy-in-flight");
        CollectionAssert.Contains(recorder.Snapshot(now).Suspicions, "capture-quiet-after-visual-demand-not-proof-of-static-desktop");
    }

    [TestMethod]
    public void ConcurrentProducersKeepBoundedRingAndExactAggregateCount()
    {
        var recorder = new PipelineRecorder(256);
        Parallel.For(0, 100_000, index => recorder.Record(PipelineStage.CaptureDone, index + 1));
        var snapshot = recorder.Snapshot();
        Assert.AreEqual(100_000L, snapshot.Counters.Single(counter => counter.Stage == PipelineStage.CaptureDone).Count);
        Assert.IsTrue(recorder.Events().Length <= 256);
        Assert.AreEqual(recorder.Events().Length, recorder.Events().Select(item => item.Sequence).Distinct().Count());
    }

    [TestMethod]
    public void QuietCaptureWithoutOutstandingDemandIsNotDeclaredStalled()
    {
        var recorder = new PipelineRecorder();
        var now = Stopwatch.Frequency * 100;
        recorder.Record(PipelineStage.CaptureDone, ticks: now - Stopwatch.Frequency * 60);
        Assert.IsEmpty(recorder.Snapshot(now).Suspicions);
    }

    [TestMethod]
    [DataRow(PipelineStage.CaptureBegin, PipelineStage.CaptureDone, "capture-or-GPU-copy-in-flight")]
    [DataRow(PipelineStage.EncodeBegin, PipelineStage.EncodeDone, "encoder-in-flight")]
    [DataRow(PipelineStage.SendBegin, PipelineStage.SendDone, "transport-send-in-flight")]
    [DataRow(PipelineStage.DecodeBegin, PipelineStage.DecodeDone, "decoder-in-flight")]
    [DataRow(PipelineStage.UiProbeQueued, PipelineStage.UiProbeExecuted, "UI-dispatcher-not-answering")]
    [DataRow(PipelineStage.UiRenderBegin, PipelineStage.UiRenderDone, "UI-render-operation-in-flight")]
    public void OutstandingStagesAreDistinguishedAndCompletionClearsSuspicion(PipelineStage begin, PipelineStage end, string suspicion)
    {
        var recorder = new PipelineRecorder();
        var now = Stopwatch.Frequency * 100;
        recorder.Record(begin, 12, ticks: now - Stopwatch.Frequency * 10);
        CollectionAssert.Contains(recorder.Snapshot(now).Suspicions, suspicion);
        recorder.Record(end, 12, ticks: now);
        CollectionAssert.DoesNotContain(recorder.Snapshot(now).Suspicions, suspicion);
    }

    [TestMethod]
    public void RingContainsOnlyNumericMetadataAndGuidCorrelationNotPayloads()
    {
        var recorder = new PipelineRecorder();
        var session = Guid.NewGuid();
        var input = Guid.NewGuid();
        recorder.Record(PipelineStage.InputReceived, 42, session, 123, 456, input);
        var entry = recorder.Events().Single();
        Assert.AreEqual(session, entry.Session);
        Assert.AreEqual(input, entry.Trace);
        Assert.AreEqual(42L, entry.Frame);
        Assert.IsFalse(typeof(PipelineEvent).GetProperties().Any(property => property.PropertyType == typeof(string) || property.PropertyType == typeof(byte[])));
    }
}
