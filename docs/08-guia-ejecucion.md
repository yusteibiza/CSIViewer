# Guía de ejecución y prueba

Esta guía describe cómo compilar, arrancar y probar la build de desarrollo actual.

---

## 1. Compilar

```powershell
dotnet build CSIViewer.slnxx -c Debug
```

La solución usa `.slnx` (formato de .NET 10). Si se abre con una versión anterior de Visual Studio, se puede crear una solución `.sln` clásica, pero el SDK fijado es 10.0.404.

Pruebas unitarias:

```powershell
dotnet test tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj -c Debug
```

Resultado actual: 154/154 pruebas superadas.

---

## 2. Servidor

El ejecutable del servidor tiene tres modos. Sin argumentos se abre la interfaz gráfica.

### 2.1 Modo con interfaz (predeterminado)

```powershell
dotnet run --project src/CSIViewer.Server
```

También puedes usar el script:

```powershell
.\scripts\run-server.ps1 -Mode ui
```

La ventana permite:

- Elegir tema del sistema, claro u oscuro.
- Configurar puerto, acceso local o por red, código de inscripción, nombre del servidor y ruta de SQLite.
- Abrir el puerto en el router con UPnP (si el router lo permite).
- Iniciar y detener el servidor.
- Guardar la configuración, que se reutiliza en modo consola.
- Activar **Iniciar con Windows** para arrancar el servidor con el inicio de sesión, sin consola.
- Cerrar la ventana y dejar el servidor en la bandeja de notificaciones.

La configuración se guarda en `data\serversettings.json`, junto al ejecutable del servidor. Si esa carpeta no es escribible (por ejemplo una instalación en `Program Files`), se usa `%ProgramData%\CSIViewerServidor`.

### 2.2 Modo sin interfaz

```powershell
dotnet run --project src/CSIViewer.Server -- --modo=servidor
```

O:

```powershell
.\scripts\run-server.ps1 -Mode servidor -Port 5080 -AllInterfaces
```

### 2.3 Modo servicio de Windows

```powershell
.\scripts\install-server-service.ps1 -AllInterfaces
.\scripts\install-server-service.ps1 -Uninstall
```

El servicio se instala como `CSIViewerServidor`, arranca automáticamente y usa `data\server.db` junto al ejecutable; si esa carpeta no es escribible, usa `%ProgramData%\CSIViewerServidor\server.db`. Sus logs JSON Lines quedan en `data\logs\csiviewer-servidor-*.log` o, en el caso anterior, en `%ProgramData%\CSIViewerServidor\logs`.

Opciones principales:

```text
--modo=ui | --modo=servidor
--port=5080
--all-interfaces
--local-only
--enrollment-code=DEV-1234
--server-name=CSIViewer Server
--db=C:\Ruta\server.db
--url=http://0.0.0.0:5080
--theme=system|light|dark
--upnp
--upnp-external-port=5080
--https
--cert-thumbprint=HUELLA
--stun
--stun-port=3478
--no-autostart
--help
```

Certificado de prueba:

```powershell
.\scripts\new-server-certificate.ps1
# Para el servicio y varios equipos, como administrador:
.\scripts\new-server-certificate.ps1 -Machine
```

- URL de desarrollo: `http://localhost:5080`.
- Salud: `http://localhost:5080/api/health`.
- Código de inscripción predeterminado: `DEV-1234`.
- Base de datos predeterminada: `data\server.db` junto al ejecutable.
- Para otra ruta: `--db=D:\CSIViewer\server.db` o `$env:CSIVIEWER_SERVER_DB="D:\CSIViewer\server.db"`.
- Para producción: configurar HTTPS con un certificado válido y usar `https://` en el cliente.

El servidor también puede ejecutarse como servicio de Windows; en ese caso debe usarse `--modo=servidor`. El paquete `Microsoft.Extensions.Hosting.WindowsServices` ya está referenciado. El instalador y la configuración del servicio quedan para la fase de distribución.

---

## 3. Cliente gráfico

```powershell
dotnet run --project src/CSIViewer
```

O el ejecutable:

```text
src/CSIViewer/bin/Debug/net10.0-windows10.0.19041.0/win-x64/CSIViewer.exe
```

### Dos clientes en el mismo equipo

Cada instancia debe usar un directorio de datos distinto para tener identidad propia:

```powershell
$env:CSIVIEWER_DATA_DIR="$env:TEMP\CSIViewer-A"
.\CSIViewer.exe

$env:CSIVIEWER_DATA_DIR="$env:TEMP\CSIViewer-B"
.\CSIViewer.exe
```

En la primera ejecución, cada cliente se registra en el servidor y recibe un ID de 9 dígitos. El código de inscripción del cliente está en **Configuración > Servidor** (`DEV-1234` por defecto).

### Conectar

1. En el equipo anfitrión, anota el ID y la contraseña temporal de **Inicio**.
2. En el otro equipo, abre **Control remoto**.
3. Introduce ID, contraseña y permisos.
4. Pulsa **Conectar**.
5. En el anfitrión, revisa la solicitud y selecciona los permisos que concede.
6. La sesión muestra el vídeo y el banner de sesión activa.

Para chat sin control, deja solo **Chat** marcado.

---

## 4. Pruebas headless

El harness usa el mismo código de identidad, criptografía, sesión y transferencia que la UI.

### Chat

```powershell
# Terminal 1
dotnet run --project src/CSIViewer.Server

# Terminal 2
$env:CSIVIEWER_DATA_DIR="$env:TEMP\CSIViewer-Host"
dotnet run --project tools/CSIViewer.TestHarness -- host

# Terminal 3
$info = Get-Content "$env:TEMP\CSIViewerHarness\host\host.json" | ConvertFrom-Json
dotnet run --project tools/CSIViewer.TestHarness -- controller $info.DeviceId $info.TemporaryPasswordRaw
```

Salida esperada: `HOST_READY`, `CONSENT`, `HOST_CHAT`, `HOST_OK`, `CONTROLLER_OK`.

### Vídeo

```powershell
dotnet run --project tools/CSIViewer.TestHarness -- viewer $info.DeviceId $info.TemporaryPasswordRaw
```

Salida esperada: `VIDEO_FRAME`, `VIDEO_OK bytes=...`.

### Archivos

```powershell
dotnet run --project tools/CSIViewer.TestHarness -- file $info.DeviceId $info.TemporaryPasswordRaw
```

Salida esperada: `HOST_TRANSFER ... Completed`, `FILE_OK count=1`. El archivo se guarda en `%TEMP%\CSIViewerHarness\host\downloads`.

### Conexión directa y relay

La sesión intenta TCP directo en LAN. Para forzar el relay y comprobar el fallback:

```powershell
dotnet run --project tools/CSIViewer.TestHarness -- relay $info.DeviceId $info.TemporaryPasswordRaw
```

La prueba aislada del transporte directo es:

```powershell
dotnet run --project tools/CSIViewer.TestHarness -- direct
```

Salida esperada: `DIRECT_OK hello`.

### STUN

```powershell
dotnet run --project tools/CSIViewer.TestHarness -- stun
```

Salida esperada: `STUN_OK <ip>:<puerto> via stun.l.google.com:19302`.

### Reanudación persistente

```powershell
dotnet run --project tools/CSIViewer.TestHarness -- resume $info.DeviceId $info.TemporaryPasswordRaw
```

Usa un archivo de 8 MiB y limita la subida a 512 KB/s. Si se interrumpe el anfitrión a mitad, al reiniciarlo y repetir el comando se reutiliza el archivo parcial `.rdpart` y se completa desde el último byte confirmado.

---

## 5. Datos locales

Cliente:

```text
data\
  identity.json      (clave privada protegida con DPAPI)
  settings.json
  client.db
  logs\
  ui-crash.log       (solo si hay un fallo de UI)
```

Servidor:

```text
data\
  serversettings.json
  server.db
  logs\
```

La carpeta se crea automáticamente en el primer arranque. En el primer inicio se migran los datos antiguos de `%LOCALAPPDATA%\CSIViewer*` y `%LOCALAPPDATA%\RemoteDesktop*`, y esas carpetas se eliminan. Si el ejecutable está en una carpeta protegida, el servidor usa `%ProgramData%\CSIViewerServidor` y el host `%ProgramData%\CSIViewerHost`.

Para reiniciar la identidad de un cliente, cierra la app, mueve o elimina `identity.json` y vuelve a abrir. Se registrará un dispositivo nuevo. Para borrar todo, elimina el directorio de datos.

---

## 6. Diagnóstico

- **Servidor no disponible:** comprueba `http://localhost:5080/api/health` y la URL de Configuración.
- **No aparece el ID:** el registro falla; revisa el código de inscripción y los logs.
- **No conecta:** confirma que ambos clientes están "En línea" y que el ID tiene 9 dígitos.
- **Vídeo negro:** puede ser una ventana DRM, un monitor desconectado o un fallo de captura; revisa el log y el banner de sesión.
- **Archivo bloqueado:** el receptor renombra automáticamente si el destino existe; revisa permisos y espacio.
- **Firewall:** el relay usa HTTP/WebSocket en 5080; en producción se debe usar 443 con TLS.
- **Logs:** `data\logs\csiviewer-AAAAAMMDD.log` junto al ejecutable del cliente.
- **UI Automation:** se usó para verificar la sesión y el FPS; no forma parte del producto.

---

## 7. Limitaciones actuales de la build

- H.264 por Media Foundation con fallback JPEG; la aceleración hardware del codificador aún requiere matriz de GPU.
- La LAN intenta TCP directo; Internet usa el relay. La consulta STUN de diagnóstico funciona, pero no hay transporte P2P/TURN para Internet.
- La reanudación de archivos tras reiniciar la app está implementada con archivos parciales `.rdpart`; la confirmación de sobrescritura se sustituye por renombrado automático.
- No hay control de ventanas elevadas ni Ctrl+Alt+Del.
- La interfaz está en español.
- El servidor de desarrollo usa HTTP; producción requiere HTTPS.
- No hay MSIX firmado ni actualizador.

Consulta [estado](07-estado.md) para la lista completa.
