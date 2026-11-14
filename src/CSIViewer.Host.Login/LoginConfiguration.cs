using System.Security.Cryptography;
using System.Text.Json;
using CSIViewer.Application;
using CSIViewer.Networking;
using CSIViewer.Security;

namespace CSIViewer.Host.Login;

internal sealed class LoginConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public bool Enabled { get; set; }
    public bool Experimental { get; set; } = true;
    public string ServerUrl { get; set; } = "";
    public bool AllowInsecureServer { get; set; }
    public string EnrollmentCode { get; set; } = "";
    public string DeviceName { get; set; } = Environment.MachineName + " (inicio Windows)";
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CSIViewer.Host.Login");
    public static string ConfigPath => Path.Combine(Root, "login.json");
    public static string StopPath => Path.Combine(Root, "stop.request");
    public static string RevokedPath => Path.Combine(Root, "revoked");
    public static string WorkerStopPath => Path.Combine(Root, "worker-stop.request");
    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CSIViewer Host", "login");

    public static LoginConfiguration Load()
    {
        InstallationSecurity.ValidateDirectory(Root, privateData: true);
        InstallationSecurity.ValidateFile(ConfigPath, privateData: true);
        var value = JsonSerializer.Deserialize<LoginConfiguration>(File.ReadAllText(ConfigPath)) ?? throw new InvalidDataException("Missing configuration.");
        if (!value.Enabled) throw new UnauthorizedAccessException("Machine login access is not explicitly enabled.");
        ServerEndpointSecurity.EnsureAllowed(value.ServerUrl, value.AllowInsecureServer);
        if (Convert.FromBase64String(value.PasswordSalt).Length != PasswordHasher.SaltSize ||
            Convert.FromBase64String(value.PasswordHash).Length != PasswordHasher.HashSize)
            throw new InvalidDataException("Invalid password verifier.");
        return value;
    }

    public AppSettings Settings() => new()
    {
        ServerUrl = ServerUrl, AllowInsecureServer = AllowInsecureServer, EnrollmentCode = EnrollmentCode, DeviceName = DeviceName,
        AutoConnect = true, AllowIncomingConnections = true, StartWithWindows = false,
        MaxConcurrentSessions = 1, PreferModernCapture = false, PreferH264 = false,
        PreferDirectLan = false, AutoResumeTransfers = false, StoreConnectionHistory = false,
        NotificationsEnabled = false, Fps = 10, MaxWidth = 1920, ImageQuality = 75
    };

    public static void Configure()
    {
        InstallationSecurity.RequireAdministrator();
        InstallationSecurity.ValidateInstallation();
        if (Directory.Exists(Root)) throw new IOException("A machine profile already exists; it will not be overwritten. Disable/remove the lab explicitly before reprovisioning.");
        Console.WriteLine("Acceso al inicio de Windows: componente SYSTEM. Version candidata, pendiente de validacion.");
        Console.WriteLine("No desactiva UAC ni guarda la contrasena de Windows. Usa una clave CSIViewer distinta.");
        Console.Write("Escribe ACTIVAR para autorizar el acceso desatendido de equipo: ");
        if (Console.ReadLine() != "ACTIVAR") throw new OperationCanceledException("Configuracion cancelada.");
        Console.Write("URL del servidor CSIViewer (https:// o http://): ");
        var url = Console.ReadLine()?.Trim() ?? "";
        var endpoint = ServerApiClient.NormalizeBaseUri(url);
        var allowInsecure = false;
        if (!ServerEndpointSecurity.IsSecure(endpoint) && !ServerEndpointSecurity.IsLocal(endpoint))
        {
            Console.WriteLine("ADVERTENCIA: HTTP remoto no protege la inscripcion, la senalizacion ni la autenticidad del servidor.");
            Console.WriteLine("Se mantiene el cifrado propio de la sesion, pero HTTPS o una VPN son recomendables, especialmente por Internet.");
            Console.Write("Escribe PERMITIR HTTP para autorizar este riesgo en este equipo: ");
            allowInsecure = Console.ReadLine() == "PERMITIR HTTP";
        }
        ServerEndpointSecurity.EnsureAllowed(url, allowInsecure);
        Console.Write("Server enrollment code: ");
        var enrollment = ReadSecret();
        char[]? first = null;
        char[]? second = null;
        try
        {
            Console.Write("New remote-access password (12-64 characters): ");
            first = ReadPassword();
            Console.Write("Repeat remote-access password: ");
            second = ReadPassword();
            if (first.Length is < 12 or > 64 || !first.AsSpan().SequenceEqual(second))
                throw new ArgumentException("Passwords must match and contain 12-64 characters.");
            var salt = RandomNumberGenerator.GetBytes(PasswordHasher.SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(first, salt, PasswordHasher.Iterations, HashAlgorithmName.SHA256, PasswordHasher.HashSize);
            try
            {
                InstallationSecurity.CreateProtectedDirectory(Root);
                var config = new LoginConfiguration
                {
                    Enabled = true, ServerUrl = url, AllowInsecureServer = allowInsecure, EnrollmentCode = enrollment,
                    PasswordSalt = Convert.ToBase64String(salt), PasswordHash = Convert.ToBase64String(hash)
                };
                using (var stream = InstallationSecurity.CreateProtectedFile(ConfigPath)) JsonSerializer.Serialize(stream, config, JsonOptions);
                Console.WriteLine("Provisioned. Only view/control of the physical console is allowed. No files, audio, clipboard or chat.");
                Console.WriteLine("Use the new machine ID shown in status.json, not the existing user's host/viewer ID.");
            }
            finally { CryptographicOperations.ZeroMemory(hash); CryptographicOperations.ZeroMemory(salt); }
        }
        finally
        {
            if (first is not null) Array.Clear(first);
            if (second is not null) Array.Clear(second);
        }
    }

    private static string ReadSecret()
    {
        var chars = ReadPassword();
        try { return new string(chars); }
        finally { Array.Clear(chars); }
    }

    private static char[] ReadPassword()
    {
        var buffer = new char[128];
        var count = 0;
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer[..count]; }
                if (key.Key == ConsoleKey.Backspace && count > 0) { buffer[--count] = '\0'; Console.Write("\b \b"); }
                else if (!char.IsControl(key.KeyChar) && count < buffer.Length) { buffer[count++] = key.KeyChar; Console.Write('*'); }
            }
        }
        finally { Array.Clear(buffer); }
    }
}
