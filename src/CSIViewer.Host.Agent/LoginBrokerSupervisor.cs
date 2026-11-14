using System.Diagnostics;
using System.Security.Principal;
using CSIViewer.Platform;

namespace CSIViewer.Host.Agent;

/// <summary>Optional machine login endpoint. The startup task still launches this agent.</summary>
internal sealed class LoginBrokerSupervisor : IDisposable
{
    private Process? _broker;
    private DateTime _startedFor;
    private DateTime? _failedFor;
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CSIViewer.Host.Login");
    private static string BinaryDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CSIViewer Host", "login");

    public void Ensure()
    {
        var config = Path.Combine(Root, "login.json");
        if (!File.Exists(config) || File.Exists(Path.Combine(Root, "stop.request")) || File.Exists(Path.Combine(Root, "revoked")))
        {
            Stop();
            return;
        }
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem || Process.GetCurrentProcess().SessionId != 0) return;
        var stamp = File.GetLastWriteTimeUtc(config);
        if (_broker is not null)
        {
            if (!_broker.HasExited) return;
            if (_broker.ExitCode != 0) _failedFor = _startedFor;
            _broker.Dispose();
            _broker = null;
        }
        if (_failedFor == stamp) return; // Fail closed; explicit admin enable touches the config timestamp.
        try
        {
            Validate(new DirectoryInfo(Root), true);
            Validate(new FileInfo(config), true);
            var exe = Path.Combine(BinaryDirectory, "CSIViewer.Host.Login.exe");
            ProtectedInstallation.ValidateProgramFilesParents(exe);
            ProtectedInstallation.ValidateTree(BinaryDirectory);
            _broker = Process.Start(new ProcessStartInfo(exe, $"--broker --agent-pid={Environment.ProcessId}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                WorkingDirectory = BinaryDirectory
            }) ?? throw new InvalidOperationException("Could not start machine login broker.");
            _ = _broker.Handle;
            _startedFor = stamp;
            ServiceLog.Write("Componente opcional de inicio de Windows iniciado por CSIViewerHost.");
        }
        catch (Exception ex)
        {
            _failedFor = stamp;
            ServiceLog.Write($"No se inicio el componente de login: {ex.GetType().Name}, HRESULT=0x{ex.HResult:X8}.");
            if (ex is InstallationValidationException validation)
                ServiceLog.Write($"Comprobacion de instalacion: {validation.Issue}; ruta: {validation.ItemPath}.");
            Stop();
        }
    }

    private static void Validate(FileSystemInfo info, bool privateData)
    {
        ProtectedInstallation.ValidateItem(info, privateData);
    }

    private void Stop()
    {
        var process = _broker;
        _broker = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                try { process.StandardInput.WriteLine("stop"); process.StandardInput.Close(); } catch { }
                if (!process.WaitForExit(8000)) process.Kill(entireProcessTree: true);
            }
        }
        catch { /* Only the process started and pinned by this supervisor is ever stopped. */ }
        finally { process.Dispose(); }
    }

    public void Dispose() => Stop();
}
