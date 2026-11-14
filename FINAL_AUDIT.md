# Auditoría final de CSIViewer 1.0.1

**Estado: continuación de estabilización ejecutada; aceptación Release bloqueada.**
No equivale a una certificación interactiva de WinUI, VMware o instalación SCM.

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
