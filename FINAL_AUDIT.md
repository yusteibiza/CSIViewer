# Auditoría final de CSIViewer 1.0.1

**Estado: continuación de estabilización ejecutada; aceptación Release bloqueada.**
No equivale a una certificación interactiva de WinUI, VMware o instalación SCM.

> **Último resultado (JPEG): 395/395 correctas, Release 0 errores/advertencias**,
> `final-audit-20261115-104817.trx`, `jpeg-fix-full-regression.log`.
> Defecto demostrado: ImageQuality configurado como metadata produce
> 0x88982F41 en la VM; en el físico se ignoraba calidad (dos pruebas rojas antes,
> diez casos JPEG verdes después). Corrección mínima en ambas rutas JPEG:
> opción de creación BitmapEncoder, valor float real. Ver
> `docs/50-jpeg-fix-vmware.md`.
> NO explica por sí sola la última congelación GDI/H264 con etapas avanzando;
> esos4869 errores acumulados precedían al cambioGDI. Se añade muestreo RGB
> acotado en memoria (solo cambio/no-cambio, sin píxeles/hashes en logs) y se
> corrige sospecha JPEG histórica tras éxitoH264. No se toca sesión instalada
> ni se recuperan componentes automáticamente. Validación JPEG en VM pendiente.
> Publicación diagnóstica `diag-20261115-jpeg-fix` completada, detalles abajo;
> no es un instalador ni una certificación de resolución del incidente.

### Entrega JPEG-fix comprobada (prevalece sobre paquetes previos)

- `artifacts/diagnostics/diag-20261115-jpeg-fix.zip`,203642653 bytes, SHA256
  `23EB81F4EF1DC37633D915D65E7CA2A959B85C15CF5A0F6309D210C9A108447C`.
- Copia entregada sin sobrescribir archivos previos:
  `C:\Users\AntonioYuste\Desktop\Nueva carpeta\CSIViewer-diagnostico-3.zip`.
  Verificados hashes562archivos/rol, identidadPE en cuatro assemblies,
  helpers coinciden con fuentes y colección sintética PASS. ZIP sin datos
  privados/agente/instaladores. CMD usan RunNamevmware04.
- Evidencia: `diagnostic-jpeg-fix-package-smoke.log`,
  `diagnostic-jpeg-fix-delivery.json`, `jpeg-fix-test-preservation.txt`.
  Suite395incluye todos los357de referencia y378anteriores; instaladores
  previos intactos, `git diff --check` sin errores (avisosCRLF/LF únicamente).
- No se ha cerrado ni actualizado la sesión del usuario desde herramientas.
  Extraer nueva carpeta Diagnostico3 en ambos extremos y validar en Win10VM,
  sin sobrescribir Diagnostico2/logs. Incidente general sigue NO RESUELTO.

## Continuación: diagnóstico VMware y operaciones por handle (15-11-2026)

> **ÚLTIMA REPRODUCCIÓN, compilación stage-errors:** usuario vuelve a informar
> congelación; nuevos logs completos muestran WGC/H264/envío/recepción/render/
> composición avanzando, vista visible y sin fallback/errores. No es evidencia
> de encoder parado en esta ocasión. Falta distinguir contenido obsoleto o
> presentación física de entrada remota sin efecto. Se solicita abrir Inicio
> desde consola VMware y observar el visor, sin restaurar Host ni reconectar.
> Detalle sanitizado en `artifacts/audit/vmware-live-20261115.md`.
> Usuario confirma que restaurar Host no recupera. Cambio manual controlado de
> Captura moderna aGDI, manteniendoH264/60FPS, más Guardar recupera imagen.
> Guardar reinicia captura/encoder: confusor conocido, NO prueba causal GDI.
> Visor de recuperación preservado103617; pendiente minimizar/repetir bajoGDI
> y verificar logs host. No se han modificado instalación, Windows o servicios.
> **Recurrencia posterior:** también se congela después de seleccionarGDI y
> múltiples ciclos buenos, ANTES de ejecutar recogida. No considerar GDI
> workaround estable ni culpar soloWGC. Visor104008 conserva avance de
> recepción/decode/UI/composición, sin fallback. Se solicita host para confirmar
> GDI efectivo y cruzar este episodio, manteniendo sesión/configuración.

### Comparación posterior y correcciones de recogida

> **LOG NORMAL DEL BLOQUEO RECIBIDO:** H264 efectivo1920×1080/60 FPS;
> writer detuvo registro a09:02:12 UTC por IOException borrando marcador en uso,
> después del inicio de la congelación. Fallos de transferencia del log abierto
> a09:06 también posteriores. No atribuirles la causa inicial.
> Se corrigió el reintento del marcador/compartición de borrado, el seguimiento
> del primer Begin pendiente, el registro JPEG/HRESULT/fase y la primera
> excepción exterior (una advertencia/proceso). Ampliación UI informa
> visibilidad/oferta/enqueue/begin/end sin cambiar scheduling.
> Smoke aislado con bloqueo real de4500ms + tres dumps/cooldown + rotación PASS
> (`diagnostic-writer-lock-smoke.log`); colector con logs host PASS.
> Suite intermedia377/377, Release0/0 (`final-audit-20261115-100913.trx`);
> regresión final tras observación UI: **378/378, 0 fallidas/omitidas,
> Release 0 errores/advertencias** (`final-audit-20261115-101059.trx`,
> `video-stall-ui-diagnostic-regression.log`). Preservados los 357 originales
> y todos los 375 de la etapa anterior (`stage-errors-test-preservation.txt`).
> Cambios no aplicados en vivo. Preparación de ZIP diagnóstico identificado
> `diag-20261115-stage-errors`, con arranque por doble clic Host/Viewer.
>
> **Entrega comprobada:** `artifacts/diagnostics/diag-20261115-stage-errors.zip`,
> 203640511 bytes, SHA256
> `22A446C8218422ABF9EA7C51B9F8B2E2B8399345A859A3B8D540A660D7E968BE`.
> Copia nueva en carpeta del usuario `CSIViewer-diagnostico-2.zip`, hash igual.
> Verificados562 archivos por rol y BuildId en cuatro assemblies; smoke ZIP
> sintéticoPASS,1133 entradas de paquete sin runs/DB/identidad/secretos/agente/
> instaladores. Incluye Start-Host.cmd, Start-Viewer.cmd y Compare-Host.ps1.
> Evidencia `diagnostic-stage-errors-package-smoke.log`/`diagnostic-stage-errors-delivery.json`.
> No se han arrancado los nuevos binarios, cerrado procesos ni aplicado cambios
> al invitado. Extraer aparte para conservar logs anteriores. Esta entrega
> corrige observabilidad y recogida, NO acredita reparación de la congelación.

> **RECURRENCIA:** usuario comunica nueva congelación del portable y confirma
> que NO llegó a cambiar ajustes a90. Registro visor preservado
> `Viewer-vmware01-20261115-100034.zip`, sesión
> `f9c4b076-e3af-447b-ad8e-8e30fe5851d6`. Dispatcher y señalización vivos,
> presentación detenida antes de parar recepción. Solicitar nuevo pipeline del
> host sin reconectar ni alterar FPS. La prueba buena previa no demuestra
> estabilidad prolongada; no atribuir este fallo al cambio a90 no efectuado.
> Host posterior recibido: captura y entrega siguen, EncodeBegin avanza pero
> EncodeDone/envío no. Visor registra backlog H264 y petición JPEG. Detectadas
> dos carencias de instrumentación r2: últimos Begin renovados ocultan intentos
> sin completarse y outer catch de JPEG solo LogDebug sin evento PipelineError.
> Falta log normal del portable, omitido por colector antiguo; no declarar aún
> codec/error concreto ni causa inicial de la detención de presentación.

> JSON del invitado recibido: hashes de Host/Media/Services instalados coinciden
> con sus homólogos físicos inspeccionados, portable con r2. Solo proceso
> portable PID11296, --data-dir, sin --unattended, agente no listado.
> Preferencias guardadas difieren en FPS: instalado 90 frente a portable 60;
> calidad95/MaxWidth2560/WGC/H264 coinciden. Snapshot posterior al registro
> exitoso, no prueba de FPS efectivos durante todo el ensayo.
> Próximo paso manual: portable a 90 FPS, cambiar solo ese ajuste y repetir
> minimizar/restaurar. No modificar instalado/Windows ni reactivar agente.

- Prueba real buena del portable documentada junto al caso malo en
  `docs/48-comparacion-vmware.md`; no confundir ambos binarios 1.0.1.0.
  Inspección PE/IL **solo del instalado físico** muestra revisión informativa
  1b82093 frente a c27c4d3 del portable, hashes distintos; ambos ya tienen
  CaptureRun y AsTask/ConfigureAwait. Hay diferencias de liberación COM, no
  prueba causal. Falta comparar hashes y preferencias del invitado.
- Preparado `compare-host-diagnostic.ps1`: inspección de solo lectura de
  binarios y whitelist numérica/booleana de vídeo; sin exportar servidor,
  contraseñas, identidad, inscripción, settings completos o DB. Tests sintéticos
  de identidad/privacidad/tipos/inmutabilidad PASS (`host-comparison-smoke.log`).
- Corregido colector para logs `csiviewer.host-*`/rotaciones, anclaje de extensión
  y aviso de pipeline ausente. Smoke de integridad r2 y recogida sintética con
  seis entradas PASS (`collector-host-log-fix-smoke.log`); no se modifica r2.
- Corregida falsa sospecha de captura en visor/proceso sin captura y añadidos
  ajustes de vídeo a muestras, solo para una futura compilación. Sin parchear
  procesos ni introducir recuperación de componentes.
- **Release 0 errores/advertencias; 375/375 pruebas, 0 fallidas/omitidas**:
  `final-audit-20261115-093417.trx`, `post-vmware-comparison-regression.log`.
  Nueva prueba cubre falso positivo sin captura; 357 casos de referencia
  comprobados en `post-vmware-baseline-preservation.txt`.
- Copias nuevas en carpeta de diagnóstico del usuario, sin sobrescribir originales:
  `Compare-Host.ps1` SHA256
  `57E5AD4EC458CBEA3E371CAD5CC14BCFA0F4DCE2129487FD52EC51BD80F79601`;
  `Collect-Diagnostic-Corrected.ps1` SHA256
  `9930A7935FB7ABCAF792451EC5312C8363C338A116B0221F1690867BADD20FAE`.
  No ejecutados contra el invitado aún. Sin nueva instalación/distribución final.

Sin reiniciar la auditoría ni sustituir instaladores. Referencia inicial de esta
continuación: 357 casos correctos. Resultados intermedios: 365/365
(`final-audit-20261115-000400.trx`) y 371/371
(`final-audit-20261115-001417.trx`), Release 0 errores/0 advertencias.
La regresión de los últimos cambios y la publicación se registran abajo cuando
terminen; esos resultados intermedios no certifican cambios posteriores.

- Diagnóstico opt-in `CSIVIEWER_PIPELINE_DIAGNOSTICS=1`: anillo de 8192 eventos,
  contadores y reloj monotónico; escritor independiente cada 2 s, rotación de
  aproximadamente 16 MiB × 3 por proceso, volcados por sospecha/error/fin de sesión
  y solicitud manual. No hay escritura de disco desde el productor por frame.
  Se observan captura/copia/conversión, encoder, cola/envío cifrado, recepción,
  decode, entrega WinUI/tick de composición, dispatcher, entrada y errores.
- Correlación sin cambio de protocolo: `Session` compartido; A de
  `SendBegin/SendDone` = nonce+1 = `VideoReceived.Frame`. H.264 identifica lotes
  del decoder, no correspondencia exacta entre píxeles. `CompositionObserved`
  **no demuestra presentación física**, ni SendInput aceptación por la app.
  Las sospechas no son diagnósticos concluyentes; no se reinicia WGC al detectar
  estancamiento en modo diagnóstico. Se mantienen fallbacks por error/backlog.
- `TransferDirectoryLease`: NtCreateFile relativo, OBJ_DONT_REPARSE, handles
  de directorio, rechazo de parciales con múltiples enlaces, metadatos acotados,
  rename nativo del handle abierto e identidad del padre comprobada al commit.
  La creación de carpetas recibidas usa también operaciones relativas.
  Fallos iniciales reales error 87/32 conservados en `handle-relative-first.trx`,
  `handle-relative-second.trx` y `native-lease-smoke.trx`; corregidos sin debilitar
  tests. La tercera pasada focalizada superó 23 casos.
- Pruebas nuevas: anillo concurrente/acotado, etapas pendientes/escritorio quieto,
  sustitución de directorio, 32 finalizaciones bajo swaps de junction/directorio,
  100 abort/dispose, ocho entradas detenidas antes de inyección y 2000 despachos
  retrasados/replays tras teardown; backend de entrada simulado, sin SendInput
  real. Veinte ciclos cifrados reales con Kestrel/SQLite exclusivamente loopback,
  controladores sintéticos nuevos para conservar throttles de producción.
  Se verifican tablas vacías y ausencia de chat posrevocación; esto no demuestra
  ausencia universal de fugas, ni operaciones privilegiadas reales. La
  cancelación no es una transacción de filesystem: una creación ya iniciada
  puede haber dejado directorios antes de observar la revocación. No se
  certifica atomicidad universal entre revocar y todos los efectos de I/O.
- Lanzamiento portable con datos aislados bajo `runs`, sin matar procesos;
  modo diagnóstico impide escribir HKCU Run, elevación de tareas y registro
  de notificaciones. La prueba original de escritura HKCU sigue excluida.
- Plantilla manual: `artifacts/audit/manual-ui/controls-and-events.csv`
  (2884 elementos XAML) y `dialogs-and-pickers.csv` (38 construcciones).
  Son inventario estático **NO EJECUTADO**, no cobertura visual certificada.
  Aplicar temas claro/oscuro/sistema, teclado/DPI, cancelar/confirmar/error,
  doble acción y cierre mientras hay trabajo según docs 45/46.

**Límites que bloquean Release:** ejecutar en Windows 10 VMware afectado con
visor físico, WinUI interactivo, device loss y composición real; validar
compatibilidad del I/O nativo en Windows 10/SMB/ReFS/filtros. No hay fallback
inseguro por pathname si falla el I/O nativo. La raíz queda autorizada al abrir
su handle, no al conceder previamente una ruta textual: sustitución antes de
ese binding y atacantes privilegiados/kernel no se consideran eliminados.
Escritor/rotación reales bajo bloqueo de VM y rendimiento gráfico no certificados.
Guía reproducible: `docs/47-diagnostico-vmware.md`. **NO APTA PARA RELEASE;
incidente VMware NO RESUELTO** hasta comprobarlo en el entorno afectado.

### Resultado automatizado de esta continuación

- `final-audit-20261115-002524.trx`: 372/372; después de añadir dos casos,
  **`final-audit-20261115-002654.trx`: 374/374, 0 fallidas, 0 omitidas**.
  Restore/build Release de solución: **0 errores, 0 advertencias**.
  Logs: `diagnostic-final-regression.log` y `diagnostic-delivery-regression.log`.
- Dos casos adicionales ejecutados: cancelación previa de apertura no crea
  directorios; apertura nativa de parcial con hardlink se rechaza sin cambiar
  bytes del destino. No se modificó ni ejecutó el test original de escritura Run.
- Instaladores viewer/host/server y backup 1.0.0 conservan los SHA-256 registrados
  en la etapa anterior; evidencia `diagnostic-installer-preservation.json`.
  No se generan nuevos instaladores ni se altera su versión 1.0.1.0.
- Conservación nominal comprobada: los **357 casos** de `224652` están presentes
  en los 374 resultados finales y todos son Passed
  (`diagnostic-baseline-preservation.txt`). `git diff --check` sin errores;
  Git avisa de normalización CRLF/LF, no se fuerza cambio global de finales.
- Smoke del escritor en **proceso hijo aislado**, sin iniciar red/captura/input/UI:
  activación, JSONL parseable, dos solicitudes manuales en menos de 30 s y
  rotación a tres segmentos bajo presión sintética de 16 MiB. Resultado PASS,
  `diagnostic-writer-smoke.log`; fixture y datos en `artifacts/audit/writer-smoke*`.
  La presión inyectada no es un benchmark de vídeo ni un bloqueo real de VMware.

### Entrega portable identificada y comprobada

> **Resultado manual posterior en VM afectada:** tras cerrar con autorización
> el agente y host instalado y arrancar el portable con script corregido,
> el usuario confirma control e imagen funcionales con host minimizado.
> Pipeline real PID11296/Build r2 verificado; WGC activo, H264 en visor,
> captura/encode/envío/recepción/composición progresan, 508 paquetes
> correlacionados en anillos solapados, sin GraphicError/PipelineError/
> RecoverySuppressed en host. Hubo reconexiones y resoluciones 1280×720 y
> 1920×1080, por lo que no se afirma equivalencia de configuración ni causa
> raíz. Evidencia sanitizada: `artifacts/audit/vmware-live-20261115.md`.
> El colector omite logs normales `csiviewer.host-*` por patrón de nombre:
> pendiente corregirlo; el pipeline sí se recogió. Incidente no cerrado.

> **Incidente real posterior:** se confirmó que el usuario estaba conectado al
> host de `C:\Program Files\CSIViewer Host`, no al portable. El ZIP del host solo
> contenía manifiesto/collection.txt; los logs normales recuperados coinciden en
> SessionId con el visor, pero falta pipeline del host. Además, revisión del
> arranque confirma que el host anula el env de datos si falta `--data-dir`.
> Las comprobaciones sintéticas anteriores no detectaron esta diferencia del
> ejecutable real: no equivalían a aislamiento WinUI ejecutado.
> Corregido el script para pasar `--data-dir` con comillas y exigir evidencia
> pipeline del PID lanzado durante hasta 20 s. No se matan procesos al fallar.
> Prueba sintética nueva: quoting/datos, readiness, env restaurado, VerifyOnly,
> rechazo de proceso existente y detección de salida prematura PASS
> (`diagnostic-launcher-fix-smoke.log`). Regresión completa ejecutada:
> `final-audit-20261115-091158.trx`, 374/374 correctas, 0 fallidas/omitidas;
> Release 0 errores/advertencias (`diagnostic-launcher-fix-regression.log`).
> Parche copiado con autorización a la carpeta de diagnóstico como
> `Start-Diagnostic-Corrected.ps1`, SHA256
> `9495F06C44D17C06E9CA93680F3EDBAF294465AE6A343612C972B302DB13EDDF`.
> VerifyOnly comprobó los 562 archivos host sin lanzar la aplicación.
> Binarios r2 sin cambios; parche de lanzador aparte, no instalador.
> Hechos sanitizados: `artifacts/audit/vmware-live-20261115.md`.

- **Entregar `artifacts/diagnostics/diag-20261115-374tests-r2.zip`**,
  203631302 bytes. BuildId `diag-20261115-374tests-r2`.
  SHA-256:
  `9E26050F1B57E15CF894D7F8487C3F68A5BCF220EF8B666C9E60489783158ED6`.
- Publicación Release win-x64 autocontenida de visor y host por separado,
  sin instalador/agente/provisión. ZIP inspeccionado: 1130 entradas,
  sin runs, bases de datos, settings/identity, claves/certificados o instaladores.
  El manifiesto identifica archivos; SHA no equivale a firma de editor.
- Verificados los hashes de **562 archivos por rol** y el atributo de assembly
  `CSIViewerDiagnosticBuild` en ambos ejecutables gestionados y ambos Services.dll
  mediante lectura PE/metadata, sin cargar/arrancar las apps.
- `test-diagnostic-package.ps1`: PASS de integridad, identidad y recogida de
  logs sintéticos (cuatro entradas esperadas, sin settings/identity).
  Evidencias: `diagnostic-package-smoke-r2.log`, `diagnostic-delivery.json` y
  `diagnostic-publication-r2.log`.
- El primer paquete `diag-20261115-374tests.zip` queda como evidencia intermedia:
  VerifyOnly rechazó el host habitual abierto. Se corrigió **solo el script**
  para permitir comprobación sin iniciar procesos; el lanzamiento real sigue
  rechazando instancias existentes y no las detiene. Entregar únicamente r2.
- Guía dentro del ZIP: `LEEME.md` (copia de docs 47). No se ejecutaron las apps
  gráficas del paquete, ni se modificó la instalación del host detectado.
  Pendiente la reproducción manual afectada; **NO APTA PARA RELEASE**.

## Plan y reglas de ejecución

1. Inventario reproducible de proyectos/dependencias, tipos/métodos, XAML,
   controles/eventos, suscripciones, recursos, pruebas y scripts de distribución.
2. Línea base de compilación Release x64 y pruebas existentes con datos aislados.
   Revisar primero efectos externos de las pruebas. No tocar HKCU Run, tareas,
   servicios, certificados, aplicaciones instaladas ni datos reales del usuario.
3. Revisión prioritaria de autenticación, autorización/revocación, entrada
   retrasada, rutas/transferencias, secretos y componentes SYSTEM.
4. Concurrencia/cancelación/cierre; después chat, persistencia, captura/vídeo,
   navegación, diálogos, eventos, temas y accesibilidad.
5. Corregir solo defectos sustentados en código/pruebas, con regresiones,
   compilación y revisión del diff de cada lote. Conservar cambios preexistentes.
6. Restore/build/tests finales, auditoría de paquetes y contenidos publicables.
   Generar distribución únicamente con clasificación honesta y bloqueos visibles.
7. Completar matriz de evidencias, pendientes y clasificación de aceptación.

La instrucción actual autoriza pruebas y sustituye la prohibición anterior de
ejecutarlas. No autoriza intervenir en las instalaciones o servidor del usuario,
reiniciar el equipo ni instalar candidatos con privilegios administrativos.
No se harán commits/pushes ni operaciones destructivas de Git.

## Evidencia y estados

Los resultados se conservarán bajo `artifacts/audit/`. Se distinguirá:

- **VERIFICADA**: alcance concreto demostrado mediante ejecución reproducible.
- **VERIFICADA PARCIALMENTE**: revisión/pruebas de una parte, con límites explícitos.
- **NO VERIFICADA**: falta ejecución/evidencia suficiente.
- **DEFECTUOSA**: defecto vigente demostrado.

Compilar, inventariar eventos o inspeccionar XAML no demuestra comportamiento
visual, seguridad end-to-end ni rendimiento en otros equipos. El bloqueo
transitorio observado en VMware no se considera resuelto por haber remitido.
Winlogon y la instalación SYSTEM requieren validación en un equipo/VM aislado.

## Registro de etapas

- Inicio: árbol con cambios preexistentes sin confirmar. No se descartan.
- Suite existente inspeccionada: una prueba escribe temporalmente en HKCU Run;
  se excluirá de la línea base hasta contar con aislamiento apropiado.
- Inventario inicial ejecutado: 18 proyectos (incluidas herramientas fuera de la
  solución), 245 archivos C#, 464 tipos, 1773 métodos y 34 archivos XAML. Se
  detectaron 221 sitios `+=`/`-=` y 126 métodos para revisión adicional. Estas
  cifras son de la línea base; no equivalen a 221 eventos comprobados.
- Build Release de la solución: 0 errores, 0 advertencias.
- Línea base aislada: 302 pruebas ejecutadas, 299 correctas y 3 fallidas
  (catálogos en/fr/ca con una clave retirada extra). TRX: `baseline-release.trx`.
- Regeneración de traducciones corregida y ejecutada dos veces: hashes idénticos;
  16 pruebas de localización correctas; build sin advertencias/errores.
- Regresión criptográfica previa: 2 pruebas fallidas; tras corregir el ciclo de
  vida, suite aislada de 304 pruebas correcta.
- Regresión de autorización previa con backend simulado: 4 pruebas fallidas;
  tras corregirla y añadir control positivo, suite de 309 pruebas correcta.
- Regresión de junctions previa: 2 pruebas fallidas, tanto en una subcarpeta de
  destino como en `.rdpart`. Tras corregir: build sin advertencias/errores y
  suite de 311 pruebas correcta. Ampliación con cambio de directorio antes de
  finalizar y raíz redirigida autorizada: **313 pruebas correctas, 0 fallidas**
  (`transfer-reparse-extended-release.trx`). La prueba de HKCU Run sigue excluida.
- Tras serializar cifrado/envío: build Release de la solución con **0 errores y
  0 advertencias**, suite aislada con **315 correctas y 0 fallidas**. Evidencia:
  `ordered-send-release.trx`. Este es el último resultado completo de esta etapa,
  no una aprobación final de todas las fases.
- Consulta NuGet de vulnerabilidades directas/transitivas ejecutada: ningún
  paquete vulnerable notificado para los proyectos de la solución. Evidencia:
  `artifacts/audit/vulnerable-packages.json`. No demuestra ausencia de riesgos
  desconocidos ni audita binarios externos al grafo NuGet.

### Incidencia de aislamiento de pruebas

La primera versión de `RemoteInputAuthorizationTests` suponía incorrectamente
que una dependencia nula impedía crear el inyector. `RemoteClient` lo construye
internamente: aquella ejecución pudo inyectar cuatro movimientos de ratón, sin
clics ni teclas. Su resultado no se considera evidencia válida de aislamiento.
Antes de repetir se reemplazó `_input` por `RecordingInput` mediante reflexión;
las ejecuciones posteriores no utilizan entrada nativa. La incidencia se informó
al usuario. TRX no válido para aislamiento: `input-authorization-before.trx`;
reproducción correcta: `input-authorization-isolated-before.trx`.

## Inventario y dependencias funcionales

Los inventarios detallados están en `artifacts/audit/`: `projects.json`,
`code-inventory.json`, `xaml-controls-events.json`, `event-subscriptions.json`,
`method-review-sites.json` y `package-versions.json`. La herramienta
`tools/CSIViewer.AuditTool` usa Roslyn del SDK, sin añadir paquetes externos.

| Área/proyectos | Responsabilidad y dependencias principales |
|---|---|
| `CSIViewer.Core` | Modelos, permisos, políticas, localización offline es/en/fr/ca |
| `CSIViewer.Protocol` | Mensajes, validación, marcos binarios; Core |
| `CSIViewer.Security` | Identidad/handshake, contraseñas y cifrado de sesión; Protocol/Core |
| `CSIViewer.Application` | Validación de rutas, recepción streaming y contratos; Core/Protocol/Security |
| `CSIViewer.Infrastructure` | SQLite/EF, configuración, DPAPI, logs, inicio de usuario; Application |
| `CSIViewer.Networking` | Señalización WebSocket, transporte directo, relay, STUN, heartbeats/backoff |
| `CSIViewer.Media` | WGC/GDI, H.264, conversiones de imagen, monitores, entrada/audio |
| `CSIViewer.Services` | Orquestación de conexión/sesión/chat/archivos/captura/identidad |
| `CSIViewer`, `CSIViewer.Host` | Ejecutables WinUI independientes; servicios y código compartido de presentación |
| `CSIViewer.Host.Agent`, `CSIViewer.Host.Login` | Lanzador de tarea y backend de escritorio de sistema; instalación/identidad protegidas |
| `CSIViewer.Server` | Servidor ASP.NET Core, persistencia y administración WinUI/servicio |
| `CSIViewer.UnitTests` | Suite de regresión, seguridad, persistencia, protocolo y multimedia |
| `tools/*` | TestHarness, FeatureProbe, LocalizationTool y AuditTool; no son funcionalidades del producto |

Las superficies XAML incluyen ventanas principales/App de las tres aplicaciones;
páginas Home, Host, RemoteControl, Chat, FileTransfer, Devices, Contacts, History,
Settings, HostSettings, Help y About del visor/host, y diccionarios Colors/Styles.
Los diálogos creados en C# y el código enlazado se encuentran en el inventario C#;
todavía requieren seguimiento manual de apertura/cierre y suscripciones.
Los métodos vacíos de selección que solo dejan cambios en borrador y el
`LoginRuntime.Show` sin UI no se han eliminado: un cuerpo vacío no demuestra por
sí mismo una función simulada o código muerto.

## Matriz funcional y evidencia disponible

**Todos los estados siguientes son del alcance indicado**, no certificaciones
end-to-end. Los nombres de suites remiten a `tests/CSIViewer.UnitTests/` y sus
resultados ejecutados, no a pruebas manuales de las aplicaciones instaladas.

| Funcionalidad | Estado | Evidencia y límite |
|---|---|---|
| Compilación Release | VERIFICADA PARCIALMENTE | Solución compilada; no sustituye publicación/instalación x64 |
| Localización offline | VERIFICADA PARCIALMENTE | 16 LocalizationTests y regeneración repetible; falta inspección visual de todas las vistas |
| Cifrado/replay/handshake | VERIFICADA PARCIALMENTE | SessionCryptoTests, SessionHandshakeTests, SecurityTests y orden concurrente con transporte simulado; falta validación real |
| Contraseñas/identidad/DPAPI | VERIFICADA PARCIALMENTE | PasswordHasher, TemporaryPassword, DeviceIdentity, DeviceKeyPin, CredentialExpiry, SecretProtector |
| Autenticación/limitación | VERIFICADA PARCIALMENTE | AuthenticationAttemptTracker, SlidingWindowThrottle, HostPasswordVerifier; falta ataque end-to-end aislado |
| Autorización de entrada | VERIFICADA PARCIALMENTE | RemoteInputAuthorization con backend simulado y caso positivo; faltan carrera prolongada y dos equipos |
| Cierre y estado de sesión | VERIFICADA PARCIALMENTE | RemoteSessionEnd con I/O de historial retenida, SessionManager, ConnectionStateMachine |
| Transporte/reconexión | VERIFICADA PARCIALMENTE | Transport, WebSocketMessageReader, ClientHub, ReconnectPolicy/Coordinator, ConnectionHealthMonitor; sin cortes reales |
| Recepción/integridad/resume | VERIFICADA PARCIALMENTE | IncomingFileTransfer y validación; 4 regresiones de junctions correctas; faltan carreras de filesystem |
| Explorador/permisos de archivos | VERIFICADA PARCIALMENTE | FileBrowser, FileBrowserSession, FileTransferValidation; falta recorrido interactivo |
| Chat/historial | VERIFICADA PARCIALMENTE | ChatHistory y Repository; faltan simultaneidad end-to-end y borrado desde WinUI |
| Configuración/persistencia | VERIFICADA PARCIALMENTE | Settings, AppSettings, JsonSettingsStoreSecurity, LegacyDataMigration, Repository; falta validar todos los borradores en UI |
| H.264/conversión/coordenadas | VERIFICADA PARCIALMENTE | H264RoundTrip con imágenes sintéticas, NV12, BgraScaler, mapeos; no prueba captura real |
| WGC/GDI y recuperación VMware | NO VERIFICADA | Revisión y logs históricos; causa transitoria no demostrada, sin reproducción actual |
| Audio/portapapeles | VERIFICADA PARCIALMENTE | AudioPacket y SessionFeatures; sin reproducción/captura ni clipboard real en esta auditoría |
| Winlogon/escritorio SYSTEM | VERIFICADA PARCIALMENTE | Guard/State/LoginHostPolicy; captura/control reales NO ejecutados |
| UI/comandos/diálogos/navegación | VERIFICADA PARCIALMENTE | Inventario estático y PresentationLayout; no se ha interactuado con todos los controles |
| Temas claro/oscuro/sistema | VERIFICADA PARCIALMENTE | AccentPalette y recursos inventariados; falta cambio Windows en vivo, contraste, tooltips y diálogos |
| Accesibilidad/DPI/multimonitor | NO VERIFICADA | Mapeos unitarios no equivalen a narrador, teclado, pantallas y DPI reales |
| Arranque/task host | VERIFICADA PARCIALMENTE | Políticas y revisión previa; no se han tocado tareas instaladas |
| Servicio del servidor | VERIFICADA PARCIALMENTE | Host HTTP/SQLite real en loopback y manager con runner simulado; SCM/UAC/instalación reales pendientes |
| Logs/diagnóstico | VERIFICADA PARCIALMENTE | Logging e InputEventDiagnostics/Trace; no se ha completado revisión de todos los mensajes y secretos |
| Dependencias NuGet | VERIFICADA PARCIALMENTE | Consulta sin vulnerabilidades notificadas; no es análisis completo de supply chain |
| Instalación/upgrade/uninstall | NO VERIFICADA | No se han ejecutado instaladores candidatos en máquina limpia |
| Rendimiento/recursos/cierre prolongado | NO VERIFICADA | No hay medidas de CPU/GPU/memoria/latencia/FPS reales de esta auditoría |

## Defectos demostrados y correcciones

### AUD-001 — claves retiradas reintroducidas por revisión de traducciones

`scripts/review-translations.ps1` aplicaba todas las claves de overrides aunque
ya no existieran en español. Corregido: filtrar catálogo y overrides por el
conjunto ordinal de claves activas de `es.json`. Se regeneraron en/fr/ca, se
comprobó idempotencia y pasaron las 16 pruebas de localización existentes.

### AUD-002 — uso de claves puestas a cero tras Dispose

`SessionCrypto.Dispose` borraba claves sin sincronizar ni invalidar el objeto.
Una instancia cerrada podía cifrar con clave cero y aceptar mensajes construidos
con ella. Reproducción mediante dos pruebas unitarias, sin red. Corregido:
sincronización de envío/recepción con Dispose, cierre idempotente, cifrado lanza
ObjectDisposedException y descifrado retorna false tras el cierre. No se afirma
haber demostrado una explotación remota completa.

### AUD-003 — entrada con referencias de sesiones retiradas/revocadas

El handler comprobaba permisos y propiedad del control, pero no existencia,
estado conectado ni revocación al inyectar. La ruta heredada del controlador
también tenía una rama de entrada sin sesión host autorizada. Cuatro casos
fallaron con el backend simulado. Corregido: solo el handler de host puede
inyectar; revalidación junto al efecto nativo bajo un gate por sesión, compartido
con retirada/transición y liberación de teclas. La liberación precede a esperas
de chat/red/disco. Cinco casos de regresión, incluido el positivo, superados.
No se considera concluida la auditoría de todas las operaciones posrevocación.

### AUD-004 — redirección mediante junctions en destinos y temporales

Dos reproducciones aisladas aceptaban destinos con junction hacia una carpeta
fuera de la raíz autorizada; `.rdpart` podía escribir allí sus metadatos.
Corrección comprobada para estos casos: validar reparse points en descendientes de la raíz
autorizada al abrir, leer sidecars, escribir metadatos y finalizar. La raíz
elegida explícitamente puede ser una carpeta redirigida. Se rechazan
descendientes reparse; esto requiere revisar compatibilidad con carpetas cloud.
Es una comprobación puntual: NO constituye protección completa frente a un
proceso local hostil que cambie directorios entre validación y acceso.
Las cuatro pruebas superadas cubren junction de destino, junction de `.rdpart`,
sustitución de la carpeta entre apertura y finalización, y conservación del caso
positivo con raíz explícitamente autorizada que es una junction.

### AUD-005 — cifrado y envío concurrentes con distinto orden

La asignación del contador antes de esperar el lock de transporte permitía que
otro emisor enviara un contador mayor primero. El receptor rechaza el anterior,
como demuestra `OlderCounter_IsRejected`; no es reutilización de nonce, sino
pérdida de mensajes legítimos por adelantamiento. Evidencia estática en las tres
rutas de envío cifrado. Corrección: gate asíncrono por instancia de crypto que
abarca cifrado y envío completo; utilizado por pending, sesiones activas y ruta
heredada directa/relay. La retransmisión de fallback conserva el mismo frame y
la protección anti-replay. No se serializan sesiones independientes entre sí.
Dos pruebas añadidas retienen un envío mediante backend simulado y comprueban
orden, descifrado, cancelación en cola y liberación tras IOException. Superadas,
incluidas en la suite completa de 315 pruebas; build sin advertencias/errores.
Todavía no prueba tráfico entre equipos reales.

## Continuación solicitada: servicio, filesystem, WinUI y VMware

Se continuó desde las 315 pruebas, sin descartar cambios previos ni repetir la
auditoría inicial. Se añadieron pruebas sin eliminar ni relajar las existentes.
La suite conserva su exclusión explícita de HKCU Run. Reproducción automática:
`scripts/test-final-audit.ps1` (restore, build de la solución y tests, todos con
TEMP/TMP/client/server aislados; evidencia conservada en `artifacts/audit/`).

### Nuevos defectos y tratamiento

| ID | Evidencia previa | Corrección / alcance comprobado |
|---|---|---|
| AUD-006 | Segunda DisposeAsync de ServerHost lanzaba ObjectDisposedException; puerto ocupado no provocaba error porque ExplicitUrl no llegaba a Kestrel | Cierre idempotente compartido, prohibición de restart tras Dispose, URL realmente aplicada, estado publicado solo tras arranque, rollback de mappings/estado en fallo |
| AUD-007 | Revisión del manager: temp del usuario escrito con elevación, executables sin ruta absoluta, errores de query tratados como servicio ausente, cancelación que abandonaba proceso elevado | Salida por exit code sin fichero temp; rutas del sistema; estado IsKnown y botones bloqueados si se desconoce; observar finalización tras lanzar SCM (cancelación previa sí impide lanzamiento); esperas de estado acotadas, errores de sc comprobados |
| AUD-008 | Código de inscripción plano persistido en binPath, doble escape de apóstrofes, flags falsos heredados de otra identidad | Servicio nuevo usa sal/hash PBKDF2 existente (PasswordHasher, 600000 iteraciones); nunca el secreto ni un cifrado reversible en argumentos. Verificador no sirve como código reutilizable. Opciones deshabilitadas y registro vacío se pasan explícitamente. Parse/validación fallan cerrado |
| AUD-009 | ServerDataPermissions ejecutaba concesión recursiva Modify a Users sobre carpeta configurable; servicio podía instalar un binario portable writable como SYSTEM | No reparar ACLs automáticamente: fallo visible, ACL intacta en prueba de DB bloqueada. Instalación SYSTEM requiere árbol protegido bajo Program Files validado con política compartida. Ejecución ordinaria del servidor portable sin servicio sigue permitida |
| AUD-010 | Cuatro pruebas fallidas: 32 finalizaciones concurrentes con mismo nombre, Abort viejo eliminando sidecar nuevo, dos variantes de nombres `.rdpart` aceptadas | Reintentar reclamación atómica de nombre al mover; conservar reserva temporal hasta terminar finalización/limpieza; abort no toca nombres que ya no posee; Dispose comparte tarea y no destruye gate usado por callers; nombres internos rechazados, también en creación de carpetas |
| AUD-011 | Hardlink sintético en `<key>.json.tmp` truncaba un archivo sentinel fuera de descargas | Sidecar escribe en nombre aleatorio con FileMode.CreateNew, cierra y reemplaza atómicamente; no trunca path preexistente. Reproducido y corregido con datos sintéticos |
| AUD-012 | Frame Control heredado sin sesión podía crear directorio usando permisos cacheados; prueba fallida `stale-control-before.trx` | Fallback pre-sesión solo admite auth.result mientras hay conexión pendiente; dispatch exige instancia viva/no revocada. Tras OpenAsync, registrar recepción con revalidación coordinada con retirada; duplicado no reemplaza ni limpia recepción ajena |
| AUD-013 | Revisión de Contacts/RemoteControl: await de persistencia/monitor sin observación en async void, posible doble edición, DataWriter sin finally | Gate en contactos, InfoBar existente para errores, helper de solicitudes de monitor observado y escritor con using. Contratos de fuente y compilación superados; efectos visuales requieren intervención humana |
| AUD-014 | WGC conservaba referencias ABI propias de device/item después de FromAbi | Release en finally de ambas referencias. Contrato estático y compilación; NO se afirma medición de fugas D3D ni resolución del freeze VMware |

**Compatibilidad de servicio:** el binPath ya instalado no se ha modificado ni se
han rotado secretos reales. La corrección AUD-008 se aplica al crear/recrear el
servicio con el candidato. Un servicio antiguo puede conservar el código plano;
reinstalación/rotación se validan en VM primero. El servidor portable/per-user no
puede instalarse ahora como SYSTEM desde una carpeta escribible: requiere una
instalación para todos los usuarios protegida, no una relajación de ACLs.

### Ejecuciones reales de esta continuación

- `server-lifecycle-before.trx`: 2 correctas y 1 fallida (Dispose repetida).
- `server-lifecycle-fixed.trx`: 13 correctas y 1 fallida; la prueba de puerto
  ocupado descubrió que ExplicitUrl se ignoraba. No se ocultó la prueba fallida.
- `filesystem-concurrency-before.trx`: 4 fallidas (datos sintéticos aislados).
- `filesystem-hardlink-before.trx`: 1 fallida (sentinel sintético truncado).
- `stale-control-before.trx`: 1 fallida (directorio dentro de fixture GUID).
- `server-filesystem-release.trx`: build 0/0, 340 correctas y 0 fallidas.
- `final-audit-20261114-223700.trx`: restore/build 0/0, 346 correctas.
- `final-audit-20261114-224111.trx`: restore/build 0/0, **356 correctas, 0
  fallidas, 0 omitidas**. Junctions, symlinks y hardlinks se probaron realmente
  sobre fixtures temporales de este entorno; no se habilitó Developer Mode ni
  se modificaron privilegios.
- `final-audit-20261114-224427.trx`: restore/build Release **0 errores, 0
  advertencias; 357 correctas, 0 fallidas, 0 omitidas**, incluida instalación
  protegida. Se compararon los nombres de los 315 casos iniciales contra el TRX:
  ninguno eliminado. Evidencia `continuation-verification.json`.
- Confirmación tras revisión final del manager y liberación de ServiceController:
  **`final-audit-20261114-224652.trx`: 357 correctas, 0 fallidas, 0 omitidas;
  restore/build Release 0 errores/0 advertencias**. Log completo conservado en
  `artifacts/audit/continuation-final.log`. Este es el resultado más reciente.

La integración ejecutada incluye servidor Kestrel/SQLite y un par de clientes
con identidad/DPAPI/autenticación/cifrado/chat Unicode/fin de sesión reales en
loopback. Captura, entrada, notificaciones y consentimiento UI se sustituyen por
backends explícitos de prueba: NO constituye una conexión de dos equipos físicos.
El test con WebSocket anónimo abierto observa parada del host dentro del límite
de prueba; no certifica todos los escenarios de cliente autenticado perdido.
Los archivos de servidor se compilan enlazados en tests para ejercitar el mismo
código headless, sin activar WinUI ni registrar el ejecutable en SCM.

Las siete pruebas nuevas `UiSourceSafetyContractTests` son contratos de fuentes
y XML, **no siete ejecuciones interactivas de la interfaz**. Las cuatro pruebas
`CaptureRecoveryPolicyTests` usan timestamps/estado sintéticos y recovery
cancelado: no ejecutan WGC/GDI ni demuestran recuperación de un driver real.

### Interfaz y congelación VMware

Revisión por superficie y recorrido captura/encode/send/receive/decode/render:
`docs/46-revision-ui-video.md`. Se mantuvieron páginas cacheadas y suscripciones
de vida de aplicación; no se borraron handlers por parecer vacíos. Se revisaron
marshaling, último frame, generaciones, timers, diálogos y recursos de tema;
la totalidad de los efectos interactivos queda **VERIFICADA PARCIALMENTE**.

**VMware: NO RESUELTO / NO REPRODUCIDO en este entorno.** El invitado respondió
localmente y se recuperó tras esperar según el usuario. SendInput aceptado en
logs no demuestra que la aplicación del invitado procesara cada evento; el
cursor local del visor tampoco. El watchdog solo prueba silencio de captura
tras demanda, no progreso del encoder, socket o compositor. No hay métricas
correlacionadas suficientes para atribuir el incidente a WGC, H.264, red o GPU.
No se desactivaron funciones/optimizaciones sin evidencia.

### Validación humana e inicio automático

`docs/45-validacion-manual-release.md` contiene pasos, resultados esperados y
logs para P01–P15 (dos equipos), V01–V07 (Windows 10/VMware), servicio SYSTEM,
UAC, task/Winlogon y la prueba HKCU Run. La prueba original permanece intacta y
excluida aquí; debe ejecutarse en cuenta local nueva de VM desechable sin hive
ni perfil compartido. Cambiar TEMP/CSIVIEWER_DATA_DIR **no aísla el Registro**.
No se considera superada, ni se sustituye por una comparación de strings.

## Riesgos y revisión pendiente prioritaria

- **Orden de cifrado/envío:** corrección AUD-005 supera regresión simulada.
  Falta medir colas/latencia bajo carga y cambios direct/relay
  reales; no se declara validación de rendimiento por inspección del gate.
- **Filesystem:** las comprobaciones por path, incluso repetidas, no eliminan
  TOCTOU frente a un proceso local hostil que sustituya directorios/links en la
  ventana entre validación y acceso. Falta garantía mediante handles/leases de
  filesystem y validación adversarial; no se afirma que un peer remoto pueda
  crear por sí solo esos links con el protocolo. Hardlink de temp preexistente,
  nombres internos, colisiones y ownership sí tienen las regresiones descritas.
- **Servicio SCM/SYSTEM:** runner probado con simulación y host en loopback; no
  se han ejecutado scripts elevados, UAC ni instalación/parada del servicio real.
  Validar binPath/PBKDF2, ACLs, policy de recuperación, fallo de arranque y salida
  real en VM. No reparación automática de instalaciones antiguas con ACL insegura.
- **Permisos/cierre:** dispatch viejo está protegido, pero falta estrés de todos
  los handlers de archivo/audio/clipboard en los límites de await durante
  revocación y cierre; no generalizar una prueba de input a todos los efectos.
- **WinUI:** recorrido visual completo y profiler de recursos/dispatcher no
  ejecutados; contratos estáticos no sustituyen cada interacción de P13–P15.
- La prueba `StartupServiceTests.SetEnabled_WritesAndRemovesRunEntry` permanece
  excluida por escribir en HKCU Run. No se cuenta como superada.
- Requieren equipos/VM y consentimiento operativo específico: Winlogon, UAC,
  instalación/upgrade/desinstalación, task SYSTEM, redes cortadas, dos equipos,
  pantallas/DPI reales y reproducción de VMware. No se ha eludido UAC ni SAS.

## Distribución

No se han sustituido los tres instaladores existentes ni se ha modificado el
backup original 1.0.0. No se han cambiado versiones de paquetes, firmado con
certificados nuevos ni instalado candidatos. La futura entrega conservará
`1.0.1.0`, tres instaladores directamente en `artifacts/installers/` y timestamps
idénticos. Faltan aceptación de seguridad, publicación, revisión de contenidos y
validación de instalación/ejecución antes de generar un lote distribuible.
Los SHA-256 de los tres `.exe` se han comparado con el lote entregado anterior
y coinciden (`artifacts/audit/installers-unchanged.json`). Los cambios auditados
NO están incluidos en esos instaladores. Se conserva el subdirectorio de build
`payload` preexistente; no se borra ni se presenta como entrega nueva.

## Clasificación de aceptación

**NO APTA PARA RELEASE (clasificación provisional del estado auditado).**
La auditoría sigue en curso; permanecen riesgos importantes de seguridad y
concurrencia sin cerrar y no se cumplen los requisitos de distribución.
Las pruebas correctas no autorizan una clasificación APTA ni RELEASE CANDIDATE.
