# Plan de implementacion por fases

Version: 1.0 (Fase 1)
Fecha: 2026-11-12

---

## 1. Regla de avance

Cada fase termina con:

1. Codigo compilable (cuando aplique).
2. Pruebas ejecutadas y resultado.
3. Lista de archivos creados/modificados.
4. Instrucciones de prueba manual.
5. Funcionalidades implementadas, verificadas y pendientes.
6. Riesgos y decisiones nuevas.
7. **Espera de aprobacion explicita antes de la siguiente fase.**

No se considera terminada una funcionalidad por tener solo la pantalla. Una fase puede entregar una porcion vertical completa (por ejemplo, identidad + persistencia + UI + pruebas) o una infraestructura con pruebas, pero nunca una maqueta vacia.

### Definition of Done comun

- Compila en `Debug|x64` y en `Release|x64`.
- Sin advertencias nuevas en CI.
- Pruebas unitarias de la logica nueva.
- Pruebas de integracion si toca red, persistencia o protocolo.
- Manejo de errores, timeout y cancelacion.
- Logs sin secretos.
- Documentacion actualizada.
- Sin regresiones en fases anteriores.
- Rendimiento medido cuando aplique.

---

## 2. Fase 1 - Arquitectura y diseno (actual)

**Objetivo.** Fijar el diseno antes de escribir codigo de producto.

**Entregables.**
- Vision, atributos de calidad y limites.
- Proyectos, dependencias y reglas de capas.
- Protocolo de senalizacion, datos, video y archivos.
- Modelo de identidad, handshake E2E, permisos y amenazas.
- ADR y registro de riesgos.
- Plan de fases y criterios de aceptacion.
- Configuracion base de compilacion (`global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`).

**Verificacion.**
- SDK 10.0.404 confirmado.
- Paquetes Microsoft y versiones confirmadas en NuGet.
- Limitaciones de `System.Net.Quic` y ausencia de plantilla WinUI documentadas.
- Documentos revisados y aprobados.

**No incluye.** Solucion, proyectos, XAML ni codigo ejecutable. Se crean en Fase 2.

**Criterio de salida.** Aprobacion del usuario.

---

## 3. Fase 2 - Esqueleto WinUI 3, navegacion, temas e interfaz base

**Objetivo.** Una aplicacion real que arranca, navega, cambia de tema en caliente y sienta las bases visuales.

**Entregables.**
- `CSIViewer.slnx` y todos los proyectos del grafo.
- `CSIViewer` con WinUI 3 y Windows App SDK 2.6.5, creado manualmente y compilado.
- Composición DI, `App.xaml.cs`, ventana principal, titulo personalizado, Mica con fallback.
- `NavigationView` con las nueve secciones y estado preservado (`NavigationCacheMode`).
- Sistema de diseno: tokens de color, tipografia, espaciado, radios, `ThemeDictionaries` claro/oscuro/alto contraste.
- `ThemeService` con modos Claro, Oscuro y Sistema; deteccion de cambios de Windows; aplicacion inmediata.
- `SettingsStore` inicial con persistencia de tema e idioma.
- Paginas base con contenido real de navegacion, no datos falsos; las secciones funcionales muestran su estado "pendiente" de forma honesta.
- Iconografia Segoe Fluent Icons, animaciones discretas, adaptacion a DPI y ventanas estrechas.
- Prueba de arquitectura de referencias.
- Pruebas de ViewModels de navegacion y tema.

**Archivos principales.**
- `src/CSIViewer/App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`.
- `src/CSIViewer/Themes/*.xaml`, `Styles/*.xaml`.
- `src/CSIViewer/Views/*.xaml`, `ViewModels/*.cs`.
- `src/CSIViewer/Services/NavigationService.cs`, `ThemeService.cs`, `DispatcherService.cs`.
- `src/CSIViewer.Services/Composition/ServiceRegistration.cs`.
- `src/CSIViewer.Infrastructure/Settings/JsonSettingsStore.cs`.
- `tests/CSIViewer.UnitTests/UI/ThemeServiceTests.cs`, `NavigationTests.cs`.

**Criterios de aceptacion.**
- La app arranca en Windows 11 y Windows 10 2004+ (si se dispone de VM).
- Se navega a las nueve secciones sin excepciones.
- El tema cambia al instante en toda la app, incluidos dialogos y flyouts.
- El modo Sistema reacciona al cambio de Windows sin reiniciar.
- El tema elegido persiste tras cerrar y abrir.
- La ventana se adapta a 1280x720, 1920x1080 y 200% DPI sin recortes.
- `dotnet build` y pruebas pasan.

**Riesgos.** WinUI 3 sin plantilla; comportamiento de `RequestedTheme` en dialogos; Mica en Windows 10. Mitigacion: proyecto manual validado, tema aplicado a cada `ContentDialog`, fallback solido.

**No incluye.** Datos reales, red, identidad ni control remoto. Los placeholders deben estar claramente marcados y no simular funcionalidad.

---

## 4. Fase 3 - Identidad de dispositivo, ajustes y pantalla de inicio

**Objetivo.** Cada instalacion tiene identidad criptografica, ID, contrasena temporal y estado, con persistencia real.

**Entregables.**
- Generacion y almacenamiento de clave ECDSA P-256 (CNG o DPAPI).
- `DeviceIdentityService`, `DeviceId`, `TemporaryPasswordService`.
- ID local provisional y formateo; el ID definitivo se asigna en Fase 4.
- Pantalla de Inicio con ID, contrasena, mostrar/ocultar, copiar, regenerar, estado de disponibilidad y estado de servidor (desconectado hasta Fase 4).
- Persistencia de identidad y ajustes; migracion de esquema.
- Single instance y arranque con Windows (configurable, aun sin servidor).
- `IClock`/`TimeProvider`, validacion y pruebas de entropia/formato.
- Registro de auditoria local de regeneracion de contrasena.

**Archivos principales.**
- `Core/Devices/DeviceId.cs`, `DeviceIdentity.cs`.
- `Security/Identity/*`, `Security/Passwords/*`.
- `Application/Devices/*`, `Application/Abstractions/*`.
- `Infrastructure/Security/CngKeyStore.cs`, `DpapiKeyStore.cs`.
- `UI/Views/HomePage.xaml`, `ViewModels/HomeViewModel.cs`.
- Pruebas de identidad, hash, comparacion en tiempo constante y persistencia.

**Criterios de aceptacion.**
- La clave privada no aparece en logs, disco plano ni repositorio.
- La contrasena se genera con `RandomNumberGenerator`, tiene 12 caracteres y se puede regenerar.
- Copiar ID/contrasena usa el portapapeles y muestra confirmacion.
- Al reiniciar, el ID y la clave se conservan; la contrasena se conserva o se regenera segun el ajuste.
- Pruebas de que dos identidades no colisionan (estadistico) y de que el hash verifica correctamente.
- No hay ninguna llamada de red todavia.

**Riesgos.** CNG no exportable puede fallar en algunos perfiles; fallback DPAPI probado. La contrasena en `string` no se puede borrar de memoria; se usa `char[]` para la generacion y se minimiza su vida.

---

## 5. Fase 4 - Servidor central, registro, presencia y senalizacion

**Objetivo.** Servidor independiente funcional con dispositivos, autenticacion por desafio, presencia y WebSocket de senalizacion.

**Entregables.**
- `CSIViewer.Server` ASP.NET Core con Kestrel HTTPS, configuracion y health.
- EF Core SQLite, migraciones y modelos `Devices`, `EnrollmentTokens`, `Sessions`, `AuditEvents`.
- API de inscripcion con token de un solo uso; asignacion de ID unico.
- Autenticacion ECDSA por desafio; tokens con caducidad y revocacion.
- Presencia online/offline con heartbeat y ultima conexion.
- WebSocket `/signal` con envelope JSON, validacion, rate limit y limites.
- Mensajes `connect.request`, `connect.pending`, `connect.response`, `candidate`, `relay.allocate`, `ping/pong`.
- Cliente `WebSocketSignalingClient` con reconexion y backoff.
- UI: configuracion de servidor, codigo de inscripcion, estado online, ID definitivo, diagnostico de conexion.
- Servicio de Windows para el servidor y script de publicacion.
- Pruebas de integracion con `WebApplicationFactory` y dos clientes WebSocket.

**Archivos principales.**
- `Server/Program.cs`, `Api/DevicesEndpoints.cs`, `Signaling/SignalingSocketHandler.cs`.
- `Server/Persistence/ServerDbContext.cs`, migraciones.
- `Server/Devices/DeviceRegistry.cs`, `Security/ChallengeService.cs`.
- `Networking/Signaling/WebSocketSignalingClient.cs`, `ReconnectPolicy.cs`.
- `UI/Views/SettingsPage.xaml` (conexion), `HomePage` (estado).
- `tests/CSIViewer.IntegrationTests/Signaling/*`.

**Criterios de aceptacion.**
- Un cliente se inscribe con token valido y recibe ID de 9 digitos unico.
- Un token usado o expirado se rechaza; un ID inexistente no revela informacion.
- La autenticacion por desafio falla con firma incorrecta y funciona con la correcta.
- Dos clientes se ven online/offline en menos de 2 s.
- El servidor rechaza mensajes sobredimensionados, tipos desconocidos y no autenticados.
- Reconexion con backoff tras reiniciar el servidor.
- Pruebas de rate limit y timeout de autenticacion.
- El servidor arranca como consola y como servicio de Windows.

**Riesgos.** TLS autohospedado; documentar certificados. SQLite concurrente; WAL y timeout. Crecimiento del protocolo; versionado desde el inicio.

---

## 6. Fase 5 - Seguridad de sesion: handshake E2E, contrasena y permisos

**Objetivo.** Establecer una sesion autenticada y cifrada de extremo a extremo, con consentimiento y permisos independientes, aun sin video.

**Entregables.**
- Handshake E2E completo: ECDH P-256, firmas, HKDF, confirmacion.
- `SessionCrypto` con AES-GCM, nonces, ventana anti-replay y rotacion.
- Verificacion PBKDF2 de la contrasena temporal en el anfitrion.
- Solicitud de conexion, dialogo de consentimiento, aceptacion/rechazo y permisos.
- Tickets de sesion, expiracion y revocacion.
- Rate limit y bloqueo por fuerza bruta en anfitrion y servidor.
- Auditoria de autenticacion, consentimiento y cambios de permisos.
- UI de consentimiento con identidad, huella, permisos y temporizador.
- Pruebas con vectores, replay, firma invalida, contrasena incorrecta, bloqueo y permisos.

**Archivos principales.**
- `Security/KeyExchange/*`, `Security/Encryption/*`, `Security/Passwords/*`.
- `Application/Sessions/SessionHandshakeService.cs`, `ConsentService.cs`.
- `Protocol/Signaling/ConnectMessages.cs`.
- `UI/Views/ConsentDialog.xaml`, `ViewModels/ConsentViewModel.cs`.
- `tests/UnitTests/Security/*`, `IntegrationTests/Sessions/*`.

**Criterios de aceptacion.**
- Dos clientes derivan las mismas claves y verifican las firmas.
- Un servidor que altere una clave efimera hace fallar la sesion.
- Una contrasena incorrecta no muestra el dialogo de consentimiento.
- Tras 5 fallos se aplica bloqueo; el bloqueo se libera con el tiempo configurado.
- Los mensajes reproducidos o con secuencia antigua se rechazan.
- Conceder solo `ViewScreen` impide cualquier evento de entrada.
- **Revocar todo** cierra la sesion y rota la contrasena.
- Pruebas de que la contrasena no aparece en logs ni en la base de datos.

**Riesgos.** Criptografia propia; revision externa recomendada. Compatibilidad de CNG/ECDH; pruebas en x64 y arm64. Errores de reloj; no depender de timestamps para la seguridad.

---

## 7. Fase 6 - Conexion completa entre dos equipos (sin video)

**Objetivo.** Un controlador y un anfitrion establecen una sesion real, eligen transporte y mantienen un canal fiable con chat de prueba y latencia medida.

**Entregables.**
- Flujo completo de conexion saliente/entrante con estados y UI.
- Candidatos LAN, TLS/TCP directo y relay WSS.
- `ConnectionRace` con timeouts y seleccion de ruta.
- STUN cliente y servidor integrado; candidatos reflexivos.
- Relay WSS autenticado con ticket, cuotas y backpressure.
- Canal fiable sobre TLS/TCP y WSS con framing, cifrado, ACK y reconexion; base para el adaptador UDP.
- Deteccion de host offline, rechazo, host ocupado, timeout y sesion duplicada.
- UI de sesion: estado, ruta, latencia, boton finalizar, permisos.
- Indicador de sesion activa y bandeja.
- Pruebas de integracion: dos clientes en loopback, LAN simulada, relay, corte y reconexion.

**Archivos principales.**
- `Networking/Reliable/TlsReliableChannel.cs`, `RelayReliableChannel.cs`.
- `Networking/Nat/StunClient.cs`, `NatTraversalService.cs`.
- `Server/Relay/RelayService.cs`, `Server/Stun/StunServer.cs`.
- `Application/Sessions/ConnectionCoordinator.cs`.
- `UI/Views/RemoteControlPage.xaml` (estado, sin video aun).
- `tests/IntegrationTests/Connection/*`.

**Criterios de aceptacion.**
- Dos clientes se conectan por LAN directa en menos de 2 s tras el consentimiento.
- Si se bloquea el puerto directo, la conexion cae a relay sin intervencion.
- Un segundo controlador recibe `HostBusy`.
- Un corte de red de 10 s produce `Reconnecting` y luego `Connected` con backoff.
- El servidor cae y se reinicia; los clientes se reconectan.
- El chat de prueba viaja cifrado y no aparece en logs del servidor.
- La UI nunca se congela durante la conexion.

**Riesgos.** NAT y firewalls; pruebas en redes reales. Relay como cuello de botella; medir. TLS directo con certificado autofirmado; pinning.

---

## 8. Fase 7 - Captura, codificacion, transmision y renderizado de video

**Objetivo.** Ver el escritorio remoto con latencia y calidad utilizables.

**Entregables.**
- Enumeracion de monitores, seleccion y captura con `Windows.Graphics.Capture`.
- Frame pool free-threaded, manejo de cierre/reinicio y cambio de monitor.
- Codificador H.264 Media Foundation hardware + fallback software.
- Configuracion CBR, low-latency, sin B-frames, keyframes.
- Fragmentacion UDP, AES-GCM, secuencia, reensamblado y perdida.
- Canal fiable ligero sobre UDP para control y chat (ACK, retransmision, orden, ventana 64), con fallback TCP/WSS.
- Decodificador H.264 y renderizado D3D11 en `SwapChainPanel` con shader NV12.
- Fallback de video por WSS con cola acotada.
- Ajuste automatico de resolucion, calidad/rendimiento, FPS y bitrate.
- Estadisticas: FPS, latencia, bitrate, frames descartados, codec, GPU.
- Pruebas de roundtrip encode/decode y captura a archivo; prueba manual en dos equipos.

**Archivos principales.**
- `Media/Capture/WindowsGraphicsCaptureService.cs`.
- `Media/Encoding/MediaFoundationH264Encoder.cs`, `Media/Decoding/*`.
- `Media/Rendering/D3D11VideoRenderer.cs`.
- `Networking/Media/UdpDataChannel.cs`, `VideoFragmenter.cs`, `ReliableUdpChannel.cs`.
- `Application/Sessions/RemoteVideoService.cs`.
- `UI/Views/RemoteControlPage.xaml` (canvas, toolbar, estadisticas).
- `tests/MediaTests/*`.

**Criterios de aceptacion.**
- Se ve el monitor seleccionado a 30 FPS en LAN con latencia <= 150 ms (medida).
- El uso de CPU es razonable y se usa hardware cuando esta disponible.
- Cambiar de monitor funciona sin reiniciar la sesion.
- Perder un frame no congela la imagen; se recupera con keyframe.
- El video cae a WSS si UDP esta bloqueado.
- La sesion se cierra y libera GPU/memoria sin fugas evidentes.
- Pruebas de roundtrip con resoluciones 720p, 1080p y 1440p.

**Riesgos (los mas altos del proyecto).** MFT hardware irregular; COM/MF; HDR; multiples GPU; DRM; captura de UAC. Mitigacion: vertical slice temprano, fallback software, matriz de hardware, pruebas de estres.

---

## 9. Fase 8 - Control de raton y teclado

**Objetivo.** Controlar el equipo remoto con precision y seguridad.

**Entregables.**
- Inyeccion `SendInput` de raton absoluto, botones, doble clic, rueda, arrastre.
- Teclado por scancode, teclas extendidas, Unicode/IME y combinaciones.
- Mapeo de coordenadas con DPI y monitor; multi-monitor.
- Captura de teclado en la vista remota, supresion de atajos locales cuando corresponda.
- Liberacion de teclas/botones al perder foco o cerrar.
- Modo solo visualizacion vs control; permiso `ControlInput`.
- Pantalla completa y ajuste de aspecto.
- Deteccion de UIPI y mensaje claro; `HostService` opcional para ventanas elevadas.
- Pruebas en ventana de prueba, DPI 100/150/200%, multi-monitor.

**Archivos principales.**
- `Media/Input/SendInputInjector.cs`, `InputEventMapper.cs`.
- `Media/Monitors/MonitorService.cs`.
- `UI/Controls/RemoteCanvas.xaml.cs` (foco, teclado, puntero).
- `HostService/Input/InputPipeServer.cs` (opcional).
- `tests/MediaTests/Input/*`.

**Criterios de aceptacion.**
- El puntero remoto aparece donde se pulsa, con escalado y monitor correctos.
- Clic, doble clic, arrastre y rueda funcionan en una app de prueba.
- Las teclas no se quedan atascadas al cambiar de ventana.
- Sin permiso de control, no se envia ningun evento.
- En ventanas elevadas se informa de la limitacion; con HostService aprobado, funciona.
- Combinaciones como Ctrl+Shift+Esc se envian; Win+L y Ctrl+Alt+Del se documentan como limitados.

**Riesgos.** UIPI, SAS, atajos del sistema, layouts de teclado, DPI. Mitigacion: permisos, avisos, HostService opcional, pruebas por hardware.

---

## 10. Fase 9 - Chat en tiempo real

**Objetivo.** Comunicacion fluida durante la sesion y en modo solo chat.

**Entregables.**
- Mensajes de texto con hora, remitente y estado enviado/recibido.
- Historial local cifrado, hilos por dispositivo/contacto.
- Notificaciones de mensajes nuevos y badge.
- Copiar mensaje, desplazamiento automatico, conservacion durante la sesion.
- Chat sin control remoto (solo chat) con permisos.
- Deduplicacion por `clientMessageId`, ACK y reintento.
- UI de mensajeria moderna con virtualizacion.
- Pruebas de envio/recepcion, orden, duplicados, reconexion e historial.

**Archivos principales.**
- `Core/Chat/*`, `Application/Chat/ChatService.cs`.
- `Protocol/Messages/ChatMessages.cs`.
- `Infrastructure/Persistence/ChatRepository.cs`.
- `UI/Views/ChatPage.xaml`, `ViewModels/ChatViewModel.cs`.
- `tests/UnitTests/Chat/*`, `IntegrationTests/Chat/*`.

**Criterios de aceptacion.**
- Dos equipos intercambian mensajes en < 500 ms en LAN.
- El historial persiste y esta cifrado en reposo.
- Un mensaje duplicado no se muestra dos veces.
- Al reconectar, el chat continua sin perder mensajes confirmados.
- El chat funciona sin permiso de control ni captura.
- La UI hace scroll al ultimo mensaje y notifica si la ventana no esta activa.

**Riesgos.** Mensajes offline (no prometidos en v1), cifrado de historial, notificaciones. Mitigacion: alcance claro y pruebas.

---

## 11. Fase 10 - Transferencia de archivos

**Objetivo.** Transferir archivos y carpetas de forma fiable, segura y reanudable.

**Entregables.**
- FilePicker/FolderPicker con HWND interop.
- Manifiesto, chunks de 1 MiB, SHA-256 incremental, archivo temporal y rename atomico.
- Progreso individual/global, velocidad, tamano, tiempo restante.
- Pausar, reanudar, cancelar; reanudacion tras reinicio.
- Multiples archivos/carpetas, concurrencia acotada, cola.
- Conflictos: preguntar, sobrescribir, renombrar, omitir.
- Limite de velocidad global y por transferencia.
- Verificacion de integridad y deteccion de archivos incompletos.
- Carpeta de destino configurable, abrir ubicacion, notificaciones.
- Validacion de rutas, espacio libre, permisos y nombres reservados.
- Pruebas con archivos de 1-4 GB, carpetas, interrupciones y corrupcion simulada.

**Archivos principales.**
- `Application/Transfers/FileTransferService.cs`, `TransferQueue.cs`.
- `Protocol/Messages/FileMessages.cs`.
- `Infrastructure/FileSystem/TransferFileWriter.cs`, `PathValidator.cs`.
- `Infrastructure/Persistence/TransferRepository.cs`.
- `UI/Views/FileTransferPage.xaml`, `ViewModels/FileTransferViewModel.cs`.
- `tests/IntegrationTests/Transfers/*`.

**Criterios de aceptacion.**
- Un archivo de 2 GB se transfiere sin cargarlo en memoria y con hash correcto.
- Una carpeta con 100 archivos conserva la estructura y rechaza rutas maliciosas.
- Pausar y reanudar continua desde el offset confirmado.
- Un corte de red a mitad se reanuda tras reconectar.
- Un hash incorrecto marca la transferencia como fallida y no publica el archivo.
- No se ejecuta ningun archivo recibido.
- El progreso y la velocidad son correctos y la UI no se bloquea.

**Riesgos.** Espacio en disco, permisos, archivos bloqueados, reparse points, velocidad del relay. Mitigacion: validacion previa, diario, pruebas de caos.

---

## 12. Fase 11 - Dispositivos, contactos e historial

**Objetivo.** Agenda y trazabilidad completas.

**Entregables.**
- Dispositivos conocidos: alta/edicion/borrado manual, alias, descripcion, listas personalizadas, favorito, busqueda, ultimo acceso, estado online, ID, eliminar.
- Ficha de dispositivo con huella, notas y acciones.
- Contactos que agrupan dispositivos; alta/edicion/borrado.
- Historial: fecha, equipo, ID, duracion, tipo de conexion, resultado, motivo; busqueda y filtros.
- Persistencia EF Core y migraciones.
- Sincronizacion de presencia en vivo.
- Exportacion de historial a CSV (opcional, local).
- Pruebas de repositorios, filtros y migraciones.

**Archivos principales.**
- `Core/Devices/*`, `Core/History/*`.
- `Application/Devices/DeviceCatalogService.cs`, `HistoryService.cs`.
- `Infrastructure/Persistence/*`.
- `UI/Views/DevicesPage.xaml`, `ContactsPage.xaml`, `HistoryPage.xaml`, ViewModels.
- `tests/UnitTests/Persistence/*`.

**Criterios de aceptacion.**
- Guardar un dispositivo persiste tras reiniciar.
- La busqueda filtra por nombre, ID y descripcion.
- El estado online se actualiza sin recargar la pagina.
- El historial registra una sesion completa con duracion y motivo.
- Eliminar un dispositivo no borra el historial (o lo anonimiza segun ajuste).
- Las migraciones actualizan una base existente sin perder datos.

**Riesgos.** Esquema evolutivo, datos duplicados, contactos vs dispositivos. Mitigacion: IDs estables, indices unicos, migraciones probadas.

---

## 13. Fase 12 - Reconexion, estabilidad y gestion de errores

**Objetivo.** Soportar condiciones reales sin romper la app.

**Entregables.**
- Maquinas de estado completas y transiciones invalidas rechazadas.
- Timeouts, cancelacion, reintentos y backoff en todas las rutas.
- Reconexion de senalizacion, sesion y transferencias.
- Deteccion de sesiones duplicadas y carreras; creacion atomica.
- Recuperacion ante caida del servidor, cierre del peer, reinicio, suspension y bloqueo.
- Errores tipados, mapeo a mensajes localizados y correlationId.
- Logs estructurados, rotacion y exportacion de diagnostico.
- Watchdogs de captura, codec y red; keyframe automatico.
- Limites de memoria, colas acotadas y liberacion de recursos.
- Configuracion completa: tema, idioma, calidad, FPS, resolucion, aceleracion, carpeta de descargas, limites, notificaciones, permisos, contrasena temporal, inicio automatico autorizado y privacidad. Se guarda y valida; los cambios se aplican en caliente cuando sea posible.
- Aclaracion de seguridad: el inicio automatico de la aplicacion y la reconexion al servidor nunca implican aceptar sesiones automaticamente.
- Pruebas de caos: perdida, latencia, duplicados, cortes, disco lleno, permisos.

**Archivos principales.**
- `Application/Common/RetryPolicy.cs`, `ErrorMapper.cs`.
- `Networking/Reconnect/*`, `Media/Capture/CaptureWatchdog.cs`.
- `Infrastructure/Logging/JsonLinesFileLoggerProvider.cs`.
- `UI/Controls/ErrorBanner.xaml`, `DiagnosticsPage`.
- `tests/IntegrationTests/Resilience/*`.

**Criterios de aceptacion.**
- Cortar Internet 30 s y recuperar la sesion o cerrar con mensaje claro.
- Dos controladores simultaneos: uno gana, el otro recibe `HostBusy`.
- Cerrar el peer abruptamente no deja la UI bloqueada.
- Una transferencia interrumpida se reanuda sin corromper el archivo.
- Falta de disco produce un error claro y no un crash.
- Los logs permiten reconstruir una sesion fallida sin secretos.
- No hay fugas de memoria en 8 horas de sesion (medicion).
- Todos los ajustes persisten tras reiniciar y validan rangos; un valor invalido se rechaza con un mensaje claro.
- El inicio automatico arranca la app y reconecta al servidor, pero nunca acepta una sesion sin consentimiento.

**Riesgos.** Estados imposibles, carreras, deadlocks. Mitigacion: state machines, tests de concurrencia, analisis de volcados.

---

## 14. Fase 13 - Optimizacion, pruebas y accesibilidad

**Objetivo.** Rendimiento, calidad y cobertura antes de distribuir.

**Entregables.**
- Perfilado de CPU, GPU, memoria y red; optimizacion de buffers y colas.
- Pruebas de latencia en LAN, Internet, 3G/4G simulada y redes con perdida; comparacion de control por UDP, TCP y WSS.
- Ajuste de RTO, ventana y descarte de movimientos de raton segun las mediciones.
- Matriz de hardware: Intel, NVIDIA, AMD, ARM64; Windows 10/11; DPI 100-200%.
- Pruebas de accesibilidad (Narrator, teclado, alto contraste).
- Localizacion es/en y revision de textos.
- Fuzzing de protocolo y archivos.
- Pruebas de seguridad: replay, MITM simulado, path traversal, rate limit, permisos.
- Benchmarks de transferencia y arranque.
- Documentacion de diagnostico y manual de usuario.
- Revision de dependencias y licencias.

**Criterios de aceptacion.**
- Metas de latencia/CPU/memoria medidas y documentadas.
- Sin fugas en pruebas prolongadas.
- Sin vulnerabilidades conocidas de dependencias.
- La app funciona con teclado y lector de pantalla en las tareas principales.
- Tests de caos y fuzzing pasan.
- Se documentan las limitaciones que no se pueden resolver.

**Riesgos.** Hardware no disponible, redes reales, tiempo. Mitigacion: matriz minima y pruebas en VM; documentar lo no probado.

---

## 15. Fase 14 - Instaladores, despliegue y distribucion

**Objetivo.** Producto instalable y servidor desplegable.

**Entregables.**
- MSIX firmado para cliente x64/arm64; tarea de inicio; notificaciones; actualizaciones.
- Instalador/paquete del servidor como servicio de Windows; script de configuracion TLS.
- Guia de despliegue: puertos, firewall, certificados, backup, actualizacion.
- Firma de codigo, versionado y changelog.
- Politica de privacidad y aviso de consentimiento.
- Checklist de release y rollback.
- Verificacion en maquinas limpias Windows 10/11.

**Criterios de aceptacion.**
- Instalacion limpia, primer arranque, inscripcion y conexion entre dos equipos.
- Actualizacion conserva datos y ajustes.
- El servidor arranca automaticamente y sobrevive a reinicios.
- Los instaladores estan firmados y no muestran advertencias graves.
- Se puede desinstalar sin dejar servicios ni claves huerfanas.
- Documentacion completa y probada por alguien ajeno al desarrollo.

**Riesgos.** Certificados, SmartScreen, MSIX y elevacion, actualizaciones. Mitigacion: pruebas en maquinas limpias y firma temprana.

---

## 16. Ruta critica y dependencias entre fases

```text
Fase 1 (aprobacion)
  -> Fase 2 (UI/temas)
  -> Fase 3 (identidad)
  -> Fase 4 (servidor/senalizacion)
  -> Fase 5 (seguridad E2E)
  -> Fase 6 (conexion)
  -> Fase 7 (video)  <-- mayor riesgo tecnico
  -> Fase 8 (entrada)
  -> Fase 9 (chat)
  -> Fase 10 (archivos)
  -> Fase 11 (agenda/historial)
  -> Fase 12 (resiliencia)
  -> Fase 13 (optimizacion/pruebas)
  -> Fase 14 (distribucion)
```

Fases 9, 10 y 11 pueden solaparse parcialmente si la Fase 6 esta estable, pero no se presentaran como terminadas hasta pasar sus pruebas. La Fase 7 es la que mas riesgo concentra; se recomienda no iniciar la Fase 10 hasta tener video estable.

---

## 17. Primer vertical slice recomendado (Fases 2-6)

Para reducir riesgo antes de invertir en medios:

1. Ventana WinUI con tema y navegacion.
2. Identidad local y contrasena.
3. Servidor con registro y presencia.
4. Handshake E2E y consentimiento.
5. Dos clientes conectados por LAN y relay con un canal fiable de prueba.
6. Medicion de latencia y pruebas de corte.

Este slice demuestra la columna vertebral completa antes del codec. Si falla la conectividad o el consentimiento, es mas barato corregirlo ahora.

---

## 18. Estimacion relativa

No se promete una fecha sin equipo y dedicacion conocidos. Como referencia de esfuerzo relativo:

| Bloque | Esfuerzo |
| --- | --- |
| Fases 1-6 (columna vertebral) | Alto |
| Fase 7 (video/Media Foundation) | Muy alto |
| Fase 8 (entrada/privilegios) | Alto |
| Fases 9-11 (chat, archivos, agenda) | Medio-alto |
| Fase 12 (resiliencia) | Alto |
| Fases 13-14 (calidad/distribucion) | Medio-alto |

La complejidad no esta en la UI, sino en el pipeline de medios, la conectividad NAT y la robustez.

---

## 19. Aprobacion de Fase 1

Para avanzar a la Fase 2 se requiere confirmar:

- La arquitectura de capas y proyectos.
- La estrategia de transporte (WSS + UDP + relay, sin WebRTC).
- El modelo de consentimiento y permisos.
- El alcance de la version 1 (sin audio, sin portapapeles, sin acceso desatendido).
- El nombre provisional `CSIViewer`.
- Las preguntas abiertas de [07-estado.md](07-estado.md).
