# Comparación tras la prueba real de VMware

> **Esta prueba favorable fue seguida de nuevas congelaciones, también conGDI.**
> No considerar estable el portable ni presentar GDI como solución definitiva.
> El defecto JPEG concreto y los síntomas restantes están separados en
> `docs/50-jpeg-fix-vmware.md` y `artifacts/audit/vmware-live-20261115.md`.

## Resultado observado

El host **instalado** en el invitado se congelaba al minimizar su ventana.
El usuario confirma que el host **portable de diagnóstico r2**, iniciado
directamente y con datos nuevos, funciona al minimizar/restaurar repetidamente.
Se conservan los registros de ambos casos. No se actualizó la instalación.
Esto valida esa prueba concreta del portable, no identifica aún la causa raíz.

## Diferencias que no deben mezclarse

| Factor | Instalado que fallaba | Portable que funciona |
|---|---|---|
| Ejecutable | Program Files/CSIViewer Host | host del paquete diagnóstico r2 |
| Arranque | agente; reaparece al cerrar host | directo con script corregido, sin agente |
| Datos | perfil habitual de LocalAppData | runs/Host-vmware02 |
| Vídeo observado | WGC + H264, 1920×1080, encoder a 90 FPS | WGC + H264; aparecen 1280×720 y 1920×1080 |
| Instrumentación | logs normales, sin pipeline | pipeline opt-in, anillo, muestras |
| Recuperación WGC por silencio | no documentada para ese binario invitado | suprimida; contador RecoverySuppressed=0 |

Los ajustes persistidos y un fotograma de 1920×1080 no demuestran por sí solos
FPS negociados, bitrate o misma carga. Hubo tres sesiones en el registro bueno:
los stop/start de captura asociados no se deben presentar como reinicios de
recuperación de una sesión congelada.

## Inspección de binarios en el ordenador físico (no en el invitado)

Lectura de metadata/IL, sin cargar ni ejecutar las DLL instaladas:

- Instalado local: `ProductVersion`/informational revision `1b82093…`.
- Portable r2: revisión informativa `c27c4d3…` y DiagnosticBuildId r2.
- Hashes/MVID diferentes en Host/Media/Services, aunque FileVersion es 1.0.1.0.
- **El instalado local ya tiene CaptureRun, parada en Task.Run y
  AsTask/ConfigureAwait en la copia WGC.** No es válido concluir, mirando solo
  la revisión Git, que el instalado carecía de esas correcciones.
- En Media sí se observan llamadas adicionales de liberación COM en el
  portable (CreateDevice: 3→4 Marshal.Release; CreateItem: 1→2), además de la
  instrumentación. No prueban que esa diferencia cause/solucione el incidente.
- Una revisión informativa no garantiza un árbol limpio al compilar. Tampoco
  demuestra que la instalación dentro de VMware tenga los mismos hashes.

Evidencias: `artifacts/audit/local-installed-vs-portable-metadata.json` y
`capture-binary-call-comparison.jsonl`. Se inspeccionaron exclusivamente
binarios de aplicación; no settings personales ni bases de datos del físico.

## Siguiente comparación, sin interrumpir el invitado

### JSON del invitado recibido (09:56:40 local, 08:56:40 UTC)

`host-comparison-20261115-095640-2fafef0a.json` confirma:

- Host/Media/Services instalados en el invitado tienen exactamente los SHA-256
  de la instalación física ya inspeccionada; portable coincide con r2.
  La inspección de esos tres binarios locales sí se aplica a sus copias en VM.
- Solo aparece Host PID11296, origen Portable, con --data-dir y sin --unattended;
  no aparece Agent en la consulta. No se cambia ningún proceso.
- Ajustes guardados: instalado **90 FPS**, portable **60 FPS**. Ambos con calidad
  95, MaxWidth 2560, upload limit 0, WGC/H264/direct LAN preferidos.
- Es una lectura de preferencias guardadas posterior al ZIP de vídeo bueno;
  no prueba que todos los ensayos anteriores se hicieran con esos mismos FPS,
  ni que el stream entregue 60/90 imágenes nuevas cada segundo.
- Siguiente ensayo controlado: cambiar únicamente FPS del **portable** a 90
  desde su UI, guardar y repetir minimizar/restaurar/mover. No arrancar agente
  ni tocar instalado. Si falla, conservar sesión y logs; si funciona, los FPS
  por sí solos no explican la diferencia observada.

`scripts/compare-host-diagnostic.ps1`, entregado como `Compare-Host.ps1` junto
al manifiesto del paquete, genera un JSON en `runs/Host-vmware02`.

- Lee versiones/SHA-256 de Host.exe/Host.dll/Media.dll/Services.dll de los
  directorios instalado y portable; no carga los assemblies.
- Lee únicamente para exportar una whitelist numérica/booleana de vídeo:
  Fps, MaxWidth, ImageQuality, UploadLimitKbps, PreferModernCapture,
  PreferH264, PreferDirectLan. Son ajustes guardados, no telemetría negociada.
- Solo exporta PID/padre/origen instalado o portable y presencia de dos flags;
  nunca la línea de comandos completa.
- No exporta servidor, identidad, contraseña, inscripción, ajustes completos
  ni base de datos. No cambia settings, Windows, procesos, servicios o tareas.
- Datos ausentes/denegados se marcan Missing/Unreadable/Unknown. No elevar
  privilegios automáticamente ni interpretar Unknown como equivalencia.

La prueba del script usa fixtures sintéticas (incluido un assembly de sistema
copiado al fixture), comprueba hashes, exclusión de secretos/tipos inválidos y
que el archivo de ajustes instalado sintético no cambie.

## Correcciones adicionales comprobadas en código

- Colector: incluye ahora `csiviewer.host-*.log` y rotaciones; sufijo anclado
  para no recoger `*.log.settings.json`, aviso si falta pipeline. Smoke de ZIP
  sintético de seis entradas PASS. La corrección se entrega aparte; no se
  alteró el ZIP r2 publicado ni la aplicación en marcha.
- Telemetría para una próxima compilación: no clasificar captura detenida
  cuando el proceso no captura; registrar ajustes de vídeo numéricos/booleanos
  en las muestras para futuras comparaciones. No modifica WGC ni el códec.
  El paquete r2 que está ejecutándose no recibe estos cambios en caliente.
- Release y suite aislada: **375/375, 0 fallidas/omitidas, 0 errores/advertencias**,
  `final-audit-20261115-093417.trx`; preservados los 357 nombres de referencia.

**No preparar instalador definitivo ni declarar resuelto el host instalado.**
Antes de aislar una causa se necesita el JSON del invitado y, si procede, una
comparación controlada cambiando un factor cada vez, con autorización para
cualquier interrupción. No reactivar automáticamente el agente detenido.
