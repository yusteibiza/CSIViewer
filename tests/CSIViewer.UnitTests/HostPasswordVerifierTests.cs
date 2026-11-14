using CSIViewer.Application;
using CSIViewer.Infrastructure.Logging;
using CSIViewer.Networking;
using CSIViewer.Security;
using CSIViewer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class HostPasswordVerifierTests
{
    [TestMethod]
    public async Task MachineIdentityUsesOnlyProvisionedVerifierWithoutRecoverablePassword()
    {
        const string synthetic = "Synthetic-machine-password-1234";
        var hash = PasswordHasher.Hash(synthetic, out var salt);
        using var http = new HttpClient();
        var state = new AppState();
        var service = Create(state, http, new HostPasswordVerifier(salt, hash));
        try
        {
            await service.InitializeAsync();
            Assert.AreEqual("123456789", state.DeviceId);
            Assert.IsTrue(state.UsesCustomPassword);
            Assert.AreEqual("", state.TemporaryPasswordRaw);
            Assert.AreEqual("", state.TemporaryPasswordDisplay);
            Assert.IsNull(state.TemporaryPasswordExpiresUtc);
            Assert.IsTrue(PasswordHasher.Verify(synthetic, state.TemporaryPasswordSalt, state.TemporaryPasswordHash));
            Assert.IsFalse(PasswordHasher.Verify("wrong", state.TemporaryPasswordSalt, state.TemporaryPasswordHash));
        }
        finally { state.Identity?.Dispose(); }
    }

    [TestMethod]
    public async Task InvalidVerifierFailsBeforeOpeningAnyConnection()
    {
        using var http = new HttpClient();
        var state = new AppState();
        var service = Create(state, http, new HostPasswordVerifier("", ""));
        try { await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.InitializeAsync()); }
        finally { state.Identity?.Dispose(); }
    }

    private static IdentityService Create(AppState state, HttpClient http, HostPasswordVerifier verifier) =>
        new(new MemoryIdentity(), new MemorySettings(), new NoPasswordStore(), new ServerApiClient(http), state,
            NullLogger<IdentityService>.Instance, new LoggingOptions(), verifier);

    private sealed class MemoryIdentity : IIdentityStore
    {
        public Task<StoredIdentity?> LoadAsync(CancellationToken cancellationToken = default)
        {
            using var identity = DeviceIdentity.CreateNew();
            return Task.FromResult<StoredIdentity?>(new StoredIdentity("123456789", "Test machine", identity.ExportPrivateKey(), identity.PublicKeyBase64));
        }
        public Task SaveAsync(StoredIdentity identity, CancellationToken cancellationToken = default) => throw new AssertFailedException("No identity write expected.");
    }
    private sealed class MemorySettings : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings { AutoConnect = false });
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => throw new AssertFailedException("No settings write expected.");
    }
    private sealed class NoPasswordStore : ICustomPasswordStore
    {
        public Task<string?> LoadAsync(CancellationToken cancellationToken = default) => throw new AssertFailedException("Machine mode must not read a recoverable password.");
        public Task SaveAsync(string? password, CancellationToken cancellationToken = default) => throw new AssertFailedException("Machine mode must not store a recoverable password.");
    }
}
