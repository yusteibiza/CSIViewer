using System.Reflection;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Protocol;
using CSIViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TransferClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

internal static class Program
{
    private static readonly List<object> Results = [];
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Usage: FeatureProbe isolated-server-url output.json");
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new Form { Text = "CSIViewer feature diagnostic", ShowInTaskbar = false, Width = 300, Height = 100 };
        form.Shown += async (_, _) =>
        {
            try { await RunAsync(form, args[0]); }
            catch (Exception ex) { Results.Add(new { Test = "exception", Passed = false, Error = ex.ToString() }); Environment.ExitCode = 1; }
            finally
            {
                await File.WriteAllTextAsync(args[1], System.Text.Json.JsonSerializer.Serialize(Results, JsonOptions));
                form.Close();
            }
        };
        System.Windows.Forms.Application.Run(form);
    }

    private static void Check(string test, bool passed)
    {
        Results.Add(new { Test = test, Passed = passed });
        Console.WriteLine($"{test}={passed}");
        if (!passed) throw new InvalidOperationException(test);
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(100);
        if (!ready()) throw new TimeoutException("Feature diagnostic condition timed out");
    }

    private static async Task<ServiceProvider> NewPeerAsync(Form form, string url, bool host)
    {
        var data = Path.Combine(Path.GetTempPath(), "opencode", "csiviewer-feature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        AppPaths.SetDataDirectory(data);
        await new JsonSettingsStore(data).SaveAsync(new AppSettings
        {
            ServerUrl = url, EnrollmentCode = "FEATURE-QA", DeviceName = host ? "Feature QA host" : "Feature QA controller",
            AutoConnect = true, AllowIncomingConnections = host, PreferDirectLan = false, StartWithWindows = false,
            StoreConnectionHistory = false, NotificationsEnabled = false
        });
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        services.AddCSIViewerInfrastructure();
        services.AddCSIViewerClient();
        services.AddSingleton<IConsentService, TestConsent>();
        services.AddSingleton<INotificationService, QuietNotifications>();
        var provider = services.BuildServiceProvider();
        var state = provider.GetRequiredService<AppState>();
        state.UiInvoker = action => { if (!form.IsDisposed) form.BeginInvoke(action); };
        await provider.GetRequiredService<ClientBootstrap>().InitializeAsync();
        await WaitAsync(() => state.IsServerOnline);
        return provider;
    }

    private static object Session(RemoteClient client) => typeof(RemoteClient).GetMethod("GetPrimarySession", PrivateInstance)!.Invoke(client, null)!;
    private static void Features(RemoteClient client, object session, bool audio, bool clipboard) =>
        typeof(RemoteClient).GetMethod("SetSessionFeatures", PrivateInstance)!.Invoke(client, [session, audio, clipboard]);
    private static Task SendAsync(RemoteClient client, object session, SessionFrameKind kind, byte[] payload)
    {
        var method = typeof(RemoteClient).GetMethods(PrivateInstance).Single(x => x.Name == "SendEncryptedAsync" && x.GetParameters().Length == 5);
        return (Task)method.Invoke(client, [session, kind, payload, 10, CancellationToken.None])!;
    }

    private static async Task RunAsync(Form form, string url)
    {
        await using var hostProvider = await NewPeerAsync(form, url, true);
        await using var viewerProvider = await NewPeerAsync(form, url, false);
        var host = hostProvider.GetRequiredService<RemoteClient>();
        var viewer = viewerProvider.GetRequiredService<RemoteClient>();
        var hostState = hostProvider.GetRequiredService<AppState>();
        var viewerState = viewerProvider.GetRequiredService<AppState>();
        var connected = await viewer.ConnectToPeerAsync(hostState.DeviceId, hostState.TemporaryPasswordRaw,
            SessionPermissions.Audio | SessionPermissions.Clipboard, false);
        Check("encrypted-session-new-permissions", connected.IsSuccess);
        await WaitAsync(() => hostState.IsSessionActive && viewerState.IsSessionActive);
        Check("features-default-off", !viewer.IsAudioEnabled && !viewer.IsClipboardEnabled && !host.IsClipboardEnabled);
        await viewer.ConfigureSessionFeaturesAsync(false, true);
        await WaitAsync(() => host.IsClipboardEnabled);
        Check("clipboard-negotiated-both-directions", viewer.IsClipboardEnabled && host.IsClipboardEnabled);
        await SendAsync(viewer, Session(viewer), SessionFrameKind.Clipboard, [0xC0, 0xAF]);
        await SendAsync(viewer, Session(viewer), SessionFrameKind.Clipboard, new byte[1024 * 1024 + 1]);
        await Task.Delay(200);
        Check("clipboard-rejects-invalid-utf8-and-oversize", host.ClipboardUpdatesReceived == 0);

        var bytes = System.Text.Encoding.UTF8.GetBytes("CSIViewer synthetic clipboard diagnostic");
        await SendAsync(viewer, Session(viewer), SessionFrameKind.Clipboard, bytes);
        await WaitAsync(() => host.ClipboardUpdatesReceived > 0);
        await SendAsync(host, Session(host), SessionFrameKind.Clipboard, bytes);
        await WaitAsync(() => viewer.ClipboardUpdatesReceived > 0);
        Check("encrypted-clipboard-applied-both-directions", true);
        var received = host.ClipboardUpdatesReceived + viewer.ClipboardUpdatesReceived;
        await Task.Delay(750);
        Check("remote-clipboard-no-echo", received == host.ClipboardUpdatesReceived + viewer.ClipboardUpdatesReceived);

        // Real local ContentChanged path; only synthetic text, never print clipboard data.
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText("CSIViewer local clipboard event diagnostic");
        TransferClipboard.SetContent(package);
        await WaitAsync(() => host.ClipboardUpdatesReceived + viewer.ClipboardUpdatesReceived > received);
        await Task.Delay(400);
        received = host.ClipboardUpdatesReceived + viewer.ClipboardUpdatesReceived;
        await Task.Delay(750);
        Check("local-content-changed-no-endless-echo", received == host.ClipboardUpdatesReceived + viewer.ClipboardUpdatesReceived);

        // Activate only the receiving audio endpoint: never loop system output
        // back to itself when both peers are on this one diagnostic computer.
        Features(viewer, Session(viewer), true, true);
        var silence = AudioPacket.Create(48000, 2, 32, true, new byte[3840]);
        await SendAsync(host, Session(host), SessionFrameKind.Audio, silence);
        await WaitAsync(() => viewer.AudioPacketsReceived > 0);
        Check("encrypted-silent-audio-playback", viewer.IsAudioEnabled);
        await SendAsync(viewer, Session(viewer), SessionFrameKind.Audio, silence);
        await Task.Delay(200);
        Check("host-rejects-reverse-audio", host.AudioPacketsReceived == 0);

        await viewer.ConfigureSessionFeaturesAsync(false, false);
        await WaitAsync(() => !host.IsClipboardEnabled);
        var audioCount = viewer.AudioPacketsReceived;
        var clipboardCount = viewer.ClipboardUpdatesReceived;
        await SendAsync(host, Session(host), SessionFrameKind.Audio, silence);
        await SendAsync(host, Session(host), SessionFrameKind.Clipboard, bytes);
        await Task.Delay(300);
        Check("disabled-features-drop-incoming-data", viewer.AudioPacketsReceived == audioCount && viewer.ClipboardUpdatesReceived == clipboardCount);

        // Native capture using a silent render stream; no audible tone or saved audio.
        var streamType = typeof(RemoteClient).Assembly.GetType("CSIViewer.Services.SystemAudioStream")!;
        using var capture = (IDisposable)Activator.CreateInstance(streamType, nonPublic: true)!;
        using var playback = (IDisposable)Activator.CreateInstance(streamType, nonPublic: true)!;
        var captured = 0;
        Exception? failure = null;
        Func<byte[], CancellationToken, Task> record = (packet, token) => { if (AudioPacket.TryRead(packet, out _, out _, out _, out _)) Interlocked.Increment(ref captured); return Task.CompletedTask; };
        streamType.GetMethod("StartCapture")!.Invoke(capture, [record, (Action<Exception>)(ex => failure = ex)]);
        for (var i = 0; i < 30; i++) { streamType.GetMethod("Play")!.Invoke(playback, [silence]); await Task.Delay(10); }
        await WaitAsync(() => Volatile.Read(ref captured) > 0 || failure is not null);
        Check("native-wasapi-loopback-captures-output", captured > 0 && failure is null);

        await viewer.EndCurrentSessionsAsync("feature diagnostic finished");
        await WaitAsync(() => !hostState.IsSessionActive);
        Check("session-end-clears-features", !viewer.IsAudioEnabled && !viewer.IsClipboardEnabled && !host.IsClipboardEnabled);
        connected = await viewer.ConnectToPeerAsync(hostState.DeviceId, hostState.TemporaryPasswordRaw, SessionPermissions.Chat, true);
        Check("legacy-chat-session-still-connects", connected.IsSuccess);
        await WaitAsync(() => hostState.IsSessionActive && viewerState.IsSessionActive);
        await viewer.ConfigureSessionFeaturesAsync(true, true);
        Check("ungranted-features-cannot-be-enabled-locally", !viewer.IsAudioEnabled && !viewer.IsClipboardEnabled);
        await SendAsync(viewer, Session(viewer), SessionFrameKind.Control,
            ControlJson.Create("session.features", new { audio = true, clipboard = true }));
        await Task.Delay(200);
        Check("ungranted-features-cannot-be-enabled-by-peer", !host.IsAudioEnabled && !host.IsClipboardEnabled);
        await viewer.EndCurrentSessionsAsync("feature permission diagnostic finished");
    }

    private sealed class TestConsent : IConsentService
    {
        public Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsentDecision(true, request.RequestedPermissions));
        public Task<FileReceiveDecision> RequestFileReceiveAsync(string device, string name, long size, bool directory, bool overwrite, CancellationToken cancellationToken = default) =>
            Task.FromResult(FileReceiveDecision.Reject);
    }
    private sealed class QuietNotifications : INotificationService
    {
        public void Show(string title, string message) { }
    }
}
