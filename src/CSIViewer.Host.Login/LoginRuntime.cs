using System.Diagnostics;
using System.Text.Json;
using CSIViewer.Application;
using CSIViewer.Core;
using CSIViewer.Infrastructure;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CSIViewer.Host.Login;

internal static class LoginRuntime
{
    public static int Run(LoginConfiguration config)
    {
        using var singleton = new Mutex(true, @"Global\CSIViewer.Host.Login.Worker", out var created);
        if (!created) throw new InvalidOperationException("A machine worker already exists.");
        var session = Process.GetCurrentProcess().SessionId;
        if ((uint)session != MachineBroker.ActiveConsole) throw new UnauthorizedAccessException("Not the physical console session.");
        var data = Path.Combine(LoginConfiguration.Root, "runtime");
        Directory.CreateDirectory(data);
        AppPaths.SetDataDirectory(data);
        if (!string.Equals(AppPaths.DataDirectory, data, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("No fallback machine data directory is allowed.");

        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var context = new ApplicationContext();
        var exitCode = 0;
        dispatcher.BeginInvoke(async () =>
        {
            ServiceProvider? provider = null;
            try
            {
                var services = new ServiceCollection();
                services.AddCSIViewerInfrastructure();
                services.AddCSIViewerClient();
                services.AddSingleton<ISettingsStore>(new FixedSettings(config.Settings()));
                services.AddSingleton(new HostPasswordVerifier(config.PasswordSalt, config.PasswordHash));
                services.AddSingleton(new HostDesktopOptions { SystemLoginDesktop = true });
                services.AddSingleton(new ChatHistoryOptions { RetainHistory = false });
                var consent = new LoginConsent(session);
                services.AddSingleton<IConsentService>(consent);
                services.AddSingleton<INotificationService, NoNotifications>();
                services.AddLogging(builder => builder.AddProvider(new JsonLinesFileLoggerProvider(
                    AppPaths.LogsDirectory, new LoggingOptions { MinimumLevel = LogLevel.Warning }, "login-host")));
                provider = services.BuildServiceProvider();
                var state = provider.GetRequiredService<AppState>();
                state.UiInvoker = action => { if (!dispatcher.IsDisposed) dispatcher.Invoke(action); };
                var client = provider.GetRequiredService<RemoteClient>();
                using var indicator = new Form
                {
                    Text = "CSIViewer - acceso remoto del equipo", ControlBox = false, TopMost = true,
                    Width = 480, Height = 125, StartPosition = FormStartPosition.CenterScreen
                };
                using var label = new Label { Text = "Sesion remota activa", Dock = DockStyle.Top, Height = 30 };
                using var revoke = new Button { Text = "Revocar acceso remoto", Dock = DockStyle.Bottom, Height = 35 };
                indicator.Controls.AddRange([label, revoke]);
                async void Revoke(object? sender, EventArgs args)
                {
                    consent.Enabled = false;
                    var persisted = false;
                    try { File.WriteAllText(LoginConfiguration.RevokedPath, "revoked locally"); persisted = true; }
                    catch { /* Remain denied in memory; never report persistent revocation as successful. */ }
                    try { await client.EndCurrentSessionsAsync("Acceso revocado localmente."); }
                    finally
                    {
                        label.Text = persisted
                            ? "Acceso revocado. Un administrador debe volver a habilitarlo."
                            : "Revocado ahora, pero no se pudo guardar. Deten el laboratorio como administrador.";
                        revoke.Enabled = false;
                        indicator.Show();
                    }
                }
                revoke.Click += Revoke;
                indicator.FormClosing += (sender, args) =>
                {
                    args.Cancel = true; // Closing the indicator must not hide an active SYSTEM session.
                    if (consent.Enabled) Revoke(sender, args);
                };
                client.SessionStarted += (_, _) => dispatcher.BeginInvoke(() => indicator.Show());
                client.SessionEnded += (_, _) => dispatcher.BeginInvoke(() => { if (consent.Enabled) indicator.Hide(); });
                using var tray = new NotifyIcon { Icon = SystemIcons.Shield, Text = "CSIViewer - host de inicio de Windows", Visible = true };
                using var menu = new ContextMenuStrip();
                menu.Items.Add("Revocar acceso remoto", null, Revoke);
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += (_, _) => { label.Text = $"ID de equipo: {state.DeviceId}"; indicator.Show(); };

                await provider.GetRequiredService<ClientBootstrap>().InitializeAsync();
                var retryAt = DateTimeOffset.UtcNow.AddSeconds(30);
                while (!File.Exists(LoginConfiguration.StopPath) && !File.Exists(LoginConfiguration.WorkerStopPath) && File.Exists(LoginConfiguration.ConfigPath) &&
                       (uint)session == MachineBroker.ActiveConsole)
                {
                    if (File.Exists(LoginConfiguration.RevokedPath) && consent.Enabled)
                    {
                        consent.Enabled = false;
                        await client.EndCurrentSessionsAsync("Acceso revocado por el administrador.");
                    }
                    if (!state.IsServerOnline && !state.IsReconnecting && DateTimeOffset.UtcNow >= retryAt)
                    {
                        retryAt = DateTimeOffset.UtcNow.AddSeconds(30);
                        try { await client.ConnectToServerAsync(); }
                        catch { /* Network loss does not enable access or require a process restart. */ }
                    }
                    var temporary = Path.Combine(LoginConfiguration.Root, "status.tmp");
                    File.WriteAllText(temporary, JsonSerializer.Serialize(new
                    { Time = DateTimeOffset.UtcNow, MachineId = state.DeviceId, Online = state.IsServerOnline,
                        WindowsSession = session, Sessions = state.ActiveSessionCount, Revoked = !consent.Enabled,
                        Experimental = true, PasswordsStored = false }));
                    File.Move(temporary, Path.Combine(LoginConfiguration.Root, "status.json"), overwrite: true);
                    await Task.Delay(1000);
                }
                consent.Enabled = false;
                await client.EndCurrentSessionsAsync("El host de equipo se esta deteniendo.");
            }
            catch (Exception ex)
            {
                exitCode = 1;
                File.WriteAllText(Path.Combine(LoginConfiguration.Root, "worker-failure.json"),
                    JsonSerializer.Serialize(new { Error = ex.GetType().Name, ex.HResult,
                        NativeError = (ex as System.ComponentModel.Win32Exception)?.NativeErrorCode, Time = DateTimeOffset.UtcNow }));
            }
            finally
            {
                try { if (provider is not null) await provider.DisposeAsync(); }
                finally { context.ExitThread(); }
            }
        });
        System.Windows.Forms.Application.Run(context);
        return exitCode;
    }

    private sealed class FixedSettings(AppSettings settings) : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) => throw new NotSupportedException("Machine host configuration is administrator-owned.");
    }

    private sealed class LoginConsent(int session) : IConsentService
    {
        public bool Enabled { get; set; } = true;
        public Task<ConsentDecision> RequestConsentAsync(ConsentRequest request, CancellationToken cancellationToken = default)
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var granted = cancellationToken.IsCancellationRequested ? SessionPermissions.None : LoginHostPolicy.Grant(
                request.RequestedPermissions, request.ChatOnly,
                Enabled && !File.Exists(LoginConfiguration.RevokedPath) && !File.Exists(LoginConfiguration.StopPath),
                identity.IsSystem, session, MachineBroker.ActiveConsole);
            return Task.FromResult(new ConsentDecision(granted != SessionPermissions.None, granted));
        }
        public Task<FileReceiveDecision> RequestFileReceiveAsync(string fromName, string fileName, long size,
            bool destinationExists, bool willOverwrite, CancellationToken cancellationToken = default) => Task.FromResult(FileReceiveDecision.Reject);
    }
    private sealed class NoNotifications : INotificationService { public void Show(string title, string message) { } }
}
