# AI_HANDOFF — CSIViewer

Documento de transferencia de contexto para que otro modelo de IA continúe el proyecto sin repetir errores ni romper lo que funciona.

> **ÚLTIMA REVISIÓN — 13/11, 19:29:** corregida pérdida real de contraseñas de
> dispositivos al conectar/recibir sesión. PersistKnownDeviceAsync llamaba Upsert
> sin ProtectedPassword; ahora RecordConnectionAsync solo actualiza datos de
> conexión. Editor del host conserva también el secreto. Dos regresiones fallan
> antes/pasan después; **187/187 tests** Debug/Release y **22/22 casos reales**
> (incluido secreto ficticio conservado tras conectar). Ver informe
> [21-contrasenas-dispositivos-servidor-1.0.1.md](21-contrasenas-dispositivos-servidor-1.0.1.md).
> Instaladores nuevos 1.0.1: visor 19:26:42, host 19:27:55, servidor 19:29:00.
> Servidor publicado supera health en loopback aislado. **El usuario pidió borrar
> los tres instaladores sueltos 1.0.0 y se borraron; el ZIP 1.0.0 se conserva**.
> No se actualizaron instalaciones ni se recuperaron contraseñas ya perdidas.
> Regresión UI archivos 7/7; fullscreen primer intento falla ocultación y repetición
> sin cambios pasa 15/15 (variación no diagnosticada, ambas evidencias conservadas).

> **1.0.1 — ADMINISTRADOR DE ARCHIVOS:** implementados paneles local/remoto,
> navegación, destinos de sesión, Enviar/Recibir, actualización al completar y
> progreso plegable. Ver [20-administrador-archivos-1.0.1.md](20-administrador-archivos-1.0.1.md).
> Reutiliza transferencia/cifrado/consentimiento; espera confirmación final SHA-256;
> mantiene permisos direccionales. Migración aditiva Transfers.BrowserDestination.
> 183/183 tests Debug/Release; 21/21 casos de transferencia real, 7/7 UI en ambos
> y regresión fullscreen 15/15 Release. Requiere visor y host 1.0.1, servidor sin
> cambios. Copia e instaladores 1.0.0 conservados. Confirmación física del usuario
> pendiente; ninguna instalación actual se ha actualizado automáticamente.
> Instaladores 13/11: visor **19:16:04**, host **19:17:17**, nombres terminados
> en `1.0.1.exe`. Versiones de visor/host/agente 1.0.1.0 y hashes del backup y
> paquetes 1.0.0 comprobados sin cambios. Detalles/hashes en el informe 20.

> **VERSIONADO — 13/11, 18:39:** usuario confirma estado actual correcto y pide
> conservarlo como 1.0.0 antes del administrador de archivos de 1.0.1.
> Backup completo verificado (6636 archivos) en
> `artifacts/backups/CSIViewer-1.0.0-20261113-183951.zip`, con manifiesto SHA-256.
> Ver [19-versiones-backup.md](19-versiones-backup.md). Proyecto y empaquetado
> preparados entonces para 1.0.1; el administrador se implementó después (ver arriba).
> Instaladores 1.0.0 conservados.

> **ÚLTIMA ACTUALIZACIÓN — 18:01 del 13/11:** ver
> [18-fullscreen-borde-ventana.md](18-fullscreen-borde-ventana.md).
> Eliminados stroke/radio de SessionCard en fullscreen y borde/radio redundante de
> NavigationView; desactivado auto-padding de título en fullscreen. Nuevos botones
> locales minimizar/maximizar/restaurar en visor y host. Minimizar conserva fullscreen;
> maximizar/restaurar salen a ventana estándar; restaurar permite redimensionar.
> **15/15 comprobaciones WinUI Debug y Release publicado**, **168/168 tests** en ambos,
> builds sin advertencias/errores. Instaladores visor 18:00:00 y host 18:01:13.
> No se actualizaron aplicaciones instaladas. Confirmación visual en dos PCs pendiente.

> **ACTUALIZACIÓN ANTERIOR — 17:38 del 13/11:** ver
> [17-barra-fullscreen-cierre-remoto.md](17-barra-fullscreen-cierre-remoto.md).
> Barra de pantalla completa automática (1,5 s), reaparece arriba, con chincheta
> para fijarla; implementada en visor y host. Cierre remoto publica estado antes
> de E/S lenta, limpia la vista, invalida vídeo tardío y deja al dispatcher atender
> el cierre entre fotogramas. Dos regresiones fallan en la DLL anterior y pasan
> en la nueva. **168/168 tests** Debug/Release y **8/8 comprobaciones sobre el visor
> WinUI real**, también con el EXE Release publicado, incluidos cierre desde un
> anfitrión de diagnóstico y vuelta al formulario. Servidor/protocolo sin cambios.
> Instaladores nuevos: visor 17:38:03, host 17:38:25 (mismos nombres). Aplicaciones
> instaladas no actualizadas automáticamente; confirmación del usuario pendiente.

> **ESTADO MÁS RECIENTE — 16:13 del 13/11:** consultar
> [16-reparacion-video-validacion-sesion.md](16-reparacion-video-validacion-sesion.md).
> Se corrigió NV12 con altura codificada (1088 para 1080 visible), se activó
> correctamente baja latencia del decodificador y se protegió la cola contra
> pérdida de referencias H.264 y trabajos del monitor anterior. Debug/Release:
> 0 errores/advertencias; **166/166 tests** en ambos. Cliente de diagnóstico →
> servidor → host instalado: **8/8 comprobaciones** de entrada y vídeo en dos
> monitores, incluyendo fallback JPEG bajo saturación. No es todavía la
> confirmación manual del visor WinUI en dos equipos físicos.
> Instaladores nuevos: visor 16:12:24, host 16:12:51, mismos nombres de siempre.
> Hay que actualizar el visor; no se ejecutaron instaladores automáticamente.
> TeamViewer restaurado (servicio Running/Automatic y UI abierta). La conclusión
> anterior de que bastaba tener TeamViewer activo para bloquear CSIViewer era
> demasiado general: la sesión final pasó con su servicio activo y sin otro
> controlador; una consola elevada del propio diagnóstico también invalidó
> ensayos intermedios. No cambiar el inyector ni saltarse UIPI basándose en ellos.
> Repetición final usando las DLL exactas del payload publicado (hashes
> comprobados): 8/8 casos Passed=true. Evidencia definitiva en
> `artifacts/diagnostics/session-final-published-binaries.json` y `.log`.

> **Hallazgo más reciente (15:16):** el mismo InputInjector superó una prueba
> real de seis movimientos/clics, tres por monitor, con error máximo de 1 px,
> tras detener temporalmente TeamViewer completo con autorización del usuario.
> Con TeamViewer activo fallaba el ensayo. Servicio y UI restaurados después.
> No se cambió el inyector: hay interferencia demostrada en esta configuración,
> sin identificar su mecanismo. Ver [15-resultado-prueba-raton.md](15-resultado-prueba-raton.md).
> Sigue pendiente la validación extremo a extremo de CSIViewer sin TeamViewer;
> arrastre/rueda/teclado no se validaron (segundo UAC cancelado). La franja verde
> y la congelación de vídeo siguen abiertas. No reutilizar las hipótesis antiguas
> como causas verificadas ni afirmar que la aplicación completa está reparada.

> **Última petición de UI:** globos de chat recibidos grises a la izquierda y
> enviados azules también a la izquierda, con sangría izquierda de 28 px.
> Aplicado en ChatPage.xaml de visor y host; reemplaza la decisión anterior de
> alinear enviados a la derecha. Pendiente de confirmación visual del usuario.
> La prueba real de las 14:55 confirma recepción de ratón en host y llamada a
> SendInput, pero no control efectivo: ver el documento 14 para los datos.
> Ratón, franja verde e imagen congelada siguen sin resolver.
> Instaladores con el cambio de chat regenerados: visor 15:00:50 y host 15:01:12
> del 13/11/2026, mismos nombres en artifacts/installers. No incorporan una
> corrección del control remoto. Compilación Debug/Release sin errores ni
> advertencias; 159/159 pruebas en ambas configuraciones.

> **Actualización de diagnóstico (13/11/2026):** consultar
> [14-diagnostico-raton.md](14-diagnostico-raton.md). Se ha añadido traza
> correlacionada del ratón sin cambiar su mapeo ni su transporte. Los instaladores
> listados abajo no contienen esa nueva instrumentación. La atribución causal
> de los fallos a SetCursorPos/DPI en este documento es una hipótesis histórica,
> no una causa demostrada. El ratón sigue pendiente de validación real.

> **Continuación:** ya hay instaladores instrumentados nuevos del visor
> (14:32:45) y host (14:33:08), con hashes en el documento de diagnóstico.
> Build Debug/Release sin errores ni advertencias; 159/159 tests en ambos.
> La prueba local aislada no consiguió validar posiciones/clics: SendInput
> devolvió éxito sin observarse el cursor en el destino; un contraste nativo
> tampoco lo consiguió. No se ha identificado la causa ni se ha cambiado el
> mapeo/inyector. El script `scripts/collect-input-diagnostics.ps1` facilita
> recoger solo metadatos de ratón para la prueba física siguiente.

> **Importante:** este documento se ha creado a partir del estado real del repositorio y de la sesión de trabajo actual. **No existe un repositorio Git** en la carpeta del proyecto (`git status` devuelve `fatal: not a git repository`), por lo que no se ha podido consultar un historial de commits ni recuperar versiones antiguas del código. Los detalles de conversaciones anteriores que no están en el código o en este documento no se pueden recuperar y no se inventan aquí.

---

## 1. Estado general

### Objetivo del proyecto

CSIViewer es una aplicación de asistencia y control remoto para Windows, formada por tres productos independientes:

1. **CSIViewer (visor/cliente):** permite conectarse a otro equipo, ver su pantalla, controlarlo, chatear e intercambiar archivos.
2. **CSIViewer Host (anfitrión):** se instala en el equipo que recibe la asistencia; muestra su ID y contraseña, acepta conexiones y captura/controla el equipo.
3. **CSIViewer Servidor:** servidor central de señalización, presencia, sesiones y relay. Tiene interfaz WinUI de administración y modo consola/servicio de Windows.

El host y el visor son independientes: el host no requiere que el visor esté instalado en el mismo equipo.

### Tecnologías y versiones

| Elemento | Versión / valor |
|---|---|
| Lenguaje | C# `LangVersion=latest` |
| .NET | `net10.0` / `net10.0-windows10.0.19041.0` |
| SDK usado | .NET SDK 10.0.404 |
| UI | WinUI 3 / Windows App SDK 2.6.5 |
| Windows SDK BuildTools | 10.0.28000.2957 |
| Servidor web | ASP.NET Core / Kestrel |
| Persistencia | SQLite + EF Core 10.0.15 |
| Inyección Win32 | P/Invoke + Microsoft.Windows.CsWin32 0.3.371 |
| Pruebas | MSTest 4.5.3, Microsoft.NET.Test.Sdk 18.12.0 |
| Instaladores | Inno Setup 6 |
| Plataforma | Windows 10 2004 (19041) o superior; recomendado Windows 11 |

### Estructura real de la solución

`CSIViewer.slnx` contiene:

```
src/
  CSIViewer/                 Visor WinUI 3
  CSIViewer.Host/            Ventana del host WinUI 3
  CSIViewer.Host.Agent/      Agente de inicio (tarea programada SYSTEM)
  CSIViewer.Server/          Servidor ASP.NET Core + UI WinUI 3
  CSIViewer.Services/        Lógica de cliente/sesión/captura/entrada
  CSIViewer.Media/           Captura, códec H.264/JPEG, inyección de entrada
  CSIViewer.Protocol/        Mensajes, frames binarios y validación
  CSIViewer.Security/        Identidad, ECDH, AES-GCM, hashes
  CSIViewer.Networking/      WebSocket de señalización, transporte directo
  CSIViewer.Infrastructure/  SQLite, DPAPI, logs, settings, migraciones
  CSIViewer.Application/     Abstracciones y AppSettings
  CSIViewer.Core/            Modelos y mappers puros
tests/
  CSIViewer.UnitTests/       157 pruebas MSTest
tools/
  CSIViewer.TestHarness/     Banco de pruebas headless
```

### Proyectos y ensamblados

| Proyecto | Target | RID | Ensamblado |
|---|---|---|---|
| CSIViewer | net10.0-windows10.0.19041.0 | win-x64 | CSIViewer |
| CSIViewer.Host | net10.0-windows10.0.19041.0 | win-x64 | CSIViewer.Host |
| CSIViewer.Host.Agent | net10.0-windows10.0.19041.0 | win-x64 | CSIViewer.Host.Agent |
| CSIViewer.Server | net10.0-windows10.0.19041.0 | win-x64 | CSIViewerServidor |
| CSIViewer.Services | net10.0-windows10.0.19041.0 | — | CSIViewer.Services |
| CSIViewer.Media | net10.0-windows10.0.19041.0 | — | CSIViewer.Media |
| CSIViewer.Infrastructure | net10.0-windows10.0.19041.0 | — | CSIViewer.Infrastructure |
| CSIViewer.Networking | net10.0 | — | CSIViewer.Networking |
| CSIViewer.Protocol | net10.0 | — | CSIViewer.Protocol |
| CSIViewer.Security | net10.0 | — | CSIViewer.Security |
| CSIViewer.Application | net10.0 | — | CSIViewer.Application |
| CSIViewer.Core | net10.0 | — | CSIViewer.Core |

### Arquitectura cliente-servidor

- **Identidad:** cada equipo tiene un ID de 9 cifras y una clave ECDSA P-256. La clave privada se protege con DPAPI.
- **Registro:** el visor/host se registra en el servidor con el código de inscripción.
- **Autenticación:** desafío firmado con ECDSA y comprobación en el servidor.
- **Señalización:** WebSocket con mensajes JSON (`Envelope`).
- **Sesión:** handshake ECDH P-256 + AES-256-GCM de extremo a extremo.
- **Transporte:** relay por el servidor o conexión TCP directa en LAN cuando es posible.
- **Servidor:** mantiene presencia, sesiones, relay, base de datos SQLite y auditoría.
- **Consentimiento:** el host decide qué permisos concede. En modo desatendido puede aceptar/rechazar automáticamente, pero siempre exige la contraseña correcta.
- **Captura/entrada:** el host captura su pantalla y envía vídeo; el visor envía eventos de ratón/teclado. Ambos van cifrados extremo a extremo.

### Estado de compilación

Verificado al crear este documento:

```powershell
dotnet build CSIViewer.slnx -c Debug
# Compilación correcta. 0 advertencias. 0 errores.

dotnet test tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj -c Debug --no-build
# Correctas: 157 / 157
```

También se han generado instaladores Release recientemente:

| Instalador | Tamaño | Fecha |
|---|---:|---|
| `artifacts/installers/CSIViewer-Setup-1.0.0.exe` | 63,3 MB | 13/11/2026 14:07 |
| `artifacts/installers/CSIViewer.Host-Setup-1.0.0.exe` | 90,9 MB | 13/11/2026 14:08 |
| `artifacts/installers/CSIViewerServidor-Setup-1.0.0.exe` | 71,3 MB | 13/11/2026 13:58 |

El servidor que se está usando en pruebas escucha en `http://localhost:5180` (y en `0.0.0.0:5180` si está iniciado como servicio). El DDNS usado es `yusteibiza.ddns.net:5180`.

---

## 2. Funcionalidades implementadas

Leyenda:

- **IMPLEMENTADO:** hay código completo y compila.
- **VERIFICADO:** además se ha probado y observado el resultado esperado.
- **DEFECTUOSO:** funciona parcialmente o produce un comportamiento incorrecto confirmado.
- **PENDIENTE:** no hay validación suficiente o falta una parte.

| Funcionalidad | Estado | Notas |
|---|---|---|
| Escritorio remoto (vídeo) | **IMPLEMENTADO / parcialmente VERIFICADO** | Se ve la pantalla del host en el visor; sesión local de prueba conectada correctamente. Calidad/latencia pendientes de validación real. |
| Captura Windows.Graphics.Capture | **IMPLEMENTADO** | Usada por defecto, con fallback GDI. Logs confirman uso en monitor 0 y monitor 1. Defecto de monitor 2 pendiente. |
| Captura GDI de respaldo | **IMPLEMENTADO** | `GdiScreenCapture`, usado si WGC falla. |
| Transmisión de vídeo H.264 | **IMPLEMENTADO / parcialmente VERIFICADO** | MFT software + `MF_LOW_LATENCY` en el codificador. Benchmark: primera salida en el fotograma 0. Calidad/bitrate nuevos. |
| Transmisión JPEG | **IMPLEMENTADO** | Fallback cuando H.264 falla o se solicita. |
| Control de ratón | **DEFECTUOSO — prioridad máxima** | El usuario confirma que el clic cae en otra zona (listas, ventanas). No marcar como corregido. |
| Control de teclado | **IMPLEMENTADO / PENDIENTE** | Usa scancodes por `SendInput`. No validado de forma independiente en dos equipos. |
| Cambio de monitor | **DEFECTUOSO / PENDIENTE** | Selección de monitor implementada. Monitor 2 presenta franja verde, imagen congelada y falta de interacción; también se percibe lento. |
| Chat | **IMPLEMENTADO / parcialmente VERIFICADO** | Burbujas tipo WhatsApp, estados en español, emojis, historial, auto-apertura al recibir. Verificado visualmente; falta prueba real de dos equipos. |
| Transferencia de archivos | **IMPLEMENTADO** | Bloques de 1 MiB, SHA-256, pausa/reanudación, cancelación, reanudación, límite de subida. Verificado por harness en el pasado; no revalidado tras cambios recientes. |
| Gestión de conexiones | **IMPLEMENTADO / VERIFICADO** | Registro, autenticación, presencia, sesión y relay. |
| Reconexiones | **IMPLEMENTADO / VERIFICADO** | Reintentos con backoff y recuperación; se probó el bucle de reconexión causado por SQLite de solo lectura y se corrigió. |
| Seguridad E2E | **IMPLEMENTADO** | ECDH P-256 + AES-256-GCM; contraseña PBKDF2 verificada en el host; DPAPI; consentimiento. |
| Configuración | **IMPLEMENTADO** | Tema, servidor, calidad/FPS/ancho, archivos, privacidad, red, logs. |
| Temas claro/oscuro/sistema | **IMPLEMENTADO / VERIFICADO** | Se corrigieron diálogos y botones de la barra de título; comprobado con capturas en claro y oscuro. |
| Contraseña guardada de dispositivo | **IMPLEMENTADO / parcialmente VERIFICADO** | Se conserva si el campo se deja vacío; checkbox para quitarla; DPAPI. Se probó la carga con un blob DPAPI insertado en la BD de pruebas. |
| Historial y borrado | **IMPLEMENTADO / VERIFICADO** | Borrado total probado en visor; servidor con purga de sesiones. |
| Servicio de Windows del servidor | **IMPLEMENTADO** | Crear/eliminar/iniciar/parar/actualizar desde la UI; UAC. El servicio real ya se instaló correctamente en la máquina de pruebas. |
| Tarea programada del host | **IMPLEMENTADO / PENDIENTE** | `Register-ScheduledTask` como SYSTEM; el usuario debe reinstalar y probar con UAC. |
| Icono de bandeja | **IMPLEMENTADO / PENDIENTE** | Doble clic abre la ventana; no verificado físicamente tras el cambio. |
| Ayuda integrada | **IMPLEMENTADO** | Páginas de ayuda en visor/host y desplegable en servidor. |
| Instaladores | **IMPLEMENTADO / VERIFICADO** | Client, host y server generados con Inno Setup. |

---

## 3. Historial de reparaciones

No hay historial Git. Esta sección resume cambios y resultados conocidos de la sesión. Cuando no hay prueba real se indica expresamente.

### 3.1 Separación Host / Host.Agent

- **Problema:** el host y el visor estaban mezclados; el host no debía depender del visor.
- **Archivos:** `src/CSIViewer.Host`, `src/CSIViewer.Host.Agent`, `installers/host.iss`, `scripts/install-host-autostart.ps1`.
- **Solución:** host UI independiente `CSIViewer.Host.exe`; agente `CSIViewer.Host.Agent.exe`; datos propios en `%LOCALAPPDATA%\CSIViewer.Host`.
- **Resultado:** host arranca y se conecta de forma independiente.
- **Comprobado:** sí, ejecución local.
- **Regresiones:** no conocidas.

### 3.2 Host como tarea programada, no como servicio

- **Problema:** el host debía arrancar al inicio como SYSTEM sin UAC en cada reinicio, pero no como servicio.
- **Intento fallido:** `schtasks /Create` con `/TR` y rutas con espacios devolvía código 1. El problema era el entrecomillado de PowerShell/schtasks.
- **Solución:** `Register-ScheduledTask` con `New-ScheduledTaskAction/Trigger/Principal/Settings`, tarea `CSIViewerHost`, inicio SYSTEM y tiempo de ejecución ilimitado. Se conservó el agente para lanzar la UI en la sesión del usuario.
- **Archivos:** `src/CSIViewer.Host/Services/UnattendedServiceManager.cs`, `scripts/install-host-autostart.ps1`, `installers/host.iss`.
- **Comprobado:** cmdlets validados con objetos reales; creación real de la tarea requiere UAC y no se ha podido validar desde el agente.
- **Regresión:** no conocida.

### 3.3 Error de la tarea del host “código 1”

- **Problema:** al aceptar UAC, la creación de la tarea fallaba.
- **Causa:** comillas del argumento `/TR` de `schtasks` y falta de detalle del error.
- **Solución:** `Register-ScheduledTask` + archivo temporal con el mensaje de error real.
- **Comprobado:** no end-to-end (UAC).

### 3.4 Servicio del servidor

- **Problema:** no había forma de crear/eliminar el servicio desde la ventana si el instalador no marcaba la tarea; además “actualizar servicio” fallaba cuando ya estaba en ejecución.
- **Solución:** tarjeta **Servicio de Windows** con **Crear servicio**, **Eliminar servicio**, **Iniciar servicio**, **Parar servicio** y **Actualizar estado**; se espera a que el servicio anterior termine de eliminarse antes de recrearlo.
- **Archivos:** `src/CSIViewer.Server/ServerServiceManager.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`.
- **Comprobado:** el usuario instaló el servicio real con UAC y quedó en ejecución. Los botones nuevos no se han probado todos con UAC.
- **Regresión:** no conocida.

### 3.5 Bucle conectado/desconectado del host

- **Síntoma:** el host pasaba de “En línea” a “Desconectado/Reconectando” constantemente.
- **Causa demostrada:** `server.db-wal`/`server.db-shm` fueron creados por el servicio SYSTEM sin permiso de escritura para el usuario. SQLite devolvía `attempt to write a readonly database` al actualizar `LastSeenUtc`; el WebSocket se cerraba y el host reconectaba.
- **Archivos:** `ServerHost.cs` (no cerrar la conexión si falla el guardado), `ServerDataPermissions.cs`, `scripts/prepare-server-data.ps1`, `installers/server.iss`, `install-server-service.ps1`.
- **Solución:** los fallos de persistencia ya no tumban la conexión; el instalador/servicio conceden permisos de escritura a `Usuarios` en `%ProgramData%\CSIViewerServidor`; se repararon los permisos reales de la máquina.
- **Comprobado:** sí; el host se mantuvo conectado y estable después de reparar los permisos.
- **Regresión:** no conocida. Es un cambio de seguridad consciente: los usuarios locales pueden escribir la BD del servidor.

### 3.6 Colores de estado unificados

- **Problema:** el punto de estado usaba colores distintos en visor, host y servidor.
- **Solución:** `OnlineBrush` verde (`#22C55E` claro / `#4ADE80` oscuro) y `OfflineBrush` gris `#9CA3AF`.
- **Archivos:** `Colors.xaml` de visor/host, `Server/App.xaml`, páginas de estado.
- **Comprobado:** capturas visuales.

### 3.7 Diálogos oscuros en modo claro

- **Problema:** `ContentDialog` seguía el tema de Windows, no el tema de la app.
- **Solución:** `RequestedTheme = ThemeService.Current` en visor/host y `RootGrid.ActualTheme` en servidor, en todos los diálogos.
- **Comprobado:** captura del diálogo “Editar dispositivo” en claro.
- **Regresión:** no conocida.

### 3.8 Botones de la barra de título

- **Problema:** al cambiar de tema, minimizar/maximizar/cerrar no cambiaban de color y podían quedar invisibles.
- **Solución:** `UpdateTitleBarColors()` en visor/host; fondo de la barra con `LayerFillColorDefaultBrush`; colores normal/inactivo/hover/pulsado. El servidor ya lo tenía.
- **Comprobado:** capturas en claro y oscuro.

### 3.9 Chat

- Se añadieron burbujas enviadas a la derecha y recibidas a la izquierda, colores de chat, fecha/estado en español y sin avatar repetido.
- Se añadió auto-apertura de la ventana/chat al recibir mensaje, con excepción de una vista de control remoto visible.
- **Comprobado:** capturas de layout; la auto-apertura no se ha validado con dos equipos.
- **Regresión:** no conocida.

### 3.10 Historial y privacidad

- Botón **Borrar historial** en visor/host, con confirmación; conserva dispositivos/contactos/sesiones.
- Servidor: **Borrar historial de sesiones**.
- **Comprobado:** borrado del historial del visor con BD de prueba y recuento 0.

### 3.11 Contraseña guardada de dispositivos

- **Problema:** al editar un dispositivo con contraseña guardada, el campo se mostraba vacío y al guardar se podía borrar la contraseña.
- **Solución:** se descifra con DPAPI y se rellena; si se deja vacío se **conserva** la contraseña existente; checkbox **Quitar la contraseña guardada** para eliminarla.
- **Archivos:** `src/CSIViewer/Views/DevicesPage.xaml.cs`.
- **Comprobado:** se insertó un blob DPAPI en la BD de pruebas, se abrió el diálogo y el campo mostró 10 caracteres; el checkbox apareció. No se ha probado el flujo completo del usuario en su equipo.
- **Regresión:** no conocida.

### 3.12 Calidad y latencia de vídeo

- **Problema:** texto borroso y latencia.
- **Causas:** escalado nearest-neighbour, `MaxWidth=1280` por defecto, bitrate H.264 dependiente solo de FPS, buffer de ~16 fotogramas del codificador.
- **Solución:** `BgraScaler` con filtro de área; bitrate según píxeles × FPS × calidad; `MF_LOW_LATENCY` en el codificador; perfiles de calidad; decodificación en cola de fondo; copia síncrona al `WriteableBitmap`.
- **Mediciones:** PSNR 16,02 → 27,14 dB (1440p→720p, referencia bicúbica); primera salida H.264 fotograma 16-17 → fotograma 0; codificación 3,5/5,0/5,7 ms; decodificación 3,5/9,5/6,4 ms (sintético).
- **Comprobado:** benchmarks Release. No validado en dos equipos.
- **Problema pendiente:** el **decodificador** sigue mostrando un retardo de ~24 fotogramas en el benchmark (primera salida en el fotograma 25 de 100). `MF_LOW_LATENCY` en el decodificador no lo elimina. Esto puede seguir causando lag de vídeo/entrada.

### 3.13 Ratón: intentos y estado

1. Implementación original: `SendInput` absoluto con `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK` y `MonitorCoordinateMapper`.
2. Se cambió a `SetCursorPos` + clic sin coordenadas para evitar problemas de DPI. **Esto introdujo el desplazamiento de clics** que el usuario confirmó.
3. Se ha revertido a `SendInput` absoluto, con la misma coordenada para movimiento y clic. Los instaladores actuales incluyen esta reversión.
4. Se desactivó `IsCursorCaptureEnabled` en WGC para que no aparezcan dos punteros.
5. Se añadieron logs en visor y host (`Remote pointer...`, `Input sent`, `Input rejected`, `Input injection failed`, `Input injected`).

**No está validado que el ratón funcione.** El intento local de generar `PointerMoved` con entrada sintética no produjo eventos WinUI, así que no sirve como prueba. El usuario confirma el fallo real.

### 3.14 Servidor: estado “detenido” con servicio en ejecución

- **Problema:** la cabecera solo miraba `_host.IsRunning`; con el servicio activo decía “Servidor detenido”.
- **Solución:** el estado y la bandeja consideran servicio local o servicio de Windows; la tarjeta superior muestra “Servicio en ejecución”; “Iniciar servidor” se deshabilita y se indica usar “Parar servicio”.
- **Archivos:** `src/CSIViewer.Server/MainWindow.xaml.cs`.
- **Comprobado:** compilación; pendiente de captura tras reiniciar con el servicio.

---

## 4. Problemas actuales prioritarios

### ERROR 1 — El ratón del cliente no controla correctamente el host

#### Síntomas confirmados

- El puntero local del visor se mueve con normalidad.
- La imagen remota se ve.
- Al hacer clic en un punto, se selecciona otro lugar (por ejemplo, una lista).
- No es un cursor congelado ni un problema de renderizado: los eventos llegan mal o se transforman mal.

#### Hipótesis investigadas

1. **Doble puntero:** el cursor del host capturado por WGC y el puntero local. Se desactivó la captura del cursor. No resolvió el clic incorrecto.
2. **Coordenadas DPI:** se probó `SetThreadDpiAwarenessContext` en `MonitorService`/`InputInjector`; producía mezclas 2560/1440 y 1920/1080. Se retiró.
3. **`SetCursorPos` + clic:** se introdujo y se confirmó como regresión; se ha revertido a `SendInput` absoluto.
4. **Escalado de la imagen/márgenes:** el visor mapea con `RemoteViewportMapper`; la imagen puede tener bandas negras o resolución lógica distinta a la física.
5. **Permisos/ControlOwner:** el host descarta entradas si `session.ControlOwner` es falso o falta `ControlInput`.
6. **Sesión equivocada:** `SendInputAsync` selecciona una sesión `IsController`; ahora envía con el cifrado de esa sesión concreta.
7. **Pérdida silenciosa:** antes, si no había sesión/estado/permisos o fallaba el envío, el evento se descartaba sin log. Ahora se registra.

#### Código relacionado

- `src/CSIViewer/Views/RemoteControlPage.xaml` — `RemoteSurface` y handlers.
- `src/CSIViewer/Views/RemoteControlPage.xaml.cs` — `RemoteSurface_PointerMoved`, `TryGetRemotePoint`, `SendMouse`, `CanControl`.
- `src/CSIViewer.Core/RemoteViewportMapper.cs` — coordenadas normalizadas.
- `src/CSIViewer.Services/RemoteClient.Session.cs` — `SendInputAsync`, `SendEncryptedAsync`, `HandleHostSessionFrameAsync` (caso Input).
- `src/CSIViewer.Services/SessionManager.cs` — `ControlOwner`.
- `src/CSIViewer.Core/SessionPermissionPolicy.cs` — permisos.
- `src/CSIViewer.Media/InputInjector.cs` — `Send`, `SendMouse`, `MapToVirtualDesktop`.
- `src/CSIViewer.Core/MonitorCoordinateMapper.cs`.
- `src/CSIViewer.Media/MonitorService.cs`.

#### Correcciones intentadas

- `SetCursorPos` (revertido).
- Contexto DPI por hilo (retirado).
- Envío con sesión concreta (aplicado).
- Logs de todas las fases (aplicado).
- Desactivar captura de cursor (aplicado).

#### Diagnóstico pendiente

Determinar con logs reales en qué fase se pierde o se desplaza:

1. ¿Se ejecuta `RemoteSurface_PointerEntered/Moved`?
2. ¿`CanControl()` es verdadero?
3. ¿`TryGetRemotePoint` devuelve coordenadas válidas?
4. ¿Aparece `Input sent`?
5. ¿Aparece `Input injected` en el host?
6. ¿Coinciden las coordenadas normalizadas del visor con las coordenadas del monitor del host?

#### Siguiente prueba concreta

- Instalar visor y host actuales.
- Abrir el Bloc de notas en el host.
- Mover, clic, arrastrar, rueda y cambiar a DISPLAY2.
- Enviar los logs indicados en la sección 9.
- Si no aparece `Remote pointer entered/moved`, el fallo está antes de enviar (hit testing/foco/superposición). Si aparece `Input sent` pero no `Input injected`, el fallo está en red/descifrado/`ControlOwner`. Si aparece `Input injected` con coordenadas distintas a las esperadas, el fallo es de transformación de coordenadas.

### ERROR 2 — Franja verde al cambiar al monitor 2

#### Síntomas confirmados

- Al seleccionar DISPLAY2 aparece una banda verde en la parte superior de la imagen.
- La imagen queda prácticamente congelada (1,3 FPS en la captura del usuario).
- El ratón no interactúa correctamente en ese monitor.

#### Hipótesis investigadas

1. **Stride NV12 incorrecto:** el decodificador podía devolver filas alineadas y `Nv12ToBgra` asumía ancho compacto, produciendo bandas/colores incorrectos.
2. **`MF_E_TRANSFORM_STREAM_CHANGE`:** el decodificador no renegociaba el tipo de salida; se quedaba con el anterior.
3. **`MF_E_NOTACCEPTING`:** no se drenaban fotogramas retrasados.
4. **WGC en monitor secundario:** se comprobó que se usa Windows.Graphics.Capture para monitor 1, sin errores de captura.
5. **Coordenadas del monitor:** se probaron contextos DPI; no se ha demostrado que sean la causa de la franja.

#### Código relacionado

- `src/CSIViewer.Media/H264Codec.cs` — `H264Decoder.DecodeNv12Frames`, `Nv12ToBgra`, `GetOutputStride`, `TryRenegotiateOutputType`.
- `src/CSIViewer.Media/WindowsGraphicsCapture.cs` — `TryStart`, `OnFrameArrived`, `JpegEncoder.GetBgra`.
- `src/CSIViewer.Services/RemoteClient.Session.cs` — `StartHostCaptureAsync`, `HandleVideoPayloadAsync`, `DecodeVideoLoopAsync`, `video.config`.
- `src/CSIViewer/Views/RemoteControlPage.xaml.cs` — `ShowFrameAsync`.

#### Correcciones intentadas

- Conversión NV12 respetando stride.
- Renegociación de tipo de salida ante `MF_E_TRANSFORM_STREAM_CHANGE`.
- Drenaje/reintento ante `MF_E_NOTACCEPTING`.
- Reducción de esperas al cambiar de monitor.
- Cola de decodificación en segundo plano.

#### Resultado conocido

- El benchmark de decodificación ya no lanza `MF_E_NOTACCEPTING` y devuelve 76/100 fotogramas a 640x360.
- **No hay prueba real del monitor 2 después de estos cambios.** La franja verde y la congelación no están verificadas como corregidas.

#### Diagnóstico pendiente

- Comparar H.264 vs JPEG en monitor 2: si JPEG no tiene franja, el problema es del decodificador H.264/stride; si JPEG también la tiene, es de captura/Escritorio.
- Comparar WGC vs GDI en monitor 2.
- Guardar un fotograma crudo del host y comprobar si la franja ya existe antes de codificar.
- Confirmar resolución real de Windows en DISPLAY2 y su origen/DPI.
- Medir si el decodificador sigue 24 fotogramas por detrás (benchmark).

#### Siguiente prueba concreta

1. Reproducir en monitor 2.
2. Probar el perfil JPEG y el fallback GDI.
3. Enviar el log del host con las líneas `Using Windows.Graphics.Capture`/`GDI capture` y el log del visor con `H264 decoder configured`, `dec`/`ui` FPS.
4. Si es necesario, añadir una captura de diagnóstico temporal a PNG en `StartHostCaptureAsync` (solo para depuración; no dejarla en producción).

---

## 5. Arquitectura de entrada remota

### Recorrido completo

```
WinUI 3 (visor)
  RemoteSurface.PointerEntered / PointerMoved / PointerPressed / PointerReleased / PointerWheelChanged
        ↓
RemoteControlPage.RemoteSurface_PointerMoved / PointerPressed / ...
        ↓
CanControl():
  _state.IsSessionActive && _state.IsController && _client.CanControlRemote
        ↓
TryGetRemotePoint():
  RemoteSurface.ActualWidth/ActualHeight
  _frameWidth/_frameHeight
  RemoteViewportMapper.TryMapToNormalized(...)
        ↓
SendMouse(action, nx, ny, ...)
  x = nx * 65535, y = ny * 65535
  _client.SendInputAsync(InputEventPayload)
        ↓
RemoteClient.SendInputAsync:
  busca sesión IsController
  valida session.State == Connected
  valida SessionPermissionPolicy.CanControl
  valida MessageValidation.IsValidInput
  serializa JSON
  SendEncryptedAsync(session, SessionFrameKind.Input, payload)
        ↓
RemoteClient.SendEncryptedAsync:
  session.Crypto.Encrypt
  LAN directa: FrameProtocol.WriteFrameAsync
  Relay: SignalingClient.SendBinaryAsync
        ↓
Red (TCP directo o WebSocket/relay)
        ↓
Host RemoteClient.HandleHostSessionFrameAsync:
  SessionFrameKind.Input
  comprueba session.ControlOwner y CanControl(session.Permissions)
  deserializa InputEventPayload
  monitor = _activeCaptureMonitor
  bounds = MonitorBounds(monitor.X, monitor.Y, monitor.Width, monitor.Height)
  _input.Send(input, bounds)
        ↓
InputInjector.Send:
  "mouse" → SendMouse
  "key"    → SendKey
  "text"   → SendText
        ↓
SendMouse:
  flags = movimiento/botón/rueda
  si HasPosition:
    flags |= MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
    dx,dy = MonitorCoordinateMapper.ToVirtualDesktopAbsolute(...)
  SendInput(1, INPUT, sizeof(INPUT))
        ↓
Windows (host)
```

### Permisos y propiedad del control

- `RemoteControlPage.CanControl()` exige sesión activa, rol de controlador y `RemoteClient.CanControlRemote`.
- `RemoteClient.CanControlRemote` exige una sesión `IsController`, `Connected` y permiso `ControlInput`.
- En el host, `SessionManager.TryActivate` asigna `_controlOwner` al primer solicitante con `ControlInput`. Si ya hay otro, retira el permiso y devuelve `control_in_use`.
- `HandleHostSessionFrameAsync` descarta cualquier entrada si `session.ControlOwner == false`.
- **No eliminar estas comprobaciones.**

### Coordenadas

- `RemoteViewportMapper` compensa el letterboxing de `Image Stretch=Uniform`.
- `MonitorCoordinateMapper` transforma coordenadas normalizadas al escritorio virtual (incluye monitores con origen negativo).
- `MonitorService` enumera monitores y devuelve origen/tamaño/handle.
- `InputInjector` actualmente usa **`SendInput` absoluto**, no `SetCursorPos`.

### Logs actuales de entrada

Visor:
- `Remote pointer entered...`
- `Remote pointer moved...`
- `Remote pointer ignored...`
- `Remote pointer outside image...`
- `Input dropped before send...`
- `Input sent...`
- `Input send failed...`

Host:
- `Input rejected...`
- `Input injection failed...`
- `Input injected...`

Los movimientos se muestrean; clics y rueda se registran todos.

---

## 6. Arquitectura de vídeo

### Captura

**Windows.Graphics.Capture** (`src/CSIViewer.Media/WindowsGraphicsCapture.cs`)

- `TryStart(monitor, fps, quality, maxWidth, encodeJpeg, onFrame, onError)`.
- `CreateItem(monitor)` usa `IGraphicsCaptureItemInterop.CreateForMonitor`.
- `Direct3D11CaptureFramePool.CreateFreeThreaded` + `GraphicsCaptureSession`.
- `IsCursorCaptureEnabled = false` (decisión para evitar doble puntero).
- `OnFrameArrived`:
  - limita FPS,
  - `SoftwareBitmap.CreateCopyFromSurfaceAsync`,
  - si no H.264: `JpegEncoder.GetBgra` o `EncodeAsync`,
  - `CaptureFrame` al callback.

**GDI** (`GdiScreenCapture.cs`) como fallback:
- `BitBlt` + `StretchBlt` con `HALFTONE`.
- JPEG si no H.264.

**Escalado de calidad**: `BgraScaler.Downscale` (box filter), usado por `WindowsGraphicsCapture.ScaleBgra`.

### Formato y codificación

- Captura: BGRA8.
- H.264: `H264Encoder` convierte BGRA→NV12 en CPU (`BgraToNv12`) y usa un MFT de Media Foundation.
- Salida: H.264 Annex B precedido por un byte `1` en el payload de sesión.
- JPEG: byte `0` en el payload.
- Bitrate: `GetVideoBitrate(width, height, fps, quality)` en `RemoteClient.Session.cs`.
- `MF_LOW_LATENCY` en el codificador (mejora verificada).

### Transporte

- `RemoteClient.Session.SendVideoFrameAsync`.
- `_videoSendBusy` impide acumular fotogramas; si el envío anterior no ha terminado, se descarta el nuevo.
- Un fotograma se cifra con `SessionCrypto` y se envía por LAN directa o relay.
- `VideoConfigPayload` transmite ancho, alto, FPS y calidad.

### Decodificación

- `RemoteClient.Session.HandleVideoPayloadAsync` solo encola H.264.
- `DecodeVideoLoopAsync` (cola `_decodeQueue`, capacidad 2, DropOldest) decodifica en segundo plano.
- `H264Decoder.DecodeAsync`:
  - `ProcessInput` con reintento/limpieza,
  - `ProcessOutput` drena hasta 16 fotogramas,
  - renegocia `MF_E_TRANSFORM_STREAM_CHANGE`,
  - lee stride (`MF_MT_DEFAULT_STRIDE`),
  - `Nv12ToBgra` respeta stride.
- **Defecto conocido:** el decodificador sigue acumulando ~24 fotogramas en el benchmark; `MF_LOW_LATENCY` no lo elimina. Puede ser causa de latencia visual/entrada.

### Renderizado

- `RemoteControlPage.Client_VideoFrameReceived` deja solo el último frame (`_pendingFrame`).
- `RenderLatestFrame` en el hilo UI.
- `ShowFrameAsync`:
  - BGRA: `WriteableBitmap` reutilizado, copia síncrona al `PixelBuffer`, `Invalidate`, `Source` solo si cambia el bitmap.
  - JPEG: `BitmapImage.SetSourceAsync`.
- Stats en la **barra superior**: FPS, resolución, `dec` (ms), `ui` (ms), descartes.

### Cambio de monitor

- Visor: `MonitorCombo` → `RemoteClient.SelectMonitorAsync(index)`.
- Host: `video.select_monitor` → `StartHostCaptureAsync(index)`.
- `StartHostCaptureAsync` detiene captura antigua, vacía/reinicia codificador y arranca la nueva.
- `DisplayWatcherLoopAsync` detecta cambios de configuración.
- **Pendiente:** validar monitor 2 y medir la latencia de cambio.

---

## 7. Advertencias

### Archivos críticos

- `src/CSIViewer.Services/RemoteClient.Session.cs` — sesiones, entrada, vídeo, captura, códecs. Muy sensible.
- `src/CSIViewer.Services/RemoteClient.*.cs` — conexión, chat, archivos, heartbeat.
- `src/CSIViewer.Media/H264Codec.cs` — encoder/decoder MFT; no cambiar sin benchmark.
- `src/CSIViewer.Media/InputInjector.cs` — inyección Win32; cada cambio puede romper el ratón.
- `src/CSIViewer/Views/RemoteControlPage.xaml/.cs` y copia del host — eventos, render, fullscreen.
- `src/CSIViewer.Server/ServerHost.cs`, `DeviceRegistry.cs`, `ClientHub.cs` — relay, auth, sesiones.
- `src/CSIViewer.Infrastructure/Persistence/*` — esquema SQLite y migraciones manuales.
- `src/CSIViewer.Server/ServerServiceManager.cs` y `scripts/*service*` — UAC/servicio.

### Componentes que funcionan y no deben reescribirse

- Autenticación ECDSA, handshake ECDH/AES-GCM.
- Registro de dispositivos y relay del servidor.
- Chat y transferencia de archivos (salvo bug puntual).
- Persistencia SQLite, DPAPI, hashes.
- Temas y estilos.
- Instaladores Inno Setup (salvo ajustes concretos).

### Dependencias sensibles

- `Microsoft.WindowsAppSDK` 2.6.5: no actualizar sin probar WinUI/ContentDialog/title bar.
- `Microsoft.Windows.CsWin32`: cambios en `NativeMethods.txt` regeneran interop; compilar y probar.
- Media Foundation: disponibilidad de MFT H.264 y comportamiento distinto por GPU/driver.
- DPAPI: las contraseñas solo se descifran con el mismo usuario Windows.
- SQLite/WAL: no mezclar servicio SYSTEM y UI sin permisos correctos.

### Concurrencia conocida

- `RemoteClient._sessions` es `ConcurrentDictionary`.
- `_encoderGate`, `_decoderGate`, `_captureGate`, `_directSendLock`, `_videoSendBusy`.
- `_decodeQueue` capacidad 2 con DropOldest.
- `StartHostCaptureAsync` puede esperar al codificador; se redujeron tiempos, pero un envío de vídeo lento puede retrasar el cambio de monitor.
- No mantener `_decoderGate` mientras se hacen operaciones de red largas.
- No bloquear el bucle de recepción con decodificación.

### Limitaciones existentes

- No se captura escritorio seguro/UAC/pantalla de inicio de sesión sin UIAccess firmado.
- El decodificador H.264 software tiene latencia interna de ~24 fotogramas en benchmark.
- El cursor del host no se captura en el vídeo (decisión actual). Solo se ve el puntero local del visor.
- No hay comparación real con TeamViewer.
- No hay repositorio Git.
- El relay y el servidor añaden latencia; no se ha medido RTT real.
- El host/servicio requieren UAC para tareas/servicio.
- La comprobación de dos equipos reales está pendiente.

### Cambios que empeoraron el funcionamiento

- **`SetCursorPos` para el ratón remoto:** produjo clics desplazados; se revirtió a `SendInput` absoluto.
- **Contexto DPI por hilo en MonitorService/InputInjector:** generó mezclas de 2560/1920; se retiró.
- **`IsCursorCaptureEnabled=true`:** mostraba dos punteros; se desactivó.
- **Escalado nearest-neighbour:** emborronaba texto; sustituido por box filter.
- **Bitrate H.264 solo por FPS:** máxima calidad no cambiaba nada; sustituido por píxeles×FPS×calidad.
- **Decodificación en el bucle de red:** bloqueaba entrada/chat; movida a cola de fondo.

---

## 8. Siguientes pasos

### Prioridad 1 — Ratón remoto

1. Instalar visor/host actuales.
2. Reproducir con Bloc de notas: mover, clic, arrastrar, rueda.
3. Recoger logs de visor y host.
4. Determinar la fase exacta:
   - No `Remote pointer` → problema de hit testing/foco/overlay.
   - `Remote pointer ignored` → permisos/estado.
   - `Remote pointer outside image` → mapping/márgenes.
   - `Input sent` sin `Input injected` → red/sesión/descifrado.
   - `Input rejected` → `ControlOwner`/permisos.
   - `Input injection failed` → `SendInput`/coordenadas.
   - `Input injected` con coordenadas incorrectas → mapper/DPI.
5. Corregir **solo** la fase demostrada.
6. Añadir una prueba unitaria de conversión si se toca el mapper.
7. Validar con dos equipos reales.

### Prioridad 2 — Verificación real del ratón

- No declarar corregido hasta que el usuario confirme clic correcto en lista/Bloc de notas.
- Probar monitores 1 y 2, DPI 100/125 %, ventana y pantalla completa.

### Prioridad 3 — Franja verde y monitor 2

1. Reproducir.
2. Comparar H.264 vs JPEG.
3. Comparar WGC vs GDI.
4. Guardar frame crudo antes de codificar (diagnóstico temporal).
5. Verificar stride y tamaño de frame.
6. Corregir captura/decodificador según el resultado.
7. Validar cambio de monitor y latencia.

### Prioridad 4 — Cambio de monitor

- Medir tiempo desde selección hasta primer frame.
- Revisar esperas de `StartHostCaptureAsync` y `_encoderGate`.
- No bloquear la UI.

### Prioridad 5 — Regresiones

- Ejecutar `dotnet build` Debug/Release.
- Ejecutar 157 tests.
- Probar chat, archivos, historial, contraseñas, temas, servicio, tarea programada.

### Prioridad 6 — Calidad/latencia

Solo después de estabilizar ratón y monitor 2:

- Validar perfiles Máxima nitidez/Equilibrado/Máxima velocidad.
- Medir decodificador de 24 fotogramas.
- Evaluar MFT hardware (se detectaron 2 codificadores hardware; activación no integrada).
- Considerar pipeline GPU (D3D11) para BGRA↔NV12.

---

## 9. Verificación

### Estado de Git

```powershell
git status
# fatal: not a git repository (or any of the parent directories): .git
```

No hay historial Git, ramas ni commits. No se puede hacer `git diff` ni recuperar versiones anteriores. **No ejecutar `reset` ni `clean`.** No se ha modificado ningún archivo funcional para crear este documento.

### Build y tests

```powershell
dotnet build CSIViewer.slnx -c Debug
dotnet test tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj -c Debug --no-build
```

Resultado actual: build correcto, 157/157 tests.

### Instaladores

```powershell
pwsh -File installers/build-installers.ps1 -Version 1.0.0
```

Salida: `artifacts/installers/`.

### Logs de la prueba de ratón

- Visor: `%LOCALAPPDATA%\CSIViewer\logs` o `data\logs` junto al ejecutable si es portable.
- Host: `%LOCALAPPDATA%\CSIViewer.Host\logs`.
- Servidor UI/servicio: `%ProgramData%\CSIViewerServidor\logs` o la carpeta de logs del servicio.

Buscar en el visor:

```text
Remote pointer entered
Remote pointer moved
Remote pointer ignored
Remote pointer outside image
Input dropped before send
Input sent
Input send failed
```

Buscar en el host:

```text
Input rejected
Input injection failed
Input injected
```

### Prueba manual mínima

1. Host: abrir Bloc de notas.
2. Visor: conectar.
3. Mover el ratón sobre el área remota.
4. Hacer clic en un punto conocido del Bloc de notas.
5. Arrastrar la ventana.
6. Usar la rueda.
7. Cambiar a DISPLAY2 y repetir.
8. Comprobar que el clic cae exactamente donde se pulsa.

### Información no recuperable

- No hay historial Git ni commits.
- No se conservan todas las conversaciones anteriores; los detalles no presentes en el código o en este documento no se pueden verificar.
- No hay mediciones reales de dos equipos ni comparación con TeamViewer.
- No hay confirmación del usuario de que el ratón o la franja verde estén corregidos.

---

## Resumen para el siguiente modelo

1. **No reescribir** autenticación, relay, chat, archivos, temas ni instaladores.
2. **No volver a `SetCursorPos`** para inyectar el ratón; el camino actual es `SendInput` absoluto.
3. **No quitar** `ControlOwner` ni las comprobaciones de permisos.
4. **No afirmar** que el ratón o la franja verde están corregidos sin prueba real.
5. Empezar por los logs de `RemoteControlPage`, `SendInputAsync`, `HandleHostSessionFrameAsync` e `InputInjector`.
6. Después de corregir el ratón, validar monitor 2/JPEG/GDI/stride.
7. Solo después tocar latencia/códec/GPU.

**Estado global: build correcto; funcionalidad principal implementada; ratón DEFECTUOSO confirmado por el usuario; franja verde de monitor 2 PENDIENTE; validación real de dos equipos pendiente.**
