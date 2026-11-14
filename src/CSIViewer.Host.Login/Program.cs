using System.Text.Json;

namespace CSIViewer.Host.Login;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--configure"])) { LoginConfiguration.Configure(); return 0; }
            if (args.SequenceEqual(["--enable"]))
            {
                InstallationSecurity.RequireAdministrator();
                InstallationSecurity.ValidateInstallation();
                var configuration = LoginConfiguration.Load();
                if (configuration.AllowInsecureServer) Console.WriteLine("HTTP remoto habilitado por autorizacion explicita del administrador. Se recomienda HTTPS o VPN.");
                File.Delete(LoginConfiguration.StopPath);
                File.Delete(LoginConfiguration.RevokedPath);
                File.SetLastWriteTimeUtc(LoginConfiguration.ConfigPath, DateTime.UtcNow);
                Console.WriteLine("Machine access explicitly enabled by the administrator.");
                return 0;
            }
            if (args.SequenceEqual(["--disable"]))
            {
                InstallationSecurity.RequireAdministrator();
                InstallationSecurity.ValidateInstallation();
                InstallationSecurity.ValidateDirectory(LoginConfiguration.Root, privateData: true);
                File.WriteAllText(LoginConfiguration.StopPath, "disabled by administrator");
                Console.WriteLine("Machine access stopping. Existing user host/viewer are unchanged.");
                return 0;
            }
            if (args.SequenceEqual(["--broker"]))
            {
                InstallationSecurity.RequireSystem(interactive: false);
                InstallationSecurity.ValidateInstallation();
                var config = LoginConfiguration.Load();
                MachineBroker.Run(config);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--broker" && args[1].StartsWith("--agent-pid=", StringComparison.Ordinal) &&
                int.TryParse(args[1][12..], out var pid) && pid > 0)
            {
                InstallationSecurity.RequireSystem(interactive: false);
                InstallationSecurity.ValidateInstallation();
                using var parent = System.Diagnostics.Process.GetProcessById(pid);
                _ = parent.Handle;
                var expected = Path.GetFullPath(Path.Combine(LoginConfiguration.InstallDirectory, "..", "agent", "CSIViewer.Host.Agent.exe"));
                if (parent.SessionId != 0 || !string.Equals(parent.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Unexpected launcher process.");
                var stop = Console.In.ReadLineAsync(); // Private anonymous pipe owned by the SYSTEM agent; stop only.
                MachineBroker.Run(LoginConfiguration.Load(), () => !parent.HasExited && !stop.IsCompleted);
                return 0;
            }
            if (args.SequenceEqual(["--worker"]))
            {
                InstallationSecurity.RequireSystem(interactive: true);
                InstallationSecurity.ValidateInstallation();
                var config = LoginConfiguration.Load();
                return LoginRuntime.Run(config);
            }
            Console.Error.WriteLine("Explicit modes: --configure, --enable, --disable (administrator); --broker (SYSTEM/session0); --worker (broker only). Candidate build, not yet validated on Winlogon.");
            return 2;
        }
        catch (Exception ex)
        {
            // No configuration, passwords, key events or exception payloads in logs.
            Console.Error.WriteLine($"Login endpoint failed: {ex.GetType().Name}, HRESULT=0x{ex.HResult:X8}");
            try
            {
                if (Directory.Exists(LoginConfiguration.Root))
                {
                    InstallationSecurity.ValidateDirectory(LoginConfiguration.Root, privateData: true);
                    File.WriteAllText(Path.Combine(LoginConfiguration.Root, "failure.json"), JsonSerializer.Serialize(new
                    { Time = DateTimeOffset.UtcNow, Error = ex.GetType().Name, ex.HResult,
                        NativeError = (ex as System.ComponentModel.Win32Exception)?.NativeErrorCode }));
                }
            }
            catch { /* Never fall back to an unprotected path. */ }
            return 1;
        }
    }
}
