# CSIViewer

Aplicación de asistencia y control remoto para Windows construida con C#, .NET 10, WinUI 3, Windows App SDK y ASP.NET Core. Se compone de **CSIViewer**, **CSIViewer Servidor** y **CSIViewer Host** (ventana de anfitrión y servicio desatendido).

> **Estado:** build de desarrollo funcional, no listo para producción. La conexión, el consentimiento, el chat, el vídeo directo o por relay, el control de entrada y la transferencia de archivos funcionan de extremo a extremo. H.264 con pipeline directo, conexión LAN, HTTPS/WSS, servidor STUN, servidor y host como servicios de Windows y firma de MSIX con certificado de desarrollo están implementados. Quedan pendientes el hole punching ICE/TURN para Internet, la localización completa y el certificado de producción. Ver [estado detallado](docs/07-estado.md).

## Qué funciona hoy

- **Interfaz WinUI 3** con barra lateral, nueve secciones, Mica, tema claro/oscuro/sistema, título personalizado, icono CSIViewer y estados de sesión.
- **Identidad de dispositivo** ECDSA P-256 protegida con DPAPI, ID de 9 dígitos asignado por el servidor y contraseña temporal generada o personalizada, protegida con DPAPI si se decide conservar.
- **Servidor central ASP.NET Core** con SQLite, registro de dispositivos, autenticación por desafío, presencia, señalización WebSocket, sesiones y relay. Incluye interfaz WinUI de administración, bandeja de notificaciones, temas y apertura opcional del puerto en el router mediante UPnP.
- **Conexión LAN directa** por TCP con cifrado E2E y fallback automático al relay cuando no hay ruta directa.
- **Sesión E2E** ECDH P-256 + AES-256-GCM, verificación de contraseña PBKDF2 en el anfitrión y consentimiento con permisos seleccionables.
- **Control remoto** con captura Windows.Graphics.Capture con fallback GDI, codificación H.264 mediante Media Foundation con pipeline directo BGRA→H.264 (sin JPEG intermedio) y fallback JPEG, transmisión directa en LAN o por relay, selección de monitor, pantalla completa, ratón y teclado mediante `SendInput`.
- **Chat** cifrado, con historial local en SQLite y notificaciones.
- **Instancia única:** servidor, cliente y host detectan una instancia existente, activan su ventana y no crean una segunda.
- **Transferencia de archivos** por bloques de 1 MiB, SHA-256, archivo temporal y rename atómico, hasta 3 transferencias simultáneas, progreso individual/total, velocidad, pausa/reanudación, cancelación, reanudación automática tras reconexión, carpetas vacías, control de espacio y confirmación de sobrescritura.
- **Dispositivos, listas personalizadas, contactos e historial** con persistencia, búsqueda y edición. Puedes crear, editar y eliminar dispositivos manualmente, organizarlos en listas como «Clientes» o «Casa» y filtrarlos. Cada dispositivo puede guardar su contraseña cifrada con DPAPI para conectarse sin volver a escribirla. Los contactos agrupan dispositivos enlazados y permiten conectarse desde sus chips.
- **Configuración** de tema, servidor, calidad, FPS objetivo (presets de 12 a 120, incluidos 50/60/90/120), ancho, carpeta de descargas, límite de subida, notificaciones, inicio con Windows y privacidad. A FPS altos se descartan frames si el codificador o la red no pueden seguir el ritmo.
- **Modo desatendido** mediante el servicio de Windows CSIViewerHost: arranca con el sistema y mantiene el cliente activo en la sesión del usuario. La pantalla de inicio de sesión/secure desktop requiere UIAccess firmado y no se captura.
- **Logs JSONL estructurados** con niveles configurables, correlación por sesión, rotación, límite de almacenamiento, redacción de secretos y exportación de diagnóstico.
- **Banco de pruebas headless** para chat, vídeo y archivos.

## Requisitos

- Windows 10 2004 (build 19041) o superior; recomendado Windows 11.
- .NET SDK 10.0.404 o superior de la misma banda.
- Windows App SDK Runtime 2.6.5 (instalado por el paquete NuGet o el runtime del sistema).
- Visual Studio 2022/2026 con herramientas de escritorio, o CLI con Windows SDK.
- Puertos locales: 5080/TCP para el servidor de desarrollo.

## Compilar

```powershell
dotnet build CSIViewer.slnx -c Debug
dotnet test tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj -c Debug
```

La solución usa el formato `.slnx` de .NET 10. También se puede abrir en Visual Studio.

Para generar el paquete MSIX de prueba:

```powershell
.\scripts\build-msix.ps1
```

Para firmar el MSIX con un certificado de desarrollo:

```powershell
.\scripts\sign-msix.ps1 -GenerateDevCertificate -Trust
```

El certificado de producción debe ser emitido por una CA de confianza. El PFX privado de desarrollo no se conserva en el repositorio.

## Instaladores

Los instaladores de cliente, servidor y host se generan con Inno Setup 6:

```powershell
winget install JRSoftware.InnoSetup
pwsh -File installers\build-installers.ps1 -Version 1.0.0
```

Quedan en `artifacts\installers\`. Cada instalador permite elegir **usuario actual** o **todos los usuarios**; en modo todos los usuarios el servidor y el host ofrecen instalar su servicio de Windows. Detalles en [installers/README.md](installers/README.md).

## Ejecutar el servidor

El servidor tiene una interfaz de administración y un modo sin interfaz. Sin argumentos se abre la interfaz:

```powershell
# Interfaz gráfica (modo predeterminado): configurar, iniciar y detener el servidor
dotnet run --project src/CSIViewer.Server

# Igual que el anterior
dotnet run --project src/CSIViewer.Server -- --modo=ui

# Solo servidor en consola, sin interfaz
dotnet run --project src/CSIViewer.Server -- --modo=servidor

# Servicio de Windows (sin interfaz, arranque automático)
dotnet run --project src/CSIViewer.Server -- --modo=servicio
```

Instalación del servidor como servicio:

```powershell
.\scripts\install-server-service.ps1 -AllInterfaces
.\scripts\install-server-service.ps1 -Uninstall
```

La interfaz permite elegir tema del sistema/claro/oscuro, puerto, acceso local o por red, código de inscripción, nombre del servidor, ruta de SQLite y apertura del puerto en el router con UPnP. La configuración se guarda en `data\serversettings.json`, junto al ejecutable, y la reutiliza el modo consola. Al cerrar la ventana, el servidor permanece en la bandeja de notificaciones.

Desde la interfaz se puede activar **Iniciar con Windows**, que registra `CSIViewerServidor.exe --modo=ui` en el inicio de sesión del usuario y arranca el servidor automáticamente sin mostrar ninguna consola. El cliente dispone de **Iniciar con Windows** en Ajustes y, para equipos desatendidos, del servicio `CSIViewerHost` (inicio Automático) que lanza `CSIViewer.exe --host-mode --unattended` en la sesión del usuario.

El servidor escucha por defecto en `http://localhost:5080`. El código de inscripción predeterminado es `DEV-1234`. La base de datos predeterminada es `data\server.db`, junto al ejecutable; se puede cambiar con `--db`, con la interfaz o con `CSIVIEWER_SERVER_DB`.

Para HTTPS/WSS:

```powershell
.\scripts\new-server-certificate.ps1
CSIViewerServidor.exe --modo=servidor --https --cert-thumbprint=HUELLA
```

El certificado se busca en `CurrentUser\My` y `LocalMachine\My`. Para desplegar en varios equipos, crea el certificado con `-Machine` como administrador e importa el `.cer` público en los clientes.

El servidor incluye un servidor STUN UDP opcional para descubrir la dirección pública:

```powershell
CSIViewerServidor.exe --modo=servidor --stun --stun-port=3478
```

Abre el puerto UDP 3478 en el firewall/router si quieres usarlo desde Internet. El hole punching ICE completo sigue pendiente.

> En producción se debe configurar Kestrel con HTTPS (`ASPNETCORE_URLS=https://...` y un certificado válido) y usar una URL `https://`/`wss://` en el cliente. El build de desarrollo usa HTTP para facilitar las pruebas locales.

## Ejecutar el cliente

```powershell
dotnet run --project src/CSIViewer
```

O ejecutar directamente:

```text
src/CSIViewer/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CSIViewer.exe
```

Para probar dos clientes en el mismo equipo, usa directorios de datos distintos:

```powershell
$env:CSIVIEWER_DATA_DIR="$env:TEMP\CSIViewer-A"; .\CSIViewer.exe
$env:CSIVIEWER_DATA_DIR="$env:TEMP\CSIViewer-B"; .\CSIViewer.exe
```

## Modo desatendido

Publica el cliente y el servicio:

```powershell
.\scripts\publish-client.ps1
```

Instala el servicio como administrador:

```powershell
.\scripts\install-host-service.ps1
```

También puedes hacerlo desde **Configuración → Modo desatendido** en la propia ventana del host; la aplicación pedirá UAC. El servicio arranca con Windows y lanza `CSIViewer.Host.exe --unattended` en cada sesión de usuario activa (consola o RDP). El host queda en la bandeja y se conecta automáticamente al servidor configurado.

`CSIViewer.Host.exe` es la ventana autónoma del anfitrión. Muestra el ID y la contraseña del equipo, y su configuración incluye **tema, URL del servidor, permitir HTTP remoto, calidad de imagen, modo desatendido y política de solicitudes** (preguntar, aceptar automáticamente o rechazar automáticamente). Aceptar automáticamente sigue exigiendo la contraseña. El servicio `service\CSIViewer.Host.Service.exe` solo lo arranca en la sesión del usuario. El host **no necesita ni incluye el visor CSIViewer**: tiene su propia identidad y sus datos en `%LOCALAPPDATA%\CSIViewer.Host`.

> Limitación: sin un componente UIAccess firmado no se puede capturar la pantalla de inicio de sesión ni el escritorio seguro de UAC. El modo desatendido funciona después de que el usuario haya iniciado sesión.

Cada instancia se registra con su propia identidad y recibe un ID distinto. Copia el ID y la contraseña temporal del equipo anfitrión, abre **Control remoto** en el otro y conéctate.

## Probar sin interfaz

El harness usa las mismas clases de identidad, criptografía, sesión y transferencia que la UI:

```powershell
# Terminal 1: servidor
dotnet run --project src/CSIViewer.Server

# Terminal 2: anfitrión (escribe host.json con ID y contraseña)
$env:CSIVIEWER_DATA_DIR="$env:TEMP\CSIViewer-Host"
dotnet run --project tools/CSIViewer.TestHarness -- host

# Terminal 3: controlador de chat
$info = Get-Content "$env:TEMP\CSIViewerHarness\host\host.json" | ConvertFrom-Json
dotnet run --project tools/CSIViewer.TestHarness -- controller $info.DeviceId $info.TemporaryPasswordRaw

# Controlador de vídeo
dotnet run --project tools/CSIViewer.TestHarness -- viewer $info.DeviceId $info.TemporaryPasswordRaw

# Controlador de archivos (2 MiB, verificación SHA-256)
dotnet run --project tools/CSIViewer.TestHarness -- file $info.DeviceId $info.TemporaryPasswordRaw
```

Resultados esperados: `HOST_OK`, `CONTROLLER_OK`, `VIDEO_OK bytes=...`, `FILE_OK count=1`.

## Datos y configuración

Cada aplicación guarda todo en una carpeta `data` junto a su ejecutable. Se crea automáticamente en el primer arranque; en los siguientes se reutiliza.

- Cliente: `data\` (`identity.json` protegido con DPAPI, `custom-password.bin` si se guarda una contraseña personalizada, `settings.json`, `client.db`, `logs/`).
- Servidor: `data\` (`serversettings.json`, `server.db`, `logs/`). Si el ejecutable está en una carpeta protegida (por ejemplo `Program Files`), usa `%ProgramData%\CSIViewerServidor`.
- Host: `data\logs\host.log`. Si el ejecutable está en una carpeta protegida, usa `%ProgramData%\CSIViewerHost\logs`.
- Al arrancar se migran automáticamente los datos de las ubicaciones antiguas (`%LOCALAPPDATA%\CSIViewer`, `%LOCALAPPDATA%\CSIViewerServidor`, `%LOCALAPPDATA%\CSIViewerCliente`, `%LOCALAPPDATA%\RemoteDesktop*`) a `data` y se eliminan esas carpetas obsoletas.
- La carpeta de descargas propuesta por defecto es `%USERPROFILE%\Downloads\CSIViewer`.
- El ID de dispositivo no es un secreto. La contraseña temporal se genera localmente y se verifica en el anfitrión.
- El servidor no recibe claves de sesión ni contraseñas temporales.

## Arquitectura

- [Arquitectura](docs/01-arquitectura.md)
- [Proyectos y dependencias](docs/02-proyectos-dependencias.md)
- [Protocolo y seguridad](docs/03-protocolo-seguridad.md)
- [Plan de fases](docs/04-plan-implementacion.md)
- [Decisiones (ADR)](docs/05-adr.md)
- [Riesgos y límites de Windows](docs/06-riesgos-limites.md)
- [Estado actual](docs/07-estado.md)
- [Guía de ejecución](docs/08-guia-ejecucion.md)
- [Despliegue](docs/09-despliegue.md)
- [Rendimiento y estabilidad](docs/10-rendimiento.md)
- [Registro de eventos y diagnóstico](docs/11-registro-diagnostico.md)
- [Pruebas de estabilidad y concurrencia](docs/12-pruebas-estabilidad.md)
- [Auditoría técnica completa](docs/13-auditoria-tecnica.md)

## Limitaciones conocidas

1. **Vídeo:** Windows.Graphics.Capture + H.264 Media Foundation con fallback JPEG. La aceleración hardware del codificador se probará en la matriz de GPU.
2. **Conectividad:** la LAN usa TCP directo cuando es posible; Internet sigue usando el relay. No hay P2P UDP/STUN/TURN para Internet todavía.
3. **Archivos:** la reanudación funciona tras desconexiones y reinicios; la sobrescritura se confirma explícitamente y, si se elige guardar una copia, se renombra automáticamente.
4. **UIPI/UAC:** no se controlan ventanas elevadas ni Ctrl+Alt+Del; la pantalla segura de UAC no es capturable.
5. **Idioma:** interfaz en español; la selección de idioma está preparada pero no traducida.
6. **Bandeja del sistema e instancia única:** implementadas. **Firma MSIX de desarrollo:** implementada. **Certificado de producción y actualizador:** pendientes.
7. **Audio y portapapeles:** fuera del alcance actual.
8. **TLS:** el servidor de desarrollo usa HTTP; producción requiere HTTPS configurado.
9. **Pruebas:** hay 154 pruebas unitarias y una batería de estabilidad/concurrencia en PowerShell; la integración completa en MSTest sigue pendiente.

## Seguridad

La app exige consentimiento explícito, muestra un aviso de sesión activa y permite finalizarla. No incluye acceso oculto ni persistencia encubierta. Antes de distribuirla deben revisarse la criptografía, la firma de código, las licencias de códecs y la política de privacidad. Ver [protocolo y seguridad](docs/03-protocolo-seguridad.md).

## Licencia

Proyecto privado en desarrollo.
