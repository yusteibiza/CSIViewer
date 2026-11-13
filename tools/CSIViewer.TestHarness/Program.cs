using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Protocol;
using CSIViewer.Services;

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch
{
    // La consola puede no admitir cambios de codificación.
}

var mode = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var dataRoot = Path.Combine(Path.GetTempPath(), "CSIViewerHarness");
Directory.CreateDirectory(dataRoot);

// Each controller process must have its own identity/database. The caller can
// provide CSIVIEWER_HARNESS_DATA to run several controllers concurrently.
var harnessData = Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_DATA");
if (!string.IsNullOrWhiteSpace(harnessData))
{
    Environment.SetEnvironmentVariable("CSIVIEWER_DATA_DIR", harnessData);
}
else if (mode == "host")
{
    Environment.SetEnvironmentVariable("CSIVIEWER_DATA_DIR", Path.Combine(dataRoot, "host"));
}
else if (mode is "controller" or "viewer" or "viewercontrol" or "file" or "folder" or "receivefile" or "resume" or "autoresume" or "cancel" or "cycle" or "reconnect" or "monitor" or "bench" or "relay")
{
    Environment.SetEnvironmentVariable("CSIVIEWER_DATA_DIR", Path.Combine(dataRoot, "controller"));
}

if (mode == "direct")
{
    await using var directServer = CSIViewer.Networking.DirectSessionServer.Start("harness-session");
    Console.WriteLine($"DIRECT_OFFER {directServer.EndPoints}");
    var acceptTask = directServer.AcceptAsync();
    var stream = await CSIViewer.Networking.DirectSessionClient.TryConnectAsync(
        directServer.EndPoints, directServer.Token, "harness-session");
    if (stream is null)
    {
        Console.WriteLine("DIRECT_FAIL client");
        return;
    }

    var accepted = await acceptTask;
    if (accepted is null)
    {
        Console.WriteLine("DIRECT_FAIL server");
        return;
    }

    await CSIViewer.Networking.FrameProtocol.WriteFrameAsync(stream, "hello"u8.ToArray());
    var payload = await CSIViewer.Networking.FrameProtocol.ReadFrameAsync(accepted);
    Console.WriteLine(payload is null ? "DIRECT_FAIL read" : $"DIRECT_OK {System.Text.Encoding.UTF8.GetString(payload)}");
    stream.Dispose();
    return;
}

if (mode == "stun")
{
    try
    {
        var stunHost = args.Length > 1 ? args[1] : "stun.l.google.com";
        var stunPort = args.Length > 2 && int.TryParse(args[2], out var parsedPort) ? parsedPort : 19302;
        var result = await CSIViewer.Networking.StunClient.QueryAsync(stunHost, stunPort);
        Console.WriteLine($"STUN_OK {result.PublicEndPoint} via {result.Server}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("STUN_FAIL " + ex.Message);
    }

    return;
}

var services = new ServiceCollection();
var harnessLogging = new CSIViewer.Infrastructure.Logging.LoggingOptions();
services.AddSingleton(harnessLogging);
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
    builder.AddProvider(new CSIViewer.Infrastructure.Logging.JsonLinesFileLoggerProvider(
        CSIViewer.Infrastructure.AppPaths.LogsDirectory,
        harnessLogging,
        "csiviewer-harness"));
});
services.AddCSIViewerInfrastructure();
services.AddCSIViewerClient();
services.AddSingleton(harnessLogging);
services.AddSingleton<IConsentService, ConsoleConsentService>();
services.AddSingleton<INotificationService, ConsoleNotificationService>();

await using var provider = services.BuildServiceProvider();
var bootstrap = provider.GetRequiredService<ClientBootstrap>();
var state = provider.GetRequiredService<AppState>();
var client = provider.GetRequiredService<RemoteClient>();

if (mode == "help")
{
    Console.WriteLine("Uso: CSIViewer.TestHarness host | register | controller <hostId> <password> | viewer <hostId> <password> | viewercontrol <hostId> <password> | file <hostId> <password> | folder <hostId> <password> | resume <hostId> <password> | autoresume <hostId> <password> | cancel <hostId> <password> | cycle <hostId> <password> | reconnect <hostId> <password> | monitor <hostId> <password> | bench <hostId> <password> | stats | stun | direct");
    Console.WriteLine("Entorno: CSIVIEWER_HARNESS_CYCLES, _CYCLE_HOLD_MS, _RECONNECT_TIMEOUT_SECONDS, _STATS, _FORCE_GC, _HOLD, _DURATION");
    return;
}

if (mode == "stats")
{
    PrintStats("snapshot");
    return;
}

await bootstrap.InitializeAsync();

for (var i = 0; i < 40 && !state.IsServerOnline; i++)
{
    await Task.Delay(250);
}

if (!state.IsServerOnline)
{
    Console.Error.WriteLine("No se pudo autenticar con el servidor. ¿Está ejecutándose en http://localhost:5080?");
    return;
}

if (Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_STATS") == "1")
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            PrintStats("periodic");
        }
    });
}

if (mode == "register")
{
    Console.WriteLine($"REGISTERED id={state.DeviceId}");
    return;
}

if (mode == "host")
{
    state.Settings.DownloadFolder = Path.Combine(Environment.GetEnvironmentVariable("CSIVIEWER_DATA_DIR")!, "downloads");
    var info = new
    {
        state.DeviceId,
        state.TemporaryPasswordRaw,
        state.DeviceName
    };
    await File.WriteAllTextAsync(
        Path.Combine(Environment.GetEnvironmentVariable("CSIVIEWER_DATA_DIR")!, "host.json"),
        JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"HOST_READY id={state.DeviceId} password={state.TemporaryPasswordRaw}");
    client.SessionStarted += (_, session) =>
        Console.WriteLine($"HOST_SESSION_STARTED id={session.SessionId} remote={session.RemoteName} permissions={session.Permissions}");
    client.SessionEnded += (_, reason) =>
        Console.WriteLine($"HOST_SESSION_ENDED reason={reason} active={state.ActiveSessionCount}");
    var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.ChatMessageReceived += (_, message) =>
    {
        Console.WriteLine($"HOST_CHAT from={message.SenderDeviceId} text={message.Text}");
        if (message.Text.Contains("ping", StringComparison.OrdinalIgnoreCase))
        {
            received.TrySetResult(message.Text);
        }
    };
    var pauseScheduled = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
    client.TransferUpdated += (_, transfer) =>
    {
        Console.WriteLine($"HOST_TRANSFER {transfer.FileName} {transfer.State} {transfer.TransferredBytes}/{transfer.TotalBytes} {transfer.Error}");
        if (transfer.State == TransferState.Completed)
        {
            received.TrySetResult("file:" + transfer.LocalPath);
        }

        var pauseAfter = int.TryParse(
            Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_PAUSE_AFTER"),
            out var pauseSeconds) ? pauseSeconds : 0;
        var resumeAfter = int.TryParse(
            Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_RESUME_AFTER"),
            out var resumeSeconds) ? Math.Clamp(resumeSeconds, 1, 60) : 3;
        if (pauseAfter > 0 &&
            transfer.Direction == TransferDirection.Receive &&
            transfer.State == TransferState.Transferring &&
            pauseScheduled.TryAdd(transfer.Id, 0))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(pauseAfter));
                if (client.PauseTransfer(transfer.Id))
                {
                    Console.WriteLine("HOST_PAUSED");
                    await Task.Delay(TimeSpan.FromSeconds(resumeAfter));
                    if (client.ResumeTransfer(transfer.Id))
                    {
                        Console.WriteLine("HOST_RESUMED");
                    }
                }
            });
        }
    };

    var durationSeconds = int.TryParse(
        Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_DURATION"),
        out var parsedDuration)
        ? Math.Clamp(parsedDuration, 5, 600)
        : 90;
    if (int.TryParse(
            Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_REVOKE_AFTER"),
            out var revokeSeconds) && revokeSeconds > 0)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(revokeSeconds));
            await client.RevokeAllSessionsAsync("Revocación de prueba");
            Console.WriteLine($"HOST_REVOKED password={state.TemporaryPasswordRaw}");
        });
    }

    if (int.TryParse(
            Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_END_AFTER"),
            out var endSeconds) && endSeconds > 0)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(endSeconds));
            await client.EndAllHostSessionsAsync("El usuario finalizó la sesión.");
            Console.WriteLine("HOST_END_ALL");
        });
    }

    var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(durationSeconds)));
    if (completed == received.Task)
    {
        Console.WriteLine("HOST_OK " + await received.Task);
    }

    // Keep the host alive so concurrent controllers can complete on their own
    // schedule. The caller controls this with CSIVIEWER_HARNESS_DURATION.
    var remaining = durationSeconds - 3;
    if (remaining > 0)
    {
        await Task.Delay(TimeSpan.FromSeconds(remaining));
    }

    Console.WriteLine($"HOST_DONE active={state.ActiveSessionCount}");
    return;
}

if (mode is "controller" or "viewer" or "viewercontrol" or "file" or "folder" or "receivefile" or "resume" or "autoresume" or "cancel" or "cycle" or "reconnect" or "monitor" or "bench" or "relay")
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Faltan hostId y password.");
        return;
    }

    var hostId = args[1];
    var password = args[2];
    Console.WriteLine($"CONTROLLER connecting to {hostId}");

    if (mode == "cycle")
    {
        var cycles = ReadClampedInt("CSIVIEWER_HARNESS_CYCLES", 20, 1, 200);
        var holdMs = ReadClampedInt("CSIVIEWER_HARNESS_CYCLE_HOLD_MS", 500, 0, 10_000);
        var recoverAfter = ReadClampedInt("CSIVIEWER_HARNESS_CYCLE_RECOVER_AFTER", 0, 0, 300);
        var failures = 0;
        var rateLimited = 0;
        PrintStats("cycle-start");
        for (var index = 0; index < cycles; index++)
        {
            var connect = await client.ConnectToPeerAsync(
                hostId,
                password,
                SessionPermissions.Chat,
                chatOnly: true);
            if (!connect.IsSuccess &&
                connect.Error?.Code == "rate_limited" &&
                recoverAfter > 0)
            {
                rateLimited++;
                Console.WriteLine(
                    $"CYCLE_RATE_LIMITED {index + 1}/{cycles} recovering after {recoverAfter}s");
                await Task.Delay(TimeSpan.FromSeconds(recoverAfter));
                connect = await client.ConnectToPeerAsync(
                    hostId,
                    password,
                    SessionPermissions.Chat,
                    chatOnly: true);
                if (connect.IsSuccess)
                {
                    Console.WriteLine($"CYCLE_RECOVERED {index + 1}/{cycles}");
                }
            }

            if (connect.IsSuccess)
            {
                Console.WriteLine(
                    $"CYCLE_OK {index + 1}/{cycles} sessions={state.ActiveSessionCount}");
                await Task.Delay(holdMs);
                await client.EndSessionAsync("cycle complete");
                await Task.Delay(250);
            }
            else
            {
                failures++;
                Console.WriteLine(
                    $"CYCLE_FAIL {index + 1}/{cycles} {connect.Error?.Code}: {connect.Error?.Message}");
                await Task.Delay(500);
            }

            PrintStats($"cycle-{index + 1}");
        }

        var hold = ReadClampedInt("CSIVIEWER_HARNESS_HOLD", 5, 0, 300);
        Console.WriteLine(
            $"CYCLES_DONE ok={cycles - failures} fail={failures} rate_limited={rateLimited} sessions={state.ActiveSessionCount}");
        PrintStats("cycle-end");
        if (hold > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(hold));
            PrintStats("cycle-hold-end");
        }

        return;
    }

    if (mode == "reconnect")
    {
        var timeout = TimeSpan.FromSeconds(
            ReadClampedInt("CSIVIEWER_HARNESS_RECONNECT_TIMEOUT_SECONDS", 120, 10, 600));
        var initial = await client.ConnectToPeerAsync(
            hostId,
            password,
            SessionPermissions.Chat,
            chatOnly: true);
        Console.WriteLine(initial.IsSuccess
            ? "RECONNECT_INITIAL_OK"
            : $"RECONNECT_INITIAL_FAIL {initial.Error?.Code}: {initial.Error?.Message}");
        if (!initial.IsSuccess)
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (state.IsServerOnline && watch.Elapsed < timeout)
        {
            await Task.Delay(250);
        }

        if (state.IsServerOnline)
        {
            Console.WriteLine("RECONNECT_NO_LINK_DOWN");
            await client.EndSessionAsync("reconnect timeout");
            return;
        }

        Console.WriteLine($"RECONNECT_LINK_DOWN afterMs={watch.ElapsedMilliseconds}");
        watch.Restart();
        while (!state.IsServerOnline && watch.Elapsed < timeout)
        {
            await Task.Delay(250);
        }

        if (!state.IsServerOnline)
        {
            Console.WriteLine("RECONNECT_LINK_TIMEOUT");
            return;
        }

        Console.WriteLine($"RECONNECT_LINK_UP afterMs={watch.ElapsedMilliseconds}");
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var retry = await client.ConnectToPeerAsync(
                hostId,
                password,
                SessionPermissions.Chat,
                chatOnly: true);
            if (retry.IsSuccess)
            {
                Console.WriteLine($"RECONNECT_SESSION_OK attempt={attempt}");
                await client.EndSessionAsync("reconnect test complete");
                Console.WriteLine("RECONNECT_OK");
                return;
            }

            Console.WriteLine(
                $"RECONNECT_SESSION_RETRY attempt={attempt} {retry.Error?.Code}: {retry.Error?.Message}");
            await Task.Delay(1000);
        }

        Console.WriteLine("RECONNECT_SESSION_FAIL");
        return;
    }

    if (mode == "monitor")
    {
        var monitorsSource = new TaskCompletionSource<MonitorsPayload>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.MonitorsReceived += (_, payload) => monitorsSource.TrySetResult(payload);
        var connect = await client.ConnectToPeerAsync(
            hostId,
            password,
            SessionPermissions.ViewScreen | SessionPermissions.ControlInput,
            chatOnly: false);
        if (!connect.IsSuccess)
        {
            Console.WriteLine(
                $"MONITOR_CONNECT_FAIL {connect.Error?.Code}: {connect.Error?.Message}");
            return;
        }

        var completed = await Task.WhenAny(
            monitorsSource.Task,
            Task.Delay(TimeSpan.FromSeconds(20)));
        if (completed != monitorsSource.Task)
        {
            Console.WriteLine("MONITORS_TIMEOUT");
        }
        else
        {
            var monitors = await monitorsSource.Task;
            Console.WriteLine(
                $"MONITORS count={monitors.Monitors.Count} active={monitors.ActiveIndex}");
            foreach (var monitor in monitors.Monitors)
            {
                Console.WriteLine(
                    $"MONITOR_INFO index={monitor.Index} name={monitor.Name} " +
                    $"size={monitor.Width}x{monitor.Height} primary={monitor.IsPrimary}");
            }

            var frames = 0;
            client.VideoFrameReceived += (_, _) => Interlocked.Increment(ref frames);
            foreach (var monitor in monitors.Monitors.Take(4))
            {
                var before = Volatile.Read(ref frames);
                try
                {
                    await client.SelectMonitorAsync(monitor.Index);
                    await Task.Delay(TimeSpan.FromSeconds(4));
                    Console.WriteLine(
                        $"MONITOR_SWITCH index={monitor.Index} " +
                        $"frames={Volatile.Read(ref frames) - before}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"MONITOR_SWITCH_FAIL index={monitor.Index} {ex.GetBaseException().Message}");
                }
            }

            Console.WriteLine("MONITOR_OK");
        }

        await client.EndSessionAsync("monitor test complete");
        return;
    }
    var permissions = mode switch
    {
        "viewer" => SessionPermissions.ViewScreen,
        "viewercontrol" => SessionPermissions.ViewScreen | SessionPermissions.ControlInput,
        "file" or "resume" or "folder" or "autoresume" or "cancel" or "bench" => SessionPermissions.SendFiles,
        "receivefile" => SessionPermissions.ReceiveFiles,
        _ => SessionPermissions.Chat
    };
    if (mode == "resume")
    {
        state.Settings.UploadLimitKbps = 512;
    }
    if (mode == "relay")
    {
        state.Settings.PreferDirectLan = false;
    }
    var result = await client.ConnectToPeerAsync(
        hostId,
        password,
        permissions,
        chatOnly: mode is "controller" or "relay");

    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"CONTROLLER_FAIL {result.Error?.Code}: {result.Error?.Message}");
        return;
    }

    Console.WriteLine($"CONTROLLER_CONNECTED permissions={state.ActivePermissions}");
    if (mode is "controller" or "relay")
    {
        await client.SendChatAsync("ping from headless controller");
        await Task.Delay(1500);
    }
    else if (mode is "file" or "receivefile" or "resume" or "folder" or "bench")
    {
        IReadOnlyList<string> paths;
        if (mode == "folder")
        {
            var folder = Path.Combine(dataRoot, "folder-test");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(Path.Combine(folder, "empty", "sub"));
            await File.WriteAllBytesAsync(
                Path.Combine(folder, "large.bin"),
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(4 * 1024 * 1024));
            await File.WriteAllTextAsync(Path.Combine(folder, "small.txt"), "hello from folder test");
            paths = [folder];
        }
        else
        {
            var file = Path.Combine(
                dataRoot,
                mode switch
                {
                    "resume" => "resume-test.bin",
                    "bench" => "bench-test.bin",
                    _ => "transfer-test.bin"
                });
            if (!File.Exists(file))
            {
                var size = mode switch
                {
                    "resume" => 8 * 1024 * 1024,
                    "bench" => 64 * 1024 * 1024,
                    _ => 2 * 1024 * 1024
                };
                var data = System.Security.Cryptography.RandomNumberGenerator.GetBytes(size);
                await File.WriteAllBytesAsync(file, data);
            }

            paths = [file];
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var transfer = await client.SendFilesAsync(paths);
        watch.Stop();
        if (mode == "bench")
        {
            var bytes = paths.Sum(path => File.Exists(path) ? new FileInfo(path).Length : 0);
            var mbps = bytes / 1024.0 / 1024.0 / Math.Max(0.001, watch.Elapsed.TotalSeconds);
            Console.WriteLine(
                $"FILE_BENCH bytes={bytes} ms={watch.ElapsedMilliseconds} mbps={mbps:0.0}");
        }

        Console.WriteLine(transfer.IsSuccess
            ? $"FILE_OK count={transfer.Value}"
            : $"FILE_FAIL {transfer.Error?.Code}: {transfer.Error?.Message}");

        if (mode == "resume" && transfer.IsSuccess && transfer.Value == 0)
        {
            // A reconnection may have started the automatic resume already and
            // the manual send was skipped as a duplicate. Keep the session
            // alive long enough for that automatic transfer to finish.
            await Task.Delay(TimeSpan.FromSeconds(20));
        }

        await Task.Delay(2000);
    }
    else if (mode == "autoresume")
    {
        Console.WriteLine("AUTORESUME_READY");
        await Task.Delay(TimeSpan.FromSeconds(45));
    }
    else if (mode == "cancel")
    {
        var cancelFile = Path.Combine(dataRoot, "cancel-test.bin");
        if (!File.Exists(cancelFile))
        {
            await File.WriteAllBytesAsync(
                cancelFile,
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32 * 1024 * 1024));
        }

        state.Settings.UploadLimitKbps = 512;
        using var cancelSource = new CancellationTokenSource();
        var sendTask = client.SendFilesAsync([cancelFile], cancelSource.Token);
        await Task.Delay(3000);
        var active = state.Transfers.FirstOrDefault(x =>
            x.Direction == TransferDirection.Send &&
            x.FileName.Contains("cancel-test.bin", StringComparison.OrdinalIgnoreCase) &&
            x.State == TransferState.Transferring);
        if (active is not null)
        {
            await client.CancelTransferAsync(active.Id);
            Console.WriteLine("CANCEL_REQUESTED");
        }
        else
        {
            Console.WriteLine("CANCEL_NOT_FOUND");
        }

        cancelSource.Cancel();
        try
        {
            await sendTask;
        }
        catch (OperationCanceledException)
        {
        }

        await Task.Delay(2000);
    }
    else
    {
        var frames = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        client.VideoFrameReceived += (_, frame) =>
        {
            var current = Interlocked.Increment(ref count);
            if (current == 1 || current % 10 == 0)
            {
                Console.WriteLine($"VIDEO_FRAME {current} bytes={frame.Data.Length} format={frame.Format}");
            }

            if (current >= 3)
            {
                frames.TrySetResult(frame.Data.Length);
            }
        };

        var completed = await Task.WhenAny(frames.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        if (completed != frames.Task)
        {
            Console.Error.WriteLine("VIDEO_TIMEOUT");
            await client.EndSessionAsync("video timeout");
            return;
        }

        Console.WriteLine($"VIDEO_OK bytes={await frames.Task}");

        if (mode == "viewercontrol" &&
            Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_PRESS_KEY") == "1")
        {
            // Left Shift: safe modifier that does not type characters.
            await client.SendInputAsync(new InputEventPayload(
                Kind: "key",
                Action: "down",
                X: 0,
                Y: 0,
                Button: 0,
                Delta: 0,
                KeyCode: 0,
                ScanCode: 42,
                Down: true));
            Console.WriteLine("INPUT_PRESSED scan=42");
        }
    }

    var holdSeconds = int.TryParse(
        Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_HOLD"),
        out var parsedHold)
        ? Math.Clamp(parsedHold, 0, 300)
        : 0;
    if (holdSeconds > 0)
    {
        Console.WriteLine($"HOLDING seconds={holdSeconds} permissions={state.ActivePermissions}");
        await Task.Delay(TimeSpan.FromSeconds(holdSeconds));
    }

    await client.EndSessionAsync("harness complete");
    if (Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_INPUT_AFTER_END") == "1")
    {
        var before = client.DroppedInputFrames;
        await client.SendInputAsync(new InputEventPayload(
            Kind: "key",
            Action: "down",
            X: 0,
            Y: 0,
            Button: 0,
            Delta: 0,
            KeyCode: 0,
            ScanCode: 42,
            Down: true));
        Console.WriteLine(
            $"INPUT_AFTER_END before={before} after={client.DroppedInputFrames}");
    }

    Console.WriteLine("CONTROLLER_OK");
}

static int ReadClampedInt(string name, int fallback, int minimum, int maximum)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
        ? Math.Clamp(value, minimum, maximum)
        : fallback;
}

static void PrintStats(string tag)
{
    if (Environment.GetEnvironmentVariable("CSIVIEWER_HARNESS_FORCE_GC") == "1")
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    var process = System.Diagnostics.Process.GetCurrentProcess();
    process.Refresh();
    Console.WriteLine(
        $"STATS {tag} managed={GC.GetTotalMemory(false)} " +
        $"ws={process.WorkingSet64} private={process.PrivateMemorySize64} " +
        $"threads={process.Threads.Count} handles={process.HandleCount} " +
        $"tp_threads={ThreadPool.ThreadCount} tp_pending={ThreadPool.PendingWorkItemCount} " +
        $"gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}");
}

internal sealed class ConsoleConsentService : IConsentService
{
    public Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"CONSENT from={request.FromName} id={request.FromDeviceId} permissions={request.RequestedPermissions}");
        return Task.FromResult(new ConsentDecision(true, request.RequestedPermissions));
    }

    public Task<FileReceiveDecision> RequestFileReceiveAsync(
        string fromName,
        string fileName,
        long size,
        bool destinationExists,
        bool willOverwrite,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"FILE_CONSENT from={fromName} file={fileName} size={size} exists={destinationExists} overwrite={willOverwrite}");
        return Task.FromResult(FileReceiveDecision.Accept);
    }
}

internal sealed class ConsoleNotificationService : INotificationService
{
    public void Show(string title, string message) => Console.WriteLine($"NOTIFY {title}: {message}");
}
