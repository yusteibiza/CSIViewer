using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using CSIViewer.Application;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Networking;
using CSIViewer.Security;

namespace CSIViewer.Services;

public sealed class IdentityService
{
    private readonly IIdentityStore _identityStore;
    private readonly ISettingsStore _settingsStore;
    private readonly ICustomPasswordStore _customPasswordStore;
    private readonly ServerApiClient _api;
    private readonly AppState _state;
    private readonly ILogger<IdentityService> _logger;
    private readonly LoggingOptions _loggingOptions;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public IdentityService(
        IIdentityStore identityStore,
        ISettingsStore settingsStore,
        ICustomPasswordStore customPasswordStore,
        ServerApiClient api,
        AppState state,
        ILogger<IdentityService> logger,
        LoggingOptions loggingOptions)
    {
        _identityStore = identityStore;
        _settingsStore = settingsStore;
        _customPasswordStore = customPasswordStore;
        _api = api;
        _state = state;
        _logger = logger;
        _loggingOptions = loggingOptions;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            _state.Settings = settings;
            _state.DeviceName = settings.DeviceName;
            _loggingOptions.MinimumLevel = settings.LogLevel.ToMicrosoft();
            _logger.LogInformation(
                LogEvents.AppStarted,
                "Configuración cargada. LogLevel={LogLevel}",
                settings.LogLevel);

            var stored = await _identityStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                using var identity = DeviceIdentity.CreateNew();
                stored = new StoredIdentity(
                    null,
                    settings.DeviceName,
                    identity.ExportPrivateKey(),
                    identity.PublicKeyBase64);
                await _identityStore.SaveAsync(stored, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                _state.Identity = DeviceIdentity.FromPrivateKey(stored.PrivateKeyPkcs8);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(stored.PrivateKeyPkcs8);
            }
            _state.DeviceId = stored.DeviceId ?? "—";
            _state.DeviceName = stored.DeviceName;
            var customPassword = await _customPasswordStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(customPassword))
            {
                await GeneratePasswordCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ApplyPasswordAsync(
                        customPassword,
                        customPassword,
                        temporary: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            _initialized = true;

            if (string.IsNullOrWhiteSpace(stored.DeviceId))
            {
                try
                {
                    await EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Initial device registration failed");
                    _state.Post(() => _state.ServerStatus = "Servidor no disponible");
                }
            }
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task EnsureRegisteredAsync(CancellationToken cancellationToken = default)
    {
        if (_state.Identity is null)
        {
            throw new InvalidOperationException("La identidad del dispositivo no está inicializada.");
        }

        if (IsValidDeviceId(_state.DeviceId))
        {
            return;
        }

        ServerEndpointSecurity.EnsureAllowed(
            _state.Settings.ServerUrl,
            _state.Settings.AllowInsecureServer);

        var response = await _api.RegisterAsync(
            _state.Settings.ServerUrl,
            _state.DeviceName,
            _state.Identity.PublicKeyBase64,
            _state.Settings.EnrollmentCode,
            cancellationToken).ConfigureAwait(false);

        await _identityStore.SaveAsync(
            new StoredIdentity(response.DeviceId, _state.DeviceName, _state.Identity.ExportPrivateKey(), _state.Identity.PublicKeyBase64),
            cancellationToken).ConfigureAwait(false);

        _state.Post(() => _state.DeviceId = response.DeviceId);
        _logger.LogInformation("Device registered with id {DeviceId}", response.DeviceId);
    }

    public async Task RegeneratePasswordAsync(CancellationToken cancellationToken = default)
    {
        await _customPasswordStore.SaveAsync(null, cancellationToken).ConfigureAwait(false);
        await GeneratePasswordCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangePasswordAsync(
        string customPassword,
        bool persist,
        CancellationToken cancellationToken = default)
    {
        customPassword = customPassword?.Trim() ?? "";
        if (customPassword.Length is < 8 or > 64)
        {
            throw new ArgumentException("La contraseña debe tener entre 8 y 64 caracteres.");
        }

        if (customPassword.Any(char.IsControl))
        {
            throw new ArgumentException("La contraseña contiene caracteres no permitidos.");
        }

        await ApplyPasswordAsync(
                customPassword,
                customPassword,
                temporary: false,
                cancellationToken)
            .ConfigureAwait(false);
        await _customPasswordStore.SaveAsync(persist ? customPassword : null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task GeneratePasswordCoreAsync(CancellationToken cancellationToken)
    {
        var password = TemporaryPasswordGenerator.Generate();
        await ApplyPasswordAsync(
                password.Raw,
                password.Display,
                temporary: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyPasswordAsync(
        string raw,
        string display,
        bool temporary,
        CancellationToken cancellationToken)
    {
        var (hash, salt) = await Task.Run(
            () =>
            {
                var value = PasswordHasher.Hash(raw, out var saltBase64);
                return (value, saltBase64);
            },
            cancellationToken).ConfigureAwait(false);

        var lifetimeMinutes = Math.Clamp(_state.Settings.TemporaryPasswordLifetimeMinutes, 0, 1440);
        var expiresUtc = temporary
            ? CredentialExpiry.Calculate(DateTimeOffset.UtcNow, lifetimeMinutes)
            : null;

        _state.Post(() =>
        {
            _state.TemporaryPasswordRaw = raw;
            _state.TemporaryPasswordDisplay = display;
            _state.TemporaryPasswordSalt = salt;
            _state.TemporaryPasswordHash = hash;
            _state.TemporaryPasswordExpiresUtc = expiresUtc;
            _state.UsesCustomPassword = !temporary;
        });
    }

    public async Task SetDeviceNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _state.DeviceName = name.Trim();
        _state.Settings.DeviceName = _state.DeviceName;
        await _settingsStore.SaveAsync(_state.Settings, cancellationToken).ConfigureAwait(false);

        if (_state.Identity is not null)
        {
            var privateKey = _state.Identity.ExportPrivateKey();
            try
            {
                await _identityStore.SaveAsync(
                    new StoredIdentity(
                        IsValidDeviceId(_state.DeviceId) ? _state.DeviceId : null,
                        _state.DeviceName,
                        privateKey,
                        _state.Identity.PublicKeyBase64),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }
        }
    }

    public static bool IsValidDeviceId(string value)
        => value.Length == 9 && value.All(char.IsAsciiDigit);
}
