using System.ComponentModel;
using System.Text;
using CSIViewer.Server;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class ServerServiceManagerTests
{
    [TestMethod]
    public void ServiceInstallationRejectsPortableUserWritableLocation()
    {
        Assert.Throws<UnauthorizedAccessException>(() => ServerServiceManager.ValidateServiceInstallation(
            Path.Combine(Path.GetTempPath(), "synthetic-portable", "CSIViewerServidor.exe")));
    }

    [TestMethod]
    public void InstallScriptContainsVerifierNotEnrollmentSecretAndEscapesOnlyOnce()
    {
        const string secret = "Synthetic-secret-not-in-commandline-1234";
        var script = ServerServiceManager.BuildInstallScript(@"C:\Program Files\CSIViewer\CSIViewerServidor.exe",
            new ServerRuntimeSettings { EnrollmentCode = secret, DatabasePath = @"C:\O'Brien\server.db" });
        Assert.IsFalse(script.Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("--enrollment-code", StringComparison.Ordinal));
        StringAssert.Contains(script, @"C:\O''Brien\server.db");
        Assert.IsFalse(script.Contains("O''''Brien", StringComparison.Ordinal));
        var match = System.Text.RegularExpressions.Regex.Match(script, "--enrollment-verifier=\"([^\"]+)\"");
        Assert.IsTrue(match.Success);
        var options = new ServerOptions { EnrollmentCode = "", EnrollmentVerifier = match.Groups[1].Value };
        Assert.IsTrue(ServerHost.VerifyEnrollment(secret, options));
        Assert.IsFalse(ServerHost.VerifyEnrollment("wrong", options));
        Assert.IsFalse(ServerHost.VerifyEnrollment(options.EnrollmentVerifier, options), "The verifier itself is not a reusable enrollment code.");
    }

    [TestMethod]
    public void EmptyEnrollmentAndDisabledOptionsAreExplicitInServiceArguments()
    {
        var script = ServerServiceManager.BuildInstallScript(@"C:\App\Server.exe",
            new ServerRuntimeSettings { EnrollmentCode = "", DatabasePath = @"C:\Data\server.db" });
        StringAssert.Contains(script, "--enrollment-verifier=\"\"");
        foreach (var option in new[] { "--local-only", "--no-upnp", "--no-stun", "--no-https" })
            StringAssert.Contains(script, option);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("malformed")]
    [DataRow("YQ==:Yg==")]
    public void InvalidVerifierFailsClosed(string verifier)
    {
        Assert.IsFalse(ServerHost.VerifyEnrollment("wrong", new ServerOptions { EnrollmentCode = "", EnrollmentVerifier = verifier }));
        if (verifier.Length > 0)
            Assert.ThrowsExactly<ArgumentException>(() => ServerServiceManager.BuildInstallScript(@"C:\App\Server.exe",
                new ServerRuntimeSettings { EnrollmentCode = "", EnrollmentVerifier = verifier, DatabasePath = @"C:\Data\server.db" }));
    }

    [TestMethod]
    [DataRow(1060, false, true)]
    [DataRow(5, false, false)]
    [DataRow(-1, false, false)]
    [DataRow(0, true, true)]
    public async Task QueryDistinguishesMissingFromFailure(int exitCode, bool installed, bool known)
    {
        var manager = new ServerServiceManager((start, _) =>
        {
            Assert.AreEqual(Path.Combine(Environment.SystemDirectory, "sc.exe"), start.FileName);
            return Task.FromResult((exitCode, "STATE : 4 RUNNING"));
        });
        var status = await manager.GetStatusAsync();
        Assert.AreEqual(installed, status.Installed);
        Assert.AreEqual(known, status.IsKnown);
    }

    [TestMethod]
    [DataRow("start")]
    [DataRow("stop")]
    [DataRow("uninstall")]
    public async Task ElevatedOperationsUseExitCodeWithoutUserTempWrites(string operation)
    {
        var manager = new ServerServiceManager((start, _) =>
        {
            Assert.IsTrue(Path.IsPathFullyQualified(start.FileName));
            Assert.AreEqual("runas", start.Verb);
            var encoded = start.Arguments.Split(' ')[^1];
            var script = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
            Assert.IsFalse(script.Contains("Set-Content", StringComparison.Ordinal));
            Assert.IsFalse(script.Contains("csiviewer-service-", StringComparison.Ordinal));
            Assert.IsFalse(script.Contains("Start-Service", StringComparison.Ordinal));
            Assert.IsFalse(script.Contains("Stop-Service", StringComparison.Ordinal));
            StringAssert.Contains(script, "WaitForStatus");
            StringAssert.Contains(script, Path.Combine(Environment.SystemDirectory, "sc.exe"));
            return Task.FromResult((0, ""));
        });
        var result = operation switch
        {
            "start" => await manager.StartAsync(),
            "stop" => await manager.StopAsync(),
            _ => await manager.UninstallAsync()
        };
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task PreCanceledOperationNeverLaunchesElevation()
    {
        var manager = new ServerServiceManager((_, _) => throw new AssertFailedException("Must not launch."));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.IsFalse((await manager.StartAsync(canceled.Token)).Success);
    }

    [TestMethod]
    public async Task NativeFailureAndDeniedUacAreNotSuccess()
    {
        var failed = new ServerServiceManager((_, _) => Task.FromResult((1, "")));
        Assert.IsFalse((await failed.StopAsync()).Success);
        var denied = new ServerServiceManager((_, _) => throw new Win32Exception(1223));
        Assert.IsFalse((await denied.StartAsync()).Success);
    }
}
