using System.Reflection;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Infrastructure.Persistence;
using CSIViewer.Protocol;
using CSIViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TransferClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

internal static class Program
{
    private static readonly List<object> Results = [];
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--seed-chat") { SeedChat(args[1]); return; }
        if (args.Length == 2 && args[0] == "--video-benchmark") { VideoBenchmark.Run(args[1]); return; }
        if (args.Length < 2) throw new ArgumentException("Usage: FeatureProbe isolated-server-url output.json");
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new Form { Text = "CSIViewer feature diagnostic", ShowInTaskbar = false, Width = 300, Height = 100 };
        form.Shown += async (_, _) =>
        {
            try
            {
                if (args.Length > 2 && args[2] == "--ui-controller") await RunUiControllerAsync(form, args[0], args[1]);
                else if (args.Length > 2 && args[2] == "--ui-host") await RunUiHostAsync(form, args[0], args[1]);
                else if (args.Length > 2 && args[2] == "--ui-host-delayed") await RunUiHostAsync(form, args[0], args[1], delayedConsent: true);
                else if (args.Length > 2 && args[2] == "--video-session") await RunVideoSessionAsync(form, args[0]);
                else if (args.Length > 2 && args[2] == "--control-lease") await RunControlLeaseAsync(form, args[0]);
                else await RunAsync(form, args[0]);
            }
            catch (Exception ex) { Results.Add(new { Test = "exception", Passed = false, Error = ex.ToString() }); Environment.ExitCode = 1; }
            finally
            {
                await File.WriteAllTextAsync(args[1], System.Text.Json.JsonSerializer.Serialize(Results, JsonOptions));
                form.Close();
            }
        };
        System.Windows.Forms.Application.Run(form);
    }

    private static async Task RunUiControllerAsync(Form form, string url, string output)
    {
        await using var provider = await NewPeerAsync(form, url, false);
        var client = provider.GetRequiredService<RemoteClient>();
        var state = provider.GetRequiredService<AppState>();
        form.Hide();
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(output + ".target.json"));
        var target = document.RootElement;
        var bytes = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(target.GetProperty("Password").GetString()!),
            "CSIViewer.QA.ChatUI"u8.ToArray(), System.Security.Cryptography.DataProtectionScope.CurrentUser);
        string password;
        try { password = System.Text.Encoding.UTF8.GetString(bytes); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        var id = target.GetProperty("Id").GetString()!;
        var result = await client.ConnectToPeerAsync(id, password, SessionPermissions.Chat, true);
        if (!result.IsSuccess) throw new InvalidOperationException("Diagnostic incoming connection failed");
        await client.SendChatToDeviceAsync(id, "QA temporary incoming 😊");
        await File.WriteAllTextAsync(output + ".connected", "ready");
        for (var i = 0; i < 1800 && state.IsSessionActive && !File.Exists(output + ".stop"); i++) await Task.Delay(100);
        await client.EndCurrentSessionsAsync("diagnostic viewer ends");
        await File.WriteAllTextAsync(output + ".ended", "done");
    }

    private static async Task RunUiHostAsync(Form form, string url, string output, bool delayedConsent = false)
    {
        await using var provider = await NewPeerAsync(form, url, true,
            consent: delayedConsent ? new DeferredConsent(output) : null);
        var state = provider.GetRequiredService<AppState>();
        var client = provider.GetRequiredService<RemoteClient>();
        var bytes = System.Text.Encoding.UTF8.GetBytes(state.TemporaryPasswordRaw);
        string password;
        try { password = Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(bytes,
            "CSIViewer.QA.ChatUI"u8.ToArray(), System.Security.Cryptography.DataProtectionScope.CurrentUser)); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        await File.WriteAllTextAsync(output + ".peer.json", System.Text.Json.JsonSerializer.Serialize(new { Url = url, Id = state.DeviceId, Password = password }));
        form.Hide();
        for (var i = 0; i < 1800 && !File.Exists(output + ".stop"); i++)
        {
            var progress = System.Text.Json.JsonSerializer.Serialize(new { Received = state.ChatMessages.Count(x => !x.IsMine) });
            await File.WriteAllTextAsync(output + ".progress.tmp", progress);
            try { File.Move(output + ".progress.tmp", output + ".progress.json", overwrite: true); }
            catch (IOException) { /* A diagnostic reader may briefly hold the previous snapshot. */ }
            await Task.Delay(100);
        }
        await client.EndCurrentSessionsAsync("UI chat diagnostic finished");
    }

    private static void SeedChat(string directory)
    {
        var allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode") + Path.DirectorySeparatorChar;
        directory = Path.GetFullPath(directory);
        if (!directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only isolated opencode temporary data is allowed");
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "client.db");
        if (File.Exists(database)) throw new InvalidOperationException("Refusing to seed an existing database");
        using var db = new ClientDbContext(new DbContextOptionsBuilder<ClientDbContext>().UseSqlite("Data Source=" + database).Options);
        db.EnsureDatabase();
        db.KnownDevices.AddRange(
            new KnownDeviceEntity { DeviceId = "111111111", Alias = "QA Casa", LastSeenUtc = DateTimeOffset.UtcNow, ProtectedPassword = SecretProtector.Protect("synthetic-qa-password-only", "CSIViewer.DevicePassword.v1") },
            new KnownDeviceEntity { DeviceId = "222222222", Alias = "QA Oficina", LastSeenUtc = DateTimeOffset.UtcNow },
            new KnownDeviceEntity { DeviceId = "333333333", Alias = "QA Sin historial", LastSeenUtc = DateTimeOffset.UtcNow });
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        db.Contacts.Add(new ContactEntity { Id = "qa-contact", DisplayName = "QA Contacto", Notes = "Synthetic UI row", CreatedUtc = start, DeviceIds = "111111111;222222222" });
        for (var i = 0; i < 601; i++) db.ChatMessages.Add(new ChatMessageEntity
            { Id = "qa-a-" + i, PeerDeviceId = "111111111", SenderDeviceId = "111111111", Text = $"QA-A-{i:000} 😊", SentUtc = start.AddSeconds(i), IsMine = i % 2 == 0, DeliveryState = (int)ChatDeliveryState.Sent });
        for (var i = 0; i < 2; i++) db.ChatMessages.Add(new ChatMessageEntity
            { Id = "qa-b-" + i, PeerDeviceId = "222222222", SenderDeviceId = "222222222", Text = $"QA-B-{i:000} 🎉", SentUtc = start.AddSeconds(i), DeliveryState = (int)ChatDeliveryState.Received });
        db.SaveChanges();
        Console.WriteLine("SEEDED_SYNTHETIC_CHATS");
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

    private static async Task<ServiceProvider> NewPeerAsync(Form form, string url, bool host, bool temporaryChat = false, int videoFps = 12, IConsentService? consent = null)
    {
        var data = Path.Combine(Path.GetTempPath(), "opencode", "csiviewer-feature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        AppPaths.SetDataDirectory(data);
        await new JsonSettingsStore(data).SaveAsync(new AppSettings
        {
            ServerUrl = url, EnrollmentCode = "FEATURE-QA", DeviceName = host ? "Feature QA host" : "Feature QA controller",
            AutoConnect = true, AllowIncomingConnections = host, PreferDirectLan = false, StartWithWindows = false,
            StoreConnectionHistory = false, NotificationsEnabled = false,
            Fps = videoFps, MaxWidth = 2560, PreferH264 = true, PreferModernCapture = true
        });
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        services.AddCSIViewerInfrastructure();
        services.AddCSIViewerClient();
        services.AddSingleton(new ChatHistoryOptions { RetainHistory = !temporaryChat });
        services.AddSingleton<IConsentService>(consent ?? new TestConsent());
        services.AddSingleton<INotificationService, QuietNotifications>();
        var provider = services.BuildServiceProvider();
        var state = provider.GetRequiredService<AppState>();
        state.UiInvoker = action => { if (!form.IsDisposed) form.BeginInvoke(action); };
        await provider.GetRequiredService<ClientBootstrap>().InitializeAsync();
        await WaitAsync(() => state.IsServerOnline);
        return provider;
    }

    private static async Task RunVideoSessionAsync(Form form, string url)
    {
        await using var hostProvider = await NewPeerAsync(form, url, true, videoFps: 60);
        await using var viewerProvider = await NewPeerAsync(form, url, false, videoFps: 60);
        var host = hostProvider.GetRequiredService<RemoteClient>();
        var viewer = viewerProvider.GetRequiredService<RemoteClient>();
        var hostState = hostProvider.GetRequiredService<AppState>();
        var viewerState = viewerProvider.GetRequiredService<AppState>();
        var times = new System.Collections.Concurrent.ConcurrentQueue<double>();
        var frames = 0;
        var width = 0;
        var height = 0;
        var invalid = 0;
        viewer.VideoFrameReceived += (_, frame) =>
        {
            if (frame.Format != VideoPixelFormat.Bgra || frame.Data.Length != frame.Width * frame.Height * 4)
                Interlocked.Increment(ref invalid);
            Volatile.Write(ref width, frame.Width); Volatile.Write(ref height, frame.Height);
            times.Enqueue(viewer.LastDecodeMilliseconds);
            Interlocked.Increment(ref frames);
        };
        form.Hide();
        var result = await viewer.ConnectToPeerAsync(hostState.DeviceId, hostState.TemporaryPasswordRaw, SessionPermissions.ViewScreen, false);
        Check("video-60fps-encrypted-session-connects", result.IsSuccess);
        await WaitAsync(() => Volatile.Read(ref frames) >= 10);
        var before = Volatile.Read(ref frames);
        await Task.Delay(5000);
        Check("video-stream-continues-with-valid-bgra", Volatile.Read(ref frames) > before && Volatile.Read(ref invalid) == 0);
        var values = times.Order().ToArray();
        Results.Add(new { Test = "video-local-session-measurements", Passed = true, Width = width, Height = height,
            Frames = frames, MedianDecodeMs = values[values.Length / 2], P95DecodeMs = values[(values.Length - 1) * 95 / 100],
            Scope = "Local encrypted relay; no UI rendering or WAN FPS claim" });
        await viewer.EndCurrentSessionsAsync("video optimisation diagnostic finished");
        await WaitAsync(() => !hostState.IsSessionActive && !viewerState.IsSessionActive);
        Check("video-session-ends-cleanly", true);
    }

    private static object Session(RemoteClient client) => typeof(RemoteClient).GetMethod("GetPrimarySession", PrivateInstance)!.Invoke(client, null)!;
    private static void Features(RemoteClient client, object session, bool audio, bool clipboard) =>
        typeof(RemoteClient).GetMethod("SetSessionFeatures", PrivateInstance)!.Invoke(client, [session, audio, clipboard]);
    private static Task SendAsync(RemoteClient client, object session, SessionFrameKind kind, byte[] payload)
    {
        var method = typeof(RemoteClient).GetMethods(PrivateInstance).Single(x => x.Name == "SendEncryptedAsync" && x.GetParameters().Length == 5);
        return (Task)method.Invoke(client, [session, kind, payload, 10, CancellationToken.None])!;
    }

    private static async Task RunControlLeaseAsync(Form form, string url)
    {
        await using var hostProvider = await NewPeerAsync(form, url, true);
        await using var firstProvider = await NewPeerAsync(form, url, false);
        await using var secondProvider = await NewPeerAsync(form, url, false);
        var hostState = hostProvider.GetRequiredService<AppState>();
        var first = firstProvider.GetRequiredService<RemoteClient>();
        var second = secondProvider.GetRequiredService<RemoteClient>();
        var requested = SessionPermissions.ViewScreen | SessionPermissions.ControlInput;
        var result = await first.ConnectToPeerAsync(hostState.DeviceId, hostState.TemporaryPasswordRaw, requested, false);
        Check("first-controller-connects", result.IsSuccess);
        await WaitAsync(() => first.IsSessionActive);
        Check("first-controller-owns-input", first.CanControlRemote && first.RemoteControlRestriction == ControlRestriction.None);
        result = await second.ConnectToPeerAsync(hostState.DeviceId, hostState.TemporaryPasswordRaw, requested, false);
        Check("second-controller-connects-view-only", result.IsSuccess);
        await WaitAsync(() => second.IsSessionActive);
        Check("second-controller-receives-in-use-restriction", !second.CanControlRemote && second.RemoteControlRestriction == ControlRestriction.InUse);
        await first.EndSessionAsync();
        await Task.Delay(500);
        Check("existing-viewer-not-silently-promoted", !second.CanControlRemote);
        await second.EndSessionAsync();
        await WaitAsync(() => !hostState.IsSessionActive);
        result = await second.ConnectToPeerAsync(hostState.DeviceId, hostState.TemporaryPasswordRaw, requested, false);
        Check("fresh-connection-succeeds", result.IsSuccess);
        await WaitAsync(() => second.IsSessionActive);
        Check("fresh-connection-regains-control", second.CanControlRemote && second.RemoteControlRestriction == ControlRestriction.None);
        await second.EndSessionAsync();
        // This probe never sends input to the local desktop.
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
        var sent = await viewer.SendChatToDeviceAsync(hostState.DeviceId, "QA encrypted chat 😊");
        await WaitAsync(() => hostState.ChatMessages.Any(x => x.Id == sent?.Id && !x.IsMine));
        Check("chat-targeted-message-arrives-encrypted", sent?.DeliveryState == ChatDeliveryState.Sent && sent.PeerDeviceId == hostState.DeviceId);
        var reply = await host.SendChatToDeviceAsync(viewerState.DeviceId, "QA encrypted reply 🎉");
        await WaitAsync(() => viewerState.ChatMessages.Any(x => x.Id == reply?.Id && !x.IsMine));
        Check("host-can-reply-to-selected-peer", reply?.DeliveryState == ChatDeliveryState.Sent);
        Check("chat-unknown-peer-never-falls-back", await viewer.SendChatToDeviceAsync("999999999", "Must not be sent") is null);
        Check("chat-selected-history-persisted", (await viewer.GetChatHistoryAsync(hostState.DeviceId)).Count == 2);
        await viewer.DeleteChatHistoryAsync(hostState.DeviceId);
        await WaitAsync(() => !viewerState.ChatMessages.Any(x => x.PeerDeviceId == hostState.DeviceId));
        Check("chat-delete-is-local-only", (await host.GetChatHistoryAsync(viewerState.DeviceId)).Count == 2 &&
            (await viewer.GetChatHistoryAsync(hostState.DeviceId)).Count == 0);
        sent = await viewer.SendChatToDeviceAsync(hostState.DeviceId, "QA new message after local clear");
        await WaitAsync(() => hostState.ChatMessages.Any(x => x.Id == sent?.Id && !x.IsMine));
        Check("chat-clear-does-not-end-session-or-hide-new-messages", viewerState.IsSessionActive &&
            (await viewer.GetChatHistoryAsync(hostState.DeviceId)).Count == 1);
        await viewer.EndCurrentSessionsAsync("feature permission diagnostic finished");
        await WaitAsync(() => !hostState.IsSessionActive);

        await using var temporaryProvider = await NewPeerAsync(form, url, true, temporaryChat: true);
        var temporaryHost = temporaryProvider.GetRequiredService<RemoteClient>();
        var temporaryState = temporaryProvider.GetRequiredService<AppState>();
        for (var round = 0; round < 2; round++)
        {
            connected = await viewer.ConnectToPeerAsync(temporaryState.DeviceId, temporaryState.TemporaryPasswordRaw, SessionPermissions.Chat, true);
            Check($"temporary-host-connects-round-{round}", connected.IsSuccess);
            await WaitAsync(() => temporaryState.IsSessionActive && viewerState.IsSessionActive);
            Check($"temporary-host-starts-empty-round-{round}", temporaryState.ChatMessages.Count == 0);
            sent = await viewer.SendChatToDeviceAsync(temporaryState.DeviceId, "QA temporary host chat");
            await WaitAsync(() => temporaryState.ChatMessages.Any(x => x.Id == sent?.Id));
            reply = await temporaryHost.SendChatToDeviceAsync(viewerState.DeviceId, "QA temporary reply 😊");
            await WaitAsync(() => viewerState.ChatMessages.Any(x => x.Id == reply?.Id));
            Check($"temporary-host-never-persists-round-{round}",
                (await temporaryProvider.GetRequiredService<IChatRepository>().GetThreadAsync(viewerState.DeviceId)).Count == 0);
            if (round == 0) await viewer.EndCurrentSessionsAsync("viewer ends temporary chat");
            else await temporaryHost.EndCurrentSessionsAsync("host ends temporary chat");
            await WaitAsync(() => !temporaryState.IsSessionActive && !viewerState.IsSessionActive && temporaryState.ChatMessages.Count == 0);
            Check($"temporary-host-clears-on-{(round == 0 ? "viewer" : "host")}-end", true);
            Check($"viewer-retains-chat-after-temporary-host-end-{round}",
                (await viewer.GetChatHistoryAsync(temporaryState.DeviceId)).Count == (round + 1) * 2);
        }
    }

    private sealed class TestConsent : IConsentService
    {
        public Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsentDecision(true, request.RequestedPermissions));
        public Task<FileReceiveDecision> RequestFileReceiveAsync(string device, string name, long size, bool directory, bool overwrite, CancellationToken cancellationToken = default) =>
            Task.FromResult(FileReceiveDecision.Reject);
    }

    private sealed class DeferredConsent(string output) : IConsentService
    {
        public async Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(output + ".requested", "ready", cancellationToken);
            for (var i = 0; i < 1200; i++)
            {
                if (File.Exists(output + ".decision"))
                {
                    var decision = await File.ReadAllTextAsync(output + ".decision", cancellationToken);
                    File.Delete(output + ".decision");
                    return new ConsentDecision(decision.Trim() == "accept", request.RequestedPermissions);
                }
                await Task.Delay(100, cancellationToken);
            }
            return new ConsentDecision(false, SessionPermissions.None);
        }

        public Task<FileReceiveDecision> RequestFileReceiveAsync(string device, string name, long size, bool directory, bool overwrite, CancellationToken cancellationToken = default) =>
            Task.FromResult(FileReceiveDecision.Reject);
    }
    private sealed class QuietNotifications : INotificationService
    {
        public void Show(string title, string message) { }
    }
}
