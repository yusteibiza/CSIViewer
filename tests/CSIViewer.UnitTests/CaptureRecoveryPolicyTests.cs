using System.Diagnostics;
using System.Reflection;
using CSIViewer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class CaptureRecoveryPolicyTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    [DataRow(false, false, false, false)]
    [DataRow(true, false, false, true)]
    [DataRow(false, true, false, true)]
    [DataRow(false, true, true, false)]
    public async Task WatchdogRequiresMissingStartupOrOutstandingVisualChange(bool missingFirst, bool demand, bool freshFrame, bool fallback)
    {
        await using var client = new RemoteClient(new AppState(), null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, NullLogger<RemoteClient>.Instance);
        // Cancel recovery before enabling synthetic capture state. No native
        // capture, encoder or input is started by this policy-only test.
        ((CancellationTokenSource)typeof(RemoteClient).GetField("_lifetimeCts", Fields)!.GetValue(client)!).Cancel();
        void Set(string name, object value) => typeof(RemoteClient).GetField(name, Fields)!.SetValue(client, value);
        var now = Stopwatch.GetTimestamp();
        Set("_captureRunning", true);
        Set("_usingModernCapture", true);
        Set("_captureViewerCount", 1);
        Set("_hostCaptureStartedTicks", now - Stopwatch.Frequency * 20);
        Set("_lastHostCaptureFrameTicks", missingFirst ? 0L : freshFrame ? now : now - Stopwatch.Frequency * 10);
        Set("_hostVisualDemandTicks", demand ? now - Stopwatch.Frequency * 6 : 0L);
        typeof(RemoteClient).GetMethod("CheckHostCaptureProgress", Fields)!.Invoke(client, null);
        Assert.AreEqual(fallback, (bool)typeof(RemoteClient).GetField("_modernCaptureDisabled", Fields)!.GetValue(client)!);
    }
}
