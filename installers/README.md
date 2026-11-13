# Instaladores de CSIViewer

Se generan tres instaladores:

| Instalador | Contenido | Modos |
|---|---|---|
| `CSIViewer-Setup-<versión>.exe` | Visor WinUI completo | Usuario actual / Todos los usuarios |
| `CSIViewerServidor-Setup-<versión>.exe` | Servidor WinUI + consola + servicio opcional | Usuario actual / Todos los usuarios |
| `CSIViewer.Host-Setup-<versión>.exe` | Host autónomo: ventana de anfitrión + servicio (sin visor) | Usuario actual / Todos los usuarios |

## Requisitos de compilación

- .NET 10 SDK.
- Inno Setup 6 (compilador `ISCC.exe`).
- PowerShell 7.

Si no está Inno Setup:

```powershell
winget install JRSoftware.InnoSetup
```

## Construcción

Desde la raíz del repositorio:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File installers\build-installers.ps1 -Version 1.0.0
```

El script:

1. Publica visor, servidor y host como `win-x64` autocontenidos.
2. Publica el host en dos partes: `CSIViewer.Host.exe` (ventana) y `service\CSIViewer.Host.Service.exe` (servicio).
3. Compila los tres instaladores con Inno Setup.
4. Los deja en `artifacts\installers\`.

El visor y el host son independientes: el host no necesita que CSIViewer esté instalado.

Para recompilar sin volver a publicar:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File installers\build-installers.ps1 -SkipPublish -Version 1.0.0
```

## Elección de ámbito

Al iniciar cualquier instalador, Inno Setup muestra el diálogo de modo de instalación:

- **Instalar solo para mí:** no requiere administrador; instala en
  `%LOCALAPPDATA%\Programs\...` y crea accesos en el perfil del usuario.
- **Instalar para todos los usuarios:** solicita UAC; instala en
  `%ProgramFiles%\...` y crea accesos comunes.

También se puede forzar por línea de comandos:

```powershell
CSIViewer-Setup-1.0.0.exe /CURRENTUSER
CSIViewer-Setup-1.0.0.exe /ALLUSERS
```

`/ALLUSERS` requiere elevación. `/CURRENTUSER` no.

## Servicio del servidor

En modo **todos los usuarios**, el instalador del servidor ofrece la tarea opcional:

```text
Instalar CSIViewer Servidor como servicio de Windows
```

Si se selecciona, pide un código de inscripción de 12–64 caracteres y un puerto, e instala el servicio `CSIViewerServidor` con la base de datos en:

```text
%ProgramData%\CSIViewerServidor\server.db
```

El código de inscripción se pasa al servicio mediante su línea de comandos. Para un despliegue real se recomienda sustituir esa configuración por un archivo protegido con ACL/DPAPI de máquina.

## Servicio del host

El instalador del host es completamente independiente: **no incluye ni requiere el visor CSIViewer**. Instala:

```text
{app}\CSIViewer.Host.exe              ventana de anfitrión
{app}\service\CSIViewer.Host.Service.exe  servicio desatendido
```

El acceso directo **CSIViewer Host** abre `CSIViewer.Host.exe`. En modo **todos los usuarios**, la tarea:

```text
Instalar CSIViewer Host como servicio de Windows
```

está **marcada por defecto**. El servicio `CSIViewerHost` arranca automáticamente y lanza `CSIViewer.Host.exe --unattended` en la sesión del usuario. No busca ni necesita el visor.

La configuración del host incluye **Permitir HTTP remoto (sin HTTPS)** para servidores DDNS o de confianza. Está desactivado por defecto; actívalo solo si el servidor no ofrece HTTPS/WSS.

El host tiene su propia identidad y sus propios datos, separados del visor. En `Program Files` usa:

```text
%LOCALAPPDATA%\CSIViewer.Host
```

Si ya está instalado sin servicio, se puede instalar después desde **Configuración → Modo desatendido** en la ventana del host, o desde una consola elevada:

```powershell
& "C:\Program Files\CSIViewer Host\install-host-service.ps1" `
  -ServiceExe "C:\Program Files\CSIViewer Host\service\CSIViewer.Host.Service.exe"
```

## Datos y desinstalación

- Los binarios se instalan en la carpeta elegida.
- En modo **usuario actual**, la carpeta de instalación es escribible y la aplicación usa su carpeta `data` junto al ejecutable.
- En modo **todos los usuarios**, los binarios quedan en `Program Files`; como esa carpeta no es escribible, el visor usa `%LOCALAPPDATA%\CSIViewer`, el host usa `%LOCALAPPDATA%\CSIViewer.Host` (identidad DPAPI por usuario) y el servidor usa `%ProgramData%\CSIViewerServidor`.
- Los datos de la aplicación (`data`, `settings.json`, base de datos, logs) se conservan al desinstalar para no perder identidad, historial ni configuración.
- En modo todos los usuarios, los datos del servidor quedan en `%ProgramData%\CSIViewerServidor`.
- Si se instaló un servicio, el desinstalador lo detiene y lo elimina.
- Para borrar también los datos, elimina manualmente la carpeta `data` correspondiente.

## Firma y SmartScreen

Los instaladores se generan **sin firmar**. Windows puede mostrar SmartScreen o pedir confirmación adicional. Para distribución real hay que firmar los `.exe` con un certificado de firma de código y firmar también los ejecutables publicados.

## Notas de despliegue

- El instalador no modifica las reglas del Firewall de Windows. Para acceso desde la LAN hay que permitir el puerto del servidor (5080/TCP por defecto).
- HTTPS/WSS se configura en la interfaz del servidor con un certificado instalado; el instalador no crea certificados de producción.
- El instalador no cambia el código de inscripción por defecto del servidor si se instala solo la aplicación; cámbialo antes de exponerlo.
- El servicio del servidor recibe el código de inscripción por línea de comandos; para producción se recomienda una configuración protegida por ACL/DPAPI de máquina.

## Verificación

Compilación de los tres instaladores:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File installers\build-installers.ps1 -Version 1.0.0
```

Prueba de instalación por usuario (sin administrador):

```powershell
$dir = "$env:TEMP\csiviewer-install-test"
& "artifacts\installers\CSIViewer-Setup-1.0.0.exe" `
  /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER `
  /DIR="$dir" /NOICONS
& "$dir\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

La instalación para todos los usuarios requiere UAC y no se puede validar de forma desatendida en un entorno sin elevación.
