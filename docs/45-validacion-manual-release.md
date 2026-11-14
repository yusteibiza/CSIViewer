# Validación manual de aceptación — dos equipos y VMware

**Pendiente de ejecución humana. No es una lista de pruebas ya superadas.**
Usar candidatos compilados de este árbol, no mezclar los instaladores antiguos
con los cambios auditados. No generar/sustituir instaladores definitivos mientras
`FINAL_AUDIT.md` indique NO APTA. Ejecutar en equipos de laboratorio o snapshots,
con datos, código de inscripción y contraseñas sintéticos. No usar el servidor
real del usuario ni desactivar UAC, Defender, antivirus o escritorio seguro.

## Preparación y evidencia común

1. Equipo A: visor. Equipo B: host y servidor de laboratorio (o tercer equipo).
   Primero ambos físicos; después sustituir B por Windows 10 invitado en VMware.
2. Crear carpeta `C:\CSIViewerLab` y una carpeta de datos distinta por proceso.
   Lanzar desde terminales separadas con `CSIVIEWER_DATA_DIR` específico;
   servidor con `CSIVIEWER_SERVER_DATA_DIR` y `CSIVIEWER_SERVER_DB` específicos.
   Nunca apuntar estas variables a una instalación o base de datos existente.
3. Configurar servidor de laboratorio: puerto libre, UPnP desactivado. Para
   acceder desde A usar su IP LAN; preferir HTTPS con certificado real de lab
   confiable en ambos extremos. Si se prueba HTTP, habilitar conscientemente la
   opción de transporte inseguro solo en este laboratorio. El cifrado de sesión
   no se desactiva. No confundir HTTP explícito con permiso de Windows login.
4. Registrar versión exacta de Windows (`winver`), .NET (`dotnet --info`), GPU y
   driver (`dxdiag /t C:\CSIViewerLab\dxdiag.txt`), resolución, DPI, monitores,
   RAM, VMware Workstation y VMware Tools. En la VM anotar aceleración 3D y vGPU
   **sin cambiarlas en la primera reproducción**.
5. Calcular SHA-256 de los `.exe` y DLL relevantes del candidato con
   `Get-FileHash -Algorithm SHA256 '<ruta>'`. La versión 1.0.1.0 por sí sola no
   identifica el lote. Anotar ruta, fecha UTC, PID y hash de cada proceso.
6. Activar diagnóstico Debug desde la configuración de las aplicaciones de lab.
   Anotar UTC en ambos equipos (`Get-Date -AsUTC -Format o`). La diferencia de
   relojes no es latencia de red. Guardar antes/después los packs de diagnóstico
   del visor y del host y los JSONL del servidor de laboratorio.
7. Crear archivos de prueba sin información personal: vacío; 1 KiB; 16 MiB;
   >1 GiB si hay espacio; Unicode; carpeta con varias subcarpetas. Conservar los
   hashes originales. Usar una ventana de Bloc de notas de lab y un reloj con
   segundos visible para comprobar vídeo/entrada sin introducir credenciales.

Para cada caso: registrar PASS/FAIL, hora, duración, hashes/PIDs, SessionId,
configuración de captura/códec y transporte. Repetir en Claro/Oscuro/Sistema
cuando la columna lo requiera. No enviar logs sin revisarlos por datos privados.

## Matriz funcional: dos equipos físicos

| Caso | Acción exacta | Resultado esperado | Evidencia si falla |
|---|---|---|---|
| P01 | A conecta con B: primero contraseña incorrecta, luego correcta y consentimiento denegado, finalmente autorizado con solo pantalla | Rechazos claros, timeout acotado; sesión aceptada no permite ratón/teclado/chat/archivos no concedidos | JSONL de los tres procesos y SessionId de cada intento |
| P02 | Autorizar control; arrastrar ventana, clic izq/der, rueda; escribir una frase sintética en Bloc de notas | Cambia realmente la ventana/texto en B, no solo el cursor local de A | Vídeo de ambas pantallas, trazas TraceId, Requested/Inserted/LastError y CursorX/Y |
| P03 | Mantener una tecla/botón, finalizar desde B; seguir moviendo y escribiendo desde A | Teclas/botones liberados; ningún evento remoto posterior; A abandona sesión | Hora exacta de revocación y trazas anteriores/posteriores; no escribir secretos |
| P04 | Dos visores distintos conectan al mismo host con permisos distintos; terminar uno | Se mantienen límites/permisos y exclusión del propietario de control; la otra sesión autorizada continúa sin heredar control revocado | Logs de ambos visores, host y servidor |
| P05 | Chat simultáneo en ambos sentidos, Unicode/emoji, envío vacío y muy largo; perder red durante envío; volver a conectar | Sin bloqueo/duplicación silenciosa; error o estado de entrega claro; historial ordenado | Conversation/SessionId, texto solo sintético y diagnóstico |
| P06 | Borrar dispositivo con chat, confirmar; navegar fuera/dentro; reiniciar aplicación de lab | Desaparecen dispositivo/chat/selector y borrador; no reaparecen por una carga antigua; no termina otra sesión autorizada | DB de lab y pasos exactos; nunca DB real |
| P07 | Copiar todos los archivos de lab y carpeta; recibir 3 simultáneos con mismo nombre y contenidos distintos | Hash idéntico, renombrado consistente, sin sobrescritura no autorizada ni crecimiento de RAM proporcional al archivo | Hash origen/destino, logs y listado de `.rdpart` |
| P08 | Transferencia grande: pausar, reanudar, cortar red, reconectar, reanudar; cancelar otra | Prefijo durable reutilizado, hash final correcto; cancelación no borra parciales de otra transferencia | TransferId, offsets y archivos sintéticos preservados |
| P09 | Destino ocupado, sin permisos, disco de lab lleno (solo VHD desechable), nombre Unicode/largo, duplicado con Skip/Rename/Overwrite | Error claro y archivo original intacto; sobrescritura solo confirmada | Antes/después con hashes y errores; no llenar el disco del usuario |
| P10 | Desconectar cable/Wi-Fi de A 30 s y luego de B; detener/reiniciar solo servidor de lab; cerrar abruptamente un proceso de lab | Heartbeat/timeout cierran estados antiguos; backoff acotado; reconexión exige nueva autorización, no resucita permisos | Logs completos ±60 s, tiempos y SessionId nuevo/antiguo |
| P11 | Cambiar monitor, resolución y DPI en B; monitores con coordenadas negativas; pantalla completa en A y navegación fuera/dentro | Imagen/coordenadas correctas, sin teclas atascadas; vídeo anterior no reaparece tras cerrar sesión | Capturas de disposición de pantallas y logs de ambos extremos |
| P12 | Habilitar audio/clipboard explícitamente, después deshabilitar/finalizar | Solo permisos concedidos; sin datos posteriores al cierre, ecos ni cola creciente | Diagnóstico sin copiar contenido privado del clipboard |
| P13 | Recorrer todas las páginas del visor/host, cada botón y diálogo; cancelar pickers; repetir rápido Guardar/Editar/Conectar | No dobles operaciones/diálogos, errores visibles, no cierre inesperado; Guardar/Restablecer/Valores por defecto conservan su semántica | Página, control, secuencia, tema, DPI y logs |
| P14 | Claro, Oscuro y Sistema; en Sistema cambiar Windows durante página/dialog abiertos; navegar después | Contraste y tema coherentes en texto, iconos, bordes, menús, tooltips, diálogos y barra de título | Capturas en cada estado; Narrador y recorrido por teclado |
| P15 | Cerrar ventana normal y desde bandeja según modo; repetir iniciar/parar servidor local sin servicio | Bandeja/ocultación intencionales; al salir de verdad no quedan listeners, audio, captura ni procesos secundarios de lab | PIDs antes/después, conexiones TCP de esos PIDs y logs de cierre |

## Matriz VMware Windows 10: incidente de minimización

Mantener constantes red y parámetros. Repetir **10 ciclos** por combinación de
captura/códec: WGC+H.264, WGC+JPEG, GDI+H.264, GDI+JPEG, usando las opciones ya
existentes y guardando/reconectando cuando corresponda. Probar primero la
configuración original (1920×1080, 90 FPS si sigue configurada), luego 30 FPS.
Una mejora al bajar FPS no identifica por sí sola la causa.

| Caso | Acción | Comprobación esperada |
|---|---|---|
| V01 | En la sesión remota minimizar una ventana de Bloc de notas de la VM y restaurarla | Reloj/animación siguen visibles; acciones reales en invitado; no termina sesión |
| V02 | Minimizar la ventana de CSIViewer Host dentro del invitado; restaurar desde su bandeja | Captura de monitor continúa; ocultar/minimizar no revoca ni destruye la sesión |
| V03 | Minimizar el visor A y restaurarlo; después minimizar/restaurar VMware Workstation | No se reproduce un frame viejo indefinidamente; sesiones/permisos se conservan correctamente |
| V04 | Repetir V01/V02 mientras se mueve/arrastra y con otro visor solo observando | Comparar si se congela un visor o ambos; el cursor local de A no es prueba de actividad de B |
| V05 | Si se congela, no reconectar inmediatamente: observar reloj; usar **consola VMware local** para mover una ventana y escribir una frase sintética | Distinguir invitado bloqueado de vídeo/entrada remotos; registrar si B sigue localmente responsive |
| V06 | Durante congelación esperar 5, 10 y 30 s; anotar recuperación/fallback; solo después reconectar | Watchdog puede recuperar WGC→GDI ante silencio tras cambio, sin prometerlo ante bloqueos de driver/códec; identificar SessionId preservado o nuevo |
| V07 | En snapshot separado cambiar resolución de la VM durante streaming y repetir; revisar eventos de driver gráfico | Recuperación o error acotado; no bloqueo permanente de D3D/captura/decodificador |

### Si falla: recoger antes de reiniciar

- Logs del visor, host y servidor desde al menos 60 s antes hasta 60 s después,
  con Debug, hashes, PID, SessionId y hora del clic/minimización/restauración.
- Vídeo simultáneo del visor y de la consola VMware (datos sintéticos), reloj
  con segundos en invitado; indicar cuál ventana se minimizó y desde dónde.
- Indicar separadamente: cursor local de A, mensaje enviado por A, recibido por
  B, `SendInput` aceptado por Windows, posición consultada en B y efecto visible
  real en una aplicación. `Inserted=1` NO demuestra este último punto.
- Exportar eventos Application/System del invitado en el intervalo (Display,
  dxgkrnl, errores/hangs de aplicación), `dxdiag`, versión de Tools y `vmware.log`
  de esa VM. No copiar el disco/VM completa ni credenciales.
- Guardar capturas de Administrador de tareas con CPU/GPU/memoria por proceso;
  si hay hang persistente, generar dump solo de procesos de lab con consentimiento
  (puede contener pantalla, chats, claves y contraseñas: tratarlo como secreto).
- Si no hay contadores continuos de captura/envío/recepción/decode/render, marcar
  el último tramo como **indeterminado**, no inferir que el driver es culpable
  por ausencia de errores. Las estadísticas en pantalla no son un histórico.

## Servicio Windows e inicio automático: solo VM desechable

Estos casos requieren administrador en la VM de lab y no se han ejecutado aquí.

1. Snapshot limpio. Crear/iniciar/parar servicio del servidor con configuración
   de lab. Consultar estado desde cuenta estándar: fallo de consulta no debe
   mostrarse como «No instalado» ni habilitar acciones de modificación.
2. Inspeccionar `sc.exe qc CSIViewerServidor` dentro de la VM: el binPath nuevo
   debe contener `--enrollment-verifier`, nunca el código de inscripción plano.
   No publicar la salida del servicio antiguo si contiene secretos.
3. Probar UAC denegado, arranque con puerto ocupado y DB sin permisos; el error
   no debe dejar UI bloqueada ni conceder permisos recursivos a Users. Reparar
   permisos solo para la identidad elegida en la carpeta de datos de lab.
4. Parar/iniciar, terminar proceso del servicio de lab para comprobar política
   de recuperación; luego desinstalar. Confirmar listener liberado y ausencia
   del servicio. No usar el host/tarea del usuario como objetivo.
5. Validar separadamente la tarea host y Winlogon conforme a los documentos de
   laboratorio existentes: UAC/SAS siguen siendo límites de Windows.

## Prueba excluida de HKCU Run, sin tocar el perfil habitual

**NO ejecutar el comando siguiente en el equipo/perfil habitual.** TEMP/TMP y
CSIVIEWER_DATA_DIR **no aíslan el Registro**. Tampoco basta un nombre GUID o
`runas` como administrador: sigue siendo un perfil real.

Usar una VM desechable de Windows, snapshot, **cuenta local nueva de lab**,
sin perfil/Registro compartidos, sin iniciar sesión con la cuenta habitual.
Copiar repositorio y SDK .NET 10 requerido (offline si no hay red); los resultados
se devuelven como archivos, nunca importando/exportando el hive al equipo real.

Dentro de esa VM, en PowerShell y desde la copia local:

```powershell
$env:CSIVIEWER_DATA_DIR = 'C:\CSIViewerLab\startup-data'
$env:TEMP = 'C:\CSIViewerLab\temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$key = Get-Item $run -ErrorAction SilentlyContinue
$before = if ($null -ne $key) { @($key.GetValueNames()) } else { @() }
dotnet test tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj -c Release `
  --filter 'FullyQualifiedName~StartupServiceTests.SetEnabled_WritesAndRemovesRunEntry' `
  --logger 'trx;LogFileName=startup-disposable-profile.trx' `
  --results-directory C:\CSIViewerLab\results
$testExit = $LASTEXITCODE
$key = Get-Item $run -ErrorAction SilentlyContinue
$after = if ($null -ne $key) { @($key.GetValueNames()) } else { @() }
Compare-Object $before $after
if ($testExit -ne 0) { throw 'La prueba de lab falló; conservar TRX y snapshot.' }
```

El comando contempla que la clave Run no exista inicialmente. El test debe
escribir/leer/eliminar su valor GUID; ningún valor residual nuevo debe
quedar. Revisar TRX y descartar la VM/snapshot al terminar. **Esta prueba sigue
excluida en el entorno actual y no se cuenta como superada.**
