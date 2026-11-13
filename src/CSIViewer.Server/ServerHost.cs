using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CSIViewer.Core;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Networking;
using CSIViewer.Protocol;
using CSIViewer.Security;
using CSIViewer.Server.Persistence;

namespace CSIViewer.Server;

/// <summary>
/// Hosts the ASP.NET Core server. It can be used from the headless entry point
/// or from the WinUI administration window, which starts and stops it on demand.
/// </summary>
public sealed class ServerHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly UpnpPortMapper _upnpTcp = new();
    private readonly UpnpPortMapper _upnpStun = new();
    private readonly List<string> _upnpMessages = [];
    private WebApplication? _app;
    private LoggingOptions? _loggingOptions;

    public bool IsRunning => _app is not null;
    public string? ActiveUrl { get; private set; }
    public IReadOnlyList<string> UpnpMessages => _upnpMessages;
    public bool UpnpFailed { get; private set; }
    public LoggingOptions? LoggingOptions => _loggingOptions;

    /// <summary>
    /// Deletes the server-side session history. Devices, enrollment codes and
    /// active sessions are not affected. Works both while the server is
    /// running (through its DI container) and when it is stopped.
    /// </summary>
    public async Task<int> PurgeSessionHistoryAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var app = _app;
        if (app is not null)
        {
            var factory = app.Services.GetRequiredService<IDbContextFactory<ServerDbContext>>();
            await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.Sessions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(databasePath))
        {
            return 0;
        }

        var options = new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        await using var direct = new ServerDbContext(options);
        return await direct.Sessions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(
        ServerRuntimeSettings settings,
        IReadOnlyList<string>? arguments = null,
        bool asWindowsService = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_app is not null)
            {
                return;
            }

            settings.Normalize();
            ServerDataPermissions.EnsureWritable(settings.DatabasePath);
            var databaseDirectory = Path.GetDirectoryName(settings.DatabasePath);
            if (!string.IsNullOrWhiteSpace(databaseDirectory))
            {
                Directory.CreateDirectory(databaseDirectory);
            }

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = arguments?.ToArray() ?? [],
                ContentRootPath = AppContext.BaseDirectory
            });
            var logDirectory = asWindowsService
                ? ServerServiceLog.LogDirectory
                : Path.Combine(ServerSettingsStore.DataDirectory, "logs");
            _loggingOptions = new LoggingOptions
            {
                MinimumLevel = settings.LogLevel.ToMicrosoft()
            };
            var minimumLogLevel = _loggingOptions.MinimumLevel;
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            // Capture the level locally. The filter can run while the server is
            // shutting down, after the mutable field has been cleared.
            builder.Logging.AddFilter((_, level) => level >= minimumLogLevel);
            builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            builder.Logging.AddProvider(new JsonLinesFileLoggerProvider(
                logDirectory,
                _loggingOptions,
                "csiviewer-servidor"));
            if (asWindowsService)
            {
                builder.Host.UseWindowsService(options => options.ServiceName = "CSIViewerServidor");
            }

            if (settings.UseHttps)
            {
                var certificate = FindCertificate(settings.CertificateThumbprint);
                if (certificate is null && !string.IsNullOrWhiteSpace(settings.CertificateThumbprint))
                {
                    throw new InvalidOperationException(
                        "No se encontró el certificado HTTPS en CurrentUser\\My ni LocalMachine\\My. " +
                        global::CSIViewer.Core.Localization.Text("Comprueba la huella y que tenga clave privada."));
                }

                if (certificate is not null)
                {
                    builder.WebHost.ConfigureKestrel(options =>
                        options.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate));
                }
            }

            var url = settings.ListenUrl;
            if (!string.IsNullOrWhiteSpace(settings.ExplicitUrl))
            {
                url = settings.ExplicitUrl;
            }
            else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            {
                builder.WebHost.UseUrls(url);
            }
            else
            {
                url = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")!;
            }

            builder.Services.AddDbContextFactory<ServerDbContext>(options =>
                options.UseSqlite($"Data Source={settings.DatabasePath}"));
            builder.Services.AddSingleton<DeviceRegistry>();
            builder.Services.AddSingleton<ClientHub>();
            builder.Services.AddSingleton<RegistrationThrottle>();
            builder.Services.AddSingleton(new ServerOptions
            {
                EnrollmentCode = settings.EnrollmentCode,
                ServerName = settings.ServerName,
                MaxSessionsPerHost = settings.MaxSessionsPerHost
            });
            if (settings.EnableStunServer)
            {
                builder.Services.AddSingleton(new StunServerOptions(settings.StunPort));
                builder.Services.AddHostedService<StunServerService>();
            }

            WebApplication? app = null;
            try
            {
                app = builder.Build();
                app.Logger.LogInformation(
                    LogEvents.AppStarted,
                    "CSIViewer Servidor iniciado. Url={Url} Db={DatabasePath} Service={AsService}",
                    settings.ListenUrl,
                    settings.DatabasePath,
                    asWindowsService);

                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ServerDbContext>>();
                    await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                    await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // WAL is an optimization.
                    }
                }
            if (settings.EnrollmentCode is "DEV-1234" or { Length: < 12 })
            {
                app.Logger.LogWarning(
                    "El código de inscripción es débil o el valor por defecto. " +
                    "Cámbialo antes de exponer el servidor.");
            }

            app.UseWebSockets(new WebSocketOptions
            {
                // Detect controllers/hosts that stop responding without closing
                // their TCP connection. Kestrel aborts only that WebSocket, so
                // the hub can release its sessions without touching others.
                KeepAliveInterval = TimeSpan.FromSeconds(20),
                KeepAliveTimeout = TimeSpan.FromSeconds(15)
            });

            app.MapGet("/api/health", () => Results.Ok(new
            {
                status = "ok",
                server = global::CSIViewer.Core.Localization.Text("CSIViewer Servidor"),
                utc = DateTimeOffset.UtcNow
            }));

            app.MapPost("/api/devices/register", async (
                RegisterDeviceRequest request,
                DeviceRegistry registry,
                RegistrationThrottle throttle,
                ServerOptions options,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var remoteKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (!throttle.TryAcquire(remoteKey))
                {
                    await registry.AuditAsync(
                        "device.registration_rate_limited",
                        null,
                        null,
                        $"IP {remoteKey}",
                        CancellationToken.None);
                    return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
                }

                if (!MessageValidation.IsValidName(request.Name))
                {
                    await registry.AuditAsync(
                        "device.registration_denied",
                        null,
                        null,
                        global::CSIViewer.Core.Localization.Text("Nombre de dispositivo no válido"),
                        CancellationToken.None);
                    return Results.BadRequest(new { error = "invalid_name" });
                }

                if (!MessageValidation.IsValidBase64(request.PublicKey, 32, 1024))
                {
                    await registry.AuditAsync(
                        "device.registration_denied",
                        null,
                        null,
                        global::CSIViewer.Core.Localization.Text("Clave pública no válida"),
                        CancellationToken.None);
                    return Results.BadRequest(new { error = "invalid_public_key" });
                }

                if (string.IsNullOrWhiteSpace(options.EnrollmentCode))
                {
                    await registry.AuditAsync(
                        "device.registration_denied",
                        null,
                        null,
                        global::CSIViewer.Core.Localization.Text("Registro deshabilitado: no hay código de inscripción"),
                        CancellationToken.None);
                    app.Logger.LogWarning(
                        LogEvents.AuthenticationFailed,
                        "Device registration rejected: enrollment is disabled");
                    return Results.Json(
                        new { error = "registration_disabled" },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (!MessageValidation.IsValidSecret(request.EnrollmentCode) ||
                    !FixedTimeEquals(request.EnrollmentCode, options.EnrollmentCode))
                {
                    await registry.AuditAsync(
                        "device.registration_denied",
                        null,
                        null,
                        global::CSIViewer.Core.Localization.Text("Código de inscripción incorrecto"),
                        CancellationToken.None);
                    app.Logger.LogWarning(
                        LogEvents.AuthenticationFailed,
                        "Device registration rejected: invalid enrollment code");
                    return Results.Json(
                        new { error = "invalid_enrollment_code" },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                try
                {
                    var device = await registry.RegisterAsync(request.Name, request.PublicKey, cancellationToken);
                    await registry.AuditAsync("device.registered", device.Id, null, global::CSIViewer.Core.Localization.Text("Registro de dispositivo"), cancellationToken);
                    return Results.Ok(new RegisterDeviceResponse(device.Id, device.Name));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { error = "invalid_public_key" });
                }
            });

            app.MapPost("/api/devices/{deviceId}/revoke", async (
                string deviceId,
                HttpContext httpContext,
                DeviceRegistry registry,
                ClientHub hub,
                ServerOptions options,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                if (!IsLocalAdminRequest(httpContext, options))
                {
                    return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
                }

                if (!MessageValidation.IsValidDeviceId(deviceId))
                {
                    return Results.BadRequest(new { error = "invalid_device_id" });
                }

                if (!await registry.RevokeAsync(deviceId, cancellationToken).ConfigureAwait(false))
                {
                    return Results.NotFound(new { error = "not_found" });
                }

                await registry.AuditAsync(
                    "device.revoked",
                    deviceId,
                    null,
                    global::CSIViewer.Core.Localization.Text("Dispositivo revocado por el administrador local"),
                    CancellationToken.None).ConfigureAwait(false);
                await hub.DisconnectDeviceAsync(deviceId, "revoked", cancellationToken).ConfigureAwait(false);
                loggerFactory.CreateLogger("Security").LogWarning("Device {DeviceId} revoked", deviceId);
                return Results.Ok(new { revoked = deviceId });
            });

            app.MapPost("/api/devices/revoke-all", async (
                HttpContext httpContext,
                DeviceRegistry registry,
                ClientHub hub,
                ServerOptions options,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                if (!IsLocalAdminRequest(httpContext, options))
                {
                    return Results.Json(new { error = "forbidden" }, statusCode: StatusCodes.Status403Forbidden);
                }

                var devices = await registry.GetAllAsync(cancellationToken).ConfigureAwait(false);
                var count = await registry.RevokeAllAsync(cancellationToken).ConfigureAwait(false);
                foreach (var device in devices)
                {
                    await hub.DisconnectDeviceAsync(device.Id, "revoked", cancellationToken)
                        .ConfigureAwait(false);
                }

                await registry.AuditAsync(
                    "device.revoked_all",
                    null,
                    null,
                    $"Dispositivos revocados: {count}",
                    CancellationToken.None).ConfigureAwait(false);
                loggerFactory.CreateLogger("Security").LogWarning("Revoked {Count} devices", count);
                return Results.Ok(new { revoked = count });
            });

            var connectThrottle = new SlidingWindowThrottle(10, TimeSpan.FromMinutes(1));
            var authFailureThrottle = new SlidingWindowThrottle(20, TimeSpan.FromMinutes(1));

            app.Map("/ws", async (HttpContext context, ServerOptions serverOptions) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var hub = context.RequestServices.GetRequiredService<ClientHub>();
                var registry = context.RequestServices.GetRequiredService<DeviceRegistry>();
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("WebSocket");

                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await using var connection = new ClientConnection(socket);
                var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var nonceIssuedUtc = DateTimeOffset.UtcNow;
                var authenticated = false;
                var failedAuth = 0;
                var connectRequests = new List<DateTimeOffset>();

                await connection.SendJsonAsync(
                    ProtocolJson.Create(MessageTypes.AuthChallenge,
                        new AuthChallenge(nonce, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())),
                    context.RequestAborted);

                var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    while (!context.RequestAborted.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        var (type, data) = await ReceiveMessageAsync(socket, buffer, context.RequestAborted);
                        if (type is null)
                        {
                            break;
                        }

                        if (type == WebSocketMessageType.Binary)
                        {
                            await RelayBinaryAsync(connection, data, hub, registry, logger, context.RequestAborted);
                            continue;
                        }

                        var envelope = ProtocolJson.Deserialize(Encoding.UTF8.GetString(data));
                        if (envelope is null)
                        {
                            await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.Error,
                                new ErrorPayload("invalid_message", global::CSIViewer.Core.Localization.Text("Mensaje no válido"))), context.RequestAborted);
                            continue;
                        }

                        if (!authenticated && envelope.Type != MessageTypes.AuthRequest)
                        {
                            await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.AuthError,
                                new AuthError("not_authenticated", global::CSIViewer.Core.Localization.Text("Autentícate antes de enviar mensajes"))), context.RequestAborted);
                            continue;
                        }

                        switch (envelope.Type)
                        {
                            case MessageTypes.AuthRequest:
                            {
                                if (authenticated)
                                {
                                    break;
                                }

                                if (DateTimeOffset.UtcNow - nonceIssuedUtc > TimeSpan.FromSeconds(30))
                                {
                                    await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.AuthError,
                                        new AuthError("expired", global::CSIViewer.Core.Localization.Text("El desafío ha caducado"))), context.RequestAborted);
                                    await connection.CloseAsync("challenge_expired");
                                    return;
                                }

                                                var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                                if (authFailureThrottle.IsLimited(remoteIp))
                                {
                                    await registry.AuditAsync(
                                        "auth.rate_limited",
                                        null,
                                        null,
                                        $"IP {remoteIp}",
                                        CancellationToken.None);
                                    await connection.SendJsonAsync(
                                        ProtocolJson.Create(MessageTypes.AuthError,
                                            new AuthError("rate_limited", global::CSIViewer.Core.Localization.Text("Demasiados intentos de autenticación"))),
                                        context.RequestAborted);
                                    await connection.CloseAsync("auth_rate_limited");
                                    return;
                                }

                                var payload = ProtocolJson.Payload<AuthRequest>(envelope);
                                if (payload is null ||
                                    !MessageValidation.IsValidDeviceId(payload.DeviceId) ||
                                    !MessageValidation.IsValidBase64(payload.Signature, 8, 1024))
                                {
                                    authFailureThrottle.TryAcquire(remoteIp);
                                    await registry.AuditAsync(
                                        "auth.failed",
                                        payload?.DeviceId,
                                        null,
                                        global::CSIViewer.Core.Localization.Text("Solicitud de autenticación no válida"),
                                        CancellationToken.None);
                                    await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.AuthError,
                                        new AuthError("invalid_auth", global::CSIViewer.Core.Localization.Text("Solicitud de autenticación no válida"))), context.RequestAborted);
                                    continue;
                                }

                                var device = await registry.GetAsync(payload.DeviceId, context.RequestAborted);
                                if (device is null || device.Revoked)
                                {
                                    authFailureThrottle.TryAcquire(remoteIp);
                                    await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.AuthError,
                                        new AuthError("unknown_device", "Dispositivo desconocido")), context.RequestAborted);
                                    await connection.CloseAsync("unknown_device");
                                    return;
                                }

                                var signed = Encoding.UTF8.GetBytes($"RD-AUTH-v1|{nonce}|{payload.DeviceId}");
                                byte[] signature;
                                try
                                {
                                    signature = Convert.FromBase64String(payload.Signature);
                                }
                                catch (FormatException)
                                {
                                    signature = [];
                                }

                                if (!DeviceIdentity.Verify(device.PublicKey, signed, signature))
                                {
                                    failedAuth++;
                                    authFailureThrottle.TryAcquire(remoteIp);
                                    await registry.AuditAsync("auth.failed", payload.DeviceId, null, "Firma incorrecta", CancellationToken.None);
                                    await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.AuthError,
                                        new AuthError("invalid_signature", "Firma incorrecta")), context.RequestAborted);
                                    if (failedAuth >= 5)
                                    {
                                        await connection.CloseAsync("too_many_auth_failures");
                                        return;
                                    }

                                    continue;
                                }

                                connection.DeviceId = device.Id;
                                connection.DeviceName = device.Name;
                                var replaced = hub.AddOrReplace(connection);
                                if (replaced is not null)
                                {
                                    await replaced.CloseAsync("replaced");
                                }

                                authenticated = true;
                                await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.AuthOk,
                                    new AuthOk(device.Id, device.Name)), context.RequestAborted);
                                await hub.SendPresenceSnapshotAsync(connection, context.RequestAborted);
                                await hub.BroadcastPresenceAsync(connection, true, context.RequestAborted);
                                try
                                {
                                    await registry.TouchAsync(device.Id, context.RequestAborted);
                                }
                                catch (Exception ex)
                                {
                                    // A read-only or busy database must not drop an
                                    // authenticated connection: the signaling and
                                    // relay paths keep working without LastSeen.
                                    logger.LogWarning(ex,
                                        "No se pudo actualizar la última conexión del dispositivo {DeviceId}",
                                        device.Id);
                                }

                                logger.LogInformation(LogEvents.ConnectionOpened, "Device {DeviceId} authenticated", device.Id);
                                break;
                            }

                            case MessageTypes.ConnectRequest:
                            {
                                var payload = ProtocolJson.Payload<ConnectRequestPayload>(envelope);
                                if (payload is null ||
                                    !Guid.TryParse(payload.SessionId, out var sessionGuid) ||
                                    !MessageValidation.IsValidDeviceId(payload.TargetId) ||
                                    !MessageValidation.IsValidPermissionMask(payload.Permissions) ||
                                    !MessageValidation.IsValidBase64(payload.EphemeralPublicKey, 32, 1024) ||
                                    !MessageValidation.IsValidBase64(payload.Nonce, 16, 64) ||
                                    !MessageValidation.IsValidBase64(payload.Signature, 8, 1024) ||
                                    !MessageValidation.IsValidBase64(payload.ControllerPublicKey, 32, 1024) ||
                                    (payload.ChatOnly && payload.Permissions != SessionPermissions.Chat))
                                {
                                    await registry.AuditAsync(
                                        "session.request_denied",
                                        connection.DeviceId,
                                        null,
                                        global::CSIViewer.Core.Localization.Text("Solicitud de conexión no válida"),
                                        CancellationToken.None);
                                    await SendErrorAsync(
                                        connection,
                                        "invalid_connect",
                                        global::CSIViewer.Core.Localization.Text("Solicitud de conexión no válida"),
                                        context.RequestAborted);
                                    break;
                                }

                                if (connection.DeviceId is null ||
                                    !connectThrottle.TryAcquire(connection.DeviceId))
                                {
                                    await registry.AuditAsync(
                                        "session.request_denied",
                                        connection.DeviceId,
                                        payload.TargetId,
                                        global::CSIViewer.Core.Localization.Text("Límite de solicitudes de conexión"),
                                        CancellationToken.None);
                                    await SendErrorAsync(
                                        connection,
                                        "rate_limited",
                                        global::CSIViewer.Core.Localization.Text("Demasiadas solicitudes de conexión. Espera un minuto."),
                                        context.RequestAborted);
                                    break;
                                }

                                var now = DateTimeOffset.UtcNow;
                                connectRequests.RemoveAll(x => now - x > TimeSpan.FromMinutes(1));
                                if (connectRequests.Count >= 5)
                                {
                                    await SendErrorAsync(connection, "rate_limited", global::CSIViewer.Core.Localization.Text("Demasiadas solicitudes de conexión. Espera un minuto."), context.RequestAborted);
                                    break;
                                }

                                connectRequests.Add(now);

                                if (payload.TargetId == connection.DeviceId)
                                {
                                    await SendErrorAsync(connection, "self_connect", global::CSIViewer.Core.Localization.Text("No puedes conectarte a tu propio equipo"), context.RequestAborted);
                                    break;
                                }

                                if (!hub.TryGet(payload.TargetId, out var target))
                                {
                                    await SendErrorAsync(connection, "host_offline", global::CSIViewer.Core.Localization.Text("El equipo remoto no está disponible"), context.RequestAborted);
                                    break;
                                }

                                var route = new SessionRoute
                                {
                                    Id = sessionGuid,
                                    ControllerId = connection.DeviceId!,
                                    HostId = payload.TargetId,
                                    ControllerConnectionId = connection.ConnectionId,
                                    HostConnectionId = target.ConnectionId
                                };

                                var admission = hub.TryAdmitSession(
                                    route,
                                    serverOptions.MaxSessionsPerHost);
                                if (admission != SessionAdmissionResult.Accepted)
                                {
                                    var (code, message) = admission switch
                                    {
                                        SessionAdmissionResult.ControllerBusy =>
                                            ("controller_busy", global::CSIViewer.Core.Localization.Text("Este equipo ya tiene una sesión con el host.")),
                                        SessionAdmissionResult.HostFull =>
                                            ("host_full", global::CSIViewer.Core.Localization.Text("El equipo remoto alcanzó el límite de sesiones simultáneas.")),
                                        _ => ("session_exists", global::CSIViewer.Core.Localization.Text("La sesión ya existe"))
                                    };
                                    await SendErrorAsync(
                                        connection,
                                        code,
                                        message,
                                        context.RequestAborted);
                                    break;
                                }

                                try
                                {
                                    await registry.RecordSessionStartAsync(
                                        payload.SessionId, connection.DeviceId!, payload.TargetId, context.RequestAborted);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex,
                                        "No se pudo guardar el inicio de la sesión {SessionId}",
                                        payload.SessionId);
                                }

                                var controllerDevice = await registry.GetAsync(connection.DeviceId!, context.RequestAborted);
                                if (controllerDevice is null)
                                {
                                    hub.RemoveSession(sessionGuid);
                                    await SendErrorAsync(connection, "unknown_device", global::CSIViewer.Core.Localization.Text("Dispositivo no registrado"), context.RequestAborted);
                                    break;
                                }

                                var delivered = await SendJsonSafeAsync(
                                        target,
                                        ProtocolJson.Create(MessageTypes.ConnectIncoming,
                                            new ConnectIncomingPayload(
                                                payload.SessionId,
                                                connection.DeviceId!,
                                                connection.DeviceName ?? connection.DeviceId!,
                                                payload.Permissions,
                                                payload.EphemeralPublicKey,
                                                payload.Nonce,
                                                payload.Signature,
                                                controllerDevice.PublicKey,
                                                payload.ChatOnly,
                                                DateTimeOffset.UtcNow.AddSeconds(60)),
                                            payload.SessionId),
                                        context.RequestAborted);
                                if (!delivered)
                                {
                                    hub.RemoveSession(sessionGuid);
                                    await SendErrorAsync(
                                        connection,
                                        "host_unreachable",
                                        global::CSIViewer.Core.Localization.Text("No se pudo contactar con el equipo remoto"),
                                        context.RequestAborted);
                                    logger.LogWarning(LogEvents.ConnectionClosed, "Could not forward connect request");
                                }

                                await registry.AuditAsync("session.requested", connection.DeviceId, payload.TargetId,
                                    $"Permisos: {payload.Permissions}", context.RequestAborted);
                                break;
                            }

                            case MessageTypes.ConnectPending:
                            {
                                var payload = ProtocolJson.Payload<ConnectPendingPayload>(envelope);
                                if (payload is null ||
                                    !Guid.TryParse(payload.SessionId, out var sessionGuid) ||
                                    !MessageValidation.IsValidBase64(payload.EphemeralPublicKey, 32, 1024) ||
                                    !MessageValidation.IsValidBase64(payload.Nonce, 16, 64) ||
                                    !MessageValidation.IsValidBase64(payload.Signature, 8, 1024) ||
                                    !hub.TryGetSession(sessionGuid, out var route) ||
                                    route.HostId != connection.DeviceId ||
                                    !hub.TryGet(route.ControllerId, out var controller))
                                {
                                    await SendErrorAsync(connection, "invalid_session", global::CSIViewer.Core.Localization.Text("Sesión no válida"), context.RequestAborted);
                                    break;
                                }

                                var hostDevice = await registry.GetAsync(connection.DeviceId!, context.RequestAborted);
                                if (hostDevice is null)
                                {
                                    await SendErrorAsync(connection, "unknown_device", global::CSIViewer.Core.Localization.Text("Dispositivo no registrado"), context.RequestAborted);
                                    break;
                                }

                                var forwarded = payload with { HostPublicKey = hostDevice.PublicKey };
                                await SendJsonSafeAsync(
                                    controller,
                                    ProtocolJson.Create(MessageTypes.ConnectPending, forwarded, payload.SessionId),
                                    context.RequestAborted);
                                break;
                            }

                            case MessageTypes.ConnectResponse:
                            {
                                var payload = ProtocolJson.Payload<ConnectResponsePayload>(envelope);
                                if (payload is null ||
                                    !Guid.TryParse(payload.SessionId, out var sessionGuid) ||
                                    !MessageValidation.IsValidPermissionMask(payload.GrantedPermissions) ||
                                    !MessageValidation.IsValidReason(payload.Reason) ||
                                    !hub.TryGetSession(sessionGuid, out var route) ||
                                    route.HostId != connection.DeviceId)
                                {
                                    break;
                                }

                                var grantedPermissions = SessionPermissionPolicy.Sanitize(payload.GrantedPermissions);

                                route.Accepted = payload.Accepted;
                                if (hub.TryGet(route.ControllerId, out var controller))
                                {
                                    await SendJsonSafeAsync(
                                        controller,
                                        ProtocolJson.Create(MessageTypes.ConnectResult,
                                            new ConnectResultPayload(
                                                payload.SessionId,
                                                payload.Accepted,
                                                grantedPermissions,
                                                connection.DeviceId!,
                                                connection.DeviceName ?? connection.DeviceId!,
                                                payload.Reason),
                                            payload.SessionId),
                                        context.RequestAborted);
                                }

                                if (!payload.Accepted)
                                {
                                    hub.RemoveSession(sessionGuid);
                                    try
                                    {
                                        await registry.RecordSessionEndAsync(
                                            payload.SessionId, "rejected", payload.Reason ?? "rejected", context.RequestAborted);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning(ex, "No se pudo guardar el rechazo de la sesión {SessionId}", payload.SessionId);
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        await registry.AuditAsync("session.accepted", connection.DeviceId, route.ControllerId,
                                            $"Permisos: {grantedPermissions}", context.RequestAborted);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning(ex, "No se pudo guardar la auditoría de la sesión {SessionId}", payload.SessionId);
                                    }
                                }

                                break;
                            }

                            case MessageTypes.SessionEnd:
                            {
                                var payload = ProtocolJson.Payload<SessionEndPayload>(envelope);
                                if (payload is null ||
                                    !Guid.TryParse(payload.SessionId, out var sessionGuid) ||
                                    !MessageValidation.IsValidReason(payload.Reason) ||
                                    connection.DeviceId is null ||
                                    !hub.TryGetSession(sessionGuid, out var route))
                                {
                                    break;
                                }

                                if (!route.TryGetPeer(connection.DeviceId, out var peerId))
                                {
                                    break;
                                }

                                if (hub.TryGet(peerId, out var peer))
                                {
                                    await SendJsonSafeAsync(
                                        peer,
                                        ProtocolJson.Create(MessageTypes.SessionEnded,
                                            new SessionEndPayload(payload.SessionId, payload.Reason),
                                            payload.SessionId),
                                        context.RequestAborted);
                                }

                                hub.RemoveSession(sessionGuid);
                                try
                                {
                                    await registry.RecordSessionEndAsync(
                                        payload.SessionId, "ended", payload.Reason, context.RequestAborted);
                                    await registry.AuditAsync("session.ended", connection.DeviceId, peerId, payload.Reason,
                                        context.RequestAborted);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex,
                                        "No se pudo guardar el final de la sesión {SessionId}",
                                        payload.SessionId);
                                }

                                break;
                            }

                            case MessageTypes.Ping:
                            {
                                var payload = ProtocolJson.Payload<PingPayload>(envelope);
                                await connection.SendJsonAsync(ProtocolJson.Create(MessageTypes.Pong,
                                    new PongPayload(payload?.TimestampUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())),
                                    context.RequestAborted);
                                break;
                            }

                            case MessageTypes.PresenceSet:
                                break;

                            default:
                                await SendErrorAsync(connection, "unknown_message", $"Tipo desconocido: {envelope.Type}",
                                    context.RequestAborted);
                                break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (WebSocketException ex)
                {
                    logger.LogDebug(ex, "WebSocket closed");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "WebSocket handler failed");
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    hub.Remove(connection);

                    if (authenticated && connection.DeviceId is { } id)
                    {
                        if (!connection.Replaced)
                        {
                            await hub.BroadcastPresenceAsync(connection, false, CancellationToken.None);
                        }

                        // Clean only the routes owned by this exact WebSocket.
                        // A replaced old socket must not remove the routes that
                        // the device's new connection creates after reconnecting.
                        foreach (var route in hub.SessionsForConnection(connection.ConnectionId))
                        {
                            if (!route.TryGetPeer(id, out var peerId))
                            {
                                hub.RemoveSession(route.Id);
                                continue;
                            }

                            if (peerId != id && hub.TryGet(peerId, out var peer))
                            {
                                await SendJsonSafeAsync(
                                    peer,
                                    ProtocolJson.Create(
                                        MessageTypes.SessionEnded,
                                        new SessionEndPayload(route.Id.ToString(), "peer_disconnected")),
                                    CancellationToken.None);
                            }

                            hub.RemoveSession(route.Id);
                            await registry.RecordSessionEndAsync(
                                route.Id.ToString(), "disconnected", "peer_disconnected", CancellationToken.None);
                        }

                        if (!connection.Replaced)
                        {
                            try
                            {
                                await registry.TouchAsync(id);
                            }
                            catch
                            {
                                // Best effort.
                            }
                        }
                    }

                    logger.LogInformation(LogEvents.ConnectionClosed, "WebSocket connection closed for {DeviceId}", connection.DeviceId ?? "anonymous");
                }
            });

                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                _app = app;
                ActiveUrl = url;

                _upnpMessages.Clear();
                UpnpFailed = false;
                if (settings.EnableUpnp)
                {
                    if (!settings.ListenOnAllInterfaces)
                    {
                        UpnpFailed = true;
                        _upnpMessages.Add(global::CSIViewer.Core.Localization.Text("UPnP requiere activar las conexiones desde otros equipos."));
                    }
                    else
                    {
                        var tcpMapping = await _upnpTcp.MapAsync(
                                settings.Port,
                                settings.EffectiveUpnpExternalPort,
                                "TCP",
                                cancellationToken)
                            .ConfigureAwait(false);
                        UpnpFailed |= !tcpMapping.Success;
                        _upnpMessages.Add(tcpMapping.Message);

                        if (settings.EnableStunServer)
                        {
                            var udpMapping = await _upnpStun.MapAsync(
                                    settings.StunPort,
                                    settings.StunPort,
                                    "UDP",
                                    cancellationToken)
                                .ConfigureAwait(false);
                            UpnpFailed |= !udpMapping.Success;
                            _upnpMessages.Add(udpMapping.Message);
                        }
                    }

                    foreach (var message in _upnpMessages)
                    {
                        if (asWindowsService)
                        {
                            ServerServiceLog.Write("UPnP: " + message);
                        }
                    }
                }
            }
            catch
            {
                if (app is not null)
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WaitForShutdownAsync(CancellationToken cancellationToken = default)
    {
        var app = _app;
        if (app is not null)
        {
            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var app = _app;
            _app = null;
            ActiveUrl = null;
            if (app is null)
            {
                _loggingOptions = null;
                return;
            }

            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                try
                {
                    await _upnpTcp.UnmapAsync(stopTimeout.Token).ConfigureAwait(false);
                    await _upnpStun.UnmapAsync(stopTimeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    // UPnP cleanup is best effort.
                }

                app.Logger.LogInformation(LogEvents.AppStopping, "CSIViewer Servidor detenido.");
                await app.StopAsync(stopTimeout.Token).ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
                _loggingOptions = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _upnpTcp.Dispose();
        _upnpStun.Dispose();
        _gate.Dispose();
    }
    private static X509Certificate2? FindCertificate(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return null;
        }

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly);
                var certificates = store.Certificates.Find(
                    X509FindType.FindByThumbprint,
                    thumbprint,
                    validOnly: false);
                var certificate = certificates
                    .Cast<X509Certificate2>()
                    .FirstOrDefault(x => x.HasPrivateKey);
                if (certificate is not null)
                {
                    return certificate;
                }
            }
            catch
            {
                // Try the next store.
            }
        }

        return null;
    }

    private static Task<(WebSocketMessageType? Type, byte[] Data)> ReceiveMessageAsync(
        WebSocket socket,
        byte[] buffer,
        CancellationToken cancellationToken)
        => WebSocketMessageReader.ReadMessageAsync(socket, buffer, cancellationToken);

    private static async Task RelayBinaryAsync(
        ClientConnection sender,
        byte[] data,
        ClientHub hub,
        DeviceRegistry registry,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (sender.DeviceId is null ||
            !SessionFrame.TryReadHeader(data, out var sessionId, out var kind, out _, out _) ||
            !hub.TryGetSession(sessionId, out var route))
        {
            return;
        }

        if (!route.TryGetPeer(sender.DeviceId, out var peerId) ||
            peerId == sender.DeviceId ||
            !hub.TryGet(peerId, out var peer))
        {
            return;
        }

        try
        {
            if (kind == SessionFrameKind.Video)
            {
                // Video is disposable. A viewer that stops reading must not
                // stall the relay for the rest of the sessions.
                peer.TrySendRaw(data, WebSocketMessageType.Binary);
                return;
            }

            if (kind == SessionFrameKind.FileChunk)
            {
                // File chunks are reliable payload: allow the upload to follow
                // the network rate instead of closing a slow but healthy peer.
                await peer.SendRawAsync(data, WebSocketMessageType.Binary, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await peer.SendRawAsync(data, WebSocketMessageType.Binary, sendTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The peer is not consuming control/input/chat data. Close only
                // that connection instead of blocking every other session.
                logger.LogWarning(
                    "Relay peer {PeerId} is not consuming data; closing the connection",
                    peerId);
                await peer.CloseAsync("peer_unresponsive").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Relay forwarding failed for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Forwards a small JSON message with a bounded wait. A peer that stops
    /// reading must not block the sender's receive loop or session cleanup.
    /// </summary>
    private static bool IsLocalAdminRequest(HttpContext context, ServerOptions options)
    {
        if (context.Connection.RemoteIpAddress is not { } address ||
            !System.Net.IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.EnrollmentCode))
        {
            return false;
        }

        var provided = context.Request.Headers["X-Enrollment-Code"].ToString();
        return FixedTimeEquals(provided, options.EnrollmentCode);
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        // Compare fixed-size hashes so the comparison does not leak the
        // length of the expected enrollment code.
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(left ?? ""));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(right ?? ""));
        try
        {
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(a);
            CryptographicOperations.ZeroMemory(b);
        }
    }

    private static async Task<bool> SendJsonSafeAsync(
        ClientConnection connection,
        Envelope envelope,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await connection.SendJsonAsync(envelope, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // The peer is stale or unresponsive; its own receive loop will
            // clean the connection up.
            return false;
        }
    }

    private static Task SendErrorAsync(ClientConnection connection, string code, string message, CancellationToken cancellationToken)
    {
        // Connection admission failures use the dedicated ConnectError message
        // so the requesting client can finish its pending connection at once.
        var isConnectError = code is "invalid_connect" or "rate_limited" or "self_connect"
            or "host_offline" or "controller_busy" or "host_full" or "session_exists"
            or "unknown_device" or "host_unreachable" or "invalid_session";
        return isConnectError
            ? connection.SendJsonAsync(
                ProtocolJson.Create(MessageTypes.ConnectError, new ConnectErrorPayload(code, message)),
                cancellationToken)
            : connection.SendJsonAsync(
                ProtocolJson.Create(MessageTypes.Error, new ErrorPayload(code, message)),
                cancellationToken);
    }
}
