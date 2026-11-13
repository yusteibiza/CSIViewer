namespace CSIViewer.Server;

public enum ServerRunMode
{
    Server,
    Ui,
    Service
}

public sealed class ServerCommandLine
{
    public ServerRunMode Mode { get; init; } = ServerRunMode.Ui;
    public bool ShowHelp { get; init; }
    public ServerRuntimeSettings Settings { get; init; } = new();
    public string[] RemainingArguments { get; init; } = [];

    public static ServerCommandLine Parse(string[] args)
    {
        var settings = ServerSettingsStore.Load();
        var mode = ServerRunMode.Ui;
        var showHelp = false;
        var autoStart = settings.AutoStartWithUi;
        var remaining = new List<string>();
        var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--modo", "--mode", "--port", "--url", "--urls", "--db", "--database",
            "--enrollment-code", "--code", "--server-name", "--name", "--theme",
            "--upnp-external-port", "--cert-thumbprint", "--stun-port", "--max-sessions-per-host",
            "--log-level", "--loglevel"
        };

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            var separator = argument.IndexOf('=');
            var key = separator >= 0 ? argument[..separator] : argument;
            var value = separator >= 0 ? argument[(separator + 1)..] : null;
            var lowerKey = key.ToLowerInvariant();

            if (value is null &&
                valueOptions.Contains(lowerKey) &&
                index + 1 < args.Length &&
                !args[index + 1].StartsWith('-'))
            {
                value = args[++index];
            }

            switch (lowerKey)
            {
                case "--modo":
                case "--mode":
                    mode = value?.Trim().ToLowerInvariant() switch
                    {
                        "ui" or "grafico" or "gráfico" => ServerRunMode.Ui,
                        "servicio" or "service" => ServerRunMode.Service,
                        "servidor" or "server" or "console" or "consola" => ServerRunMode.Server,
                        _ => mode
                    };
                    break;

                case "--port":
                    if (int.TryParse(value, out var port))
                    {
                        settings.Port = port;
                    }

                    break;

                case "--url":
                case "--urls":
                    settings.ExplicitUrl = value;
                    break;

                case "--db":
                case "--database":
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        settings.DatabasePath = value;
                    }

                    break;

                case "--enrollment-code":
                case "--code":
                    settings.EnrollmentCode = value ?? "";
                    break;

                case "--server-name":
                case "--name":
                    settings.ServerName = value ?? "";
                    break;

                case "--all-interfaces":
                    settings.ListenOnAllInterfaces = true;
                    break;

                case "--local-only":
                    settings.ListenOnAllInterfaces = false;
                    break;

                case "--upnp":
                    settings.EnableUpnp = true;
                    break;

                case "--no-upnp":
                    settings.EnableUpnp = false;
                    break;

                case "--upnp-external-port":
                    if (int.TryParse(value, out var upnpPort))
                    {
                        settings.UpnpExternalPort = upnpPort;
                    }

                    break;

                case "--https":
                    settings.UseHttps = true;
                    break;

                case "--no-https":
                    settings.UseHttps = false;
                    break;

                case "--cert-thumbprint":
                    settings.CertificateThumbprint = value ?? "";
                    settings.UseHttps = true;
                    break;

                case "--stun":
                    settings.EnableStunServer = true;
                    break;

                case "--no-stun":
                    settings.EnableStunServer = false;
                    break;

                case "--stun-port":
                    if (int.TryParse(value, out var stunPort))
                    {
                        settings.StunPort = stunPort;
                    }

                    break;

                case "--max-sessions-per-host":
                    if (int.TryParse(value, out var maxSessions))
                    {
                        settings.MaxSessionsPerHost = maxSessions;
                    }

                    break;

                case "--theme":
                    if (Enum.TryParse<ServerTheme>(value, ignoreCase: true, out var theme))
                    {
                        settings.Theme = theme;
                    }

                    break;

                case "--log-level":
                case "--loglevel":
                    if (Enum.TryParse<CSIViewer.Application.AppLogLevel>(
                            value,
                            ignoreCase: true,
                            out var logLevel))
                    {
                        settings.LogLevel = logLevel;
                    }

                    break;

                case "--autostart":
                    autoStart = true;
                    break;

                case "--no-autostart":
                    autoStart = false;
                    break;

                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        remaining.Add(argument);
                    }

                    break;
            }
        }

        settings.AutoStartWithUi = autoStart;
        settings.Normalize();
        return new ServerCommandLine
        {
            Mode = mode,
            ShowHelp = showHelp,
            Settings = settings,
            RemainingArguments = remaining.ToArray()
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            CSIViewer Servidor

            Uso:
              CSIViewerServidor.exe
                  Abre la interfaz gráfica (modo predeterminado) para configurar,
                  iniciar y detener el servidor.

              CSIViewerServidor.exe --modo=ui
                  Igual que sin argumentos: abre la interfaz gráfica.

              CSIViewerServidor.exe --modo=servidor
                  Ejecuta solo el servidor en consola, sin interfaz.

              CSIViewerServidor.exe --modo=servicio
                  Ejecuta el servidor como servicio de Windows.

            Opciones:
              --modo=ui | --modo=servidor | --modo=servicio
                                              Modo de ejecución (también --mode=...).
                                              Sin --modo, se usa la interfaz gráfica.
              --port=5080                     Puerto HTTP de escucha.
              --all-interfaces                Escucha en todas las interfaces de red.
              --local-only                    Escucha solo en localhost.
              --upnp                          Abre el puerto en el router con UPnP.
              --upnp-external-port=5080       Puerto externo de UPnP (opcional).
              --no-upnp                       Desactiva UPnP.
              --https                         Sirve HTTPS/WSS.
              --cert-thumbprint=HUELLA        Certificado con clave privada en el almacén.
              --no-https                      Vuelve a HTTP.
              --stun                          Activa el servidor STUN UDP.
              --stun-port=3478                Puerto UDP del servidor STUN.
              --no-stun                       Desactiva STUN.
              --max-sessions-per-host=10      Límite de sesiones simultáneas por host.
              --enrollment-code=DEV-1234      Código necesario para registrar equipos.
              --server-name=Nombre            Nombre visible del servidor (CSIViewer Servidor).
              --db=ruta\server.db             Ruta de la base de datos SQLite.
              --url=http://0.0.0.0:5080       URL explícita de escucha.
              --theme=system|light|dark       Tema inicial de la interfaz.
              --log-level=Information         Nivel de log: Debug, Information, Warning,
                                              Error o Critical.
              --autostart | --no-autostart    Iniciar el servidor al abrir la interfaz.
              --help                          Muestra esta ayuda.

            La interfaz guarda su configuración en:
              %LOCALAPPDATA%\CSIViewerServidor\serversettings.json

            Ejemplo:
              CSIViewerServidor.exe
              CSIViewerServidor.exe --modo=servidor --port=5080 --all-interfaces
            """);
    }
}
