# Registro de decisiones de arquitectura (ADR)

Cada decision se registra con contexto, decision, consecuencias y alternativas descartadas. Las decisiones se pueden revisar, pero un cambio debe motivarse y actualizar los documentos afectados.

---

## ADR-001: .NET 10 LTS + WinUI 3 + Windows App SDK 2.6.5

**Contexto.** Se requiere una aplicacion nativa de Windows, moderna, mantenible y con soporte a largo plazo.

**Decision.** Usar .NET 10 LTS, C# 14, WinUI 3 y Windows App SDK 2.6.5. Target `net10.0-windows10.0.19041.0` para los proyectos Windows y `net10.0` para los multiplataforma. Minimo Windows 10 2004; experiencia completa en Windows 11.

**Consecuencias.**
- Acceso a Mica, notificaciones, ciclo de vida y controles Fluent.
- .NET 10 tiene soporte LTS y el SDK 10.0.404 esta instalado.
- WinUI 3 no incluye plantilla `dotnet new` en este entorno; el proyecto se crea manualmente en Fase 2.
- Algunas APIs Win32 siguen siendo necesarias (entrada, bandeja, monitores).

**Alternativas.** WPF (maduro pero menos moderno), UWP (en declive), MAUI (multiplataforma, no es el objetivo), aplicacion web embebida (rechazada por requisito).

---

## ADR-002: Monolito modular con arquitectura hexagonal

**Contexto.** El proyecto debe ser mantenible, probable y separable, sin la complejidad operativa de microservicios.

**Decision.** Un unico repositorio y una solucion con proyectos por capa; el servidor es un ejecutable independiente. Puertos en `Application`, adaptadores en `Networking`, `Media` e `Infrastructure`. `Services` compone el cliente. La UI no conoce detalles de red ni de sistema operativo.

**Consecuencias.**
- Pruebas unitarias sin UI ni red.
- Dependencias unidireccionales verificables.
- Mas proyectos y algo mas de ceremonia que una app de una sola capa.
- El servidor comparte `Core`, `Protocol` y `Security`, pero no la persistencia del cliente.

**Alternativas.** Microservicios (sobreingenieria para v1), monolito en una sola clase (rechazado), Clean Architecture con mas capas (no aporta valor adicional).

---

## ADR-003: WebSocket nativo para senalizacion, no SignalR

**Contexto.** Se necesita senalizacion fiable, autenticada y de baja sobrecarga entre clientes y servidor.

**Decision.** Usar ASP.NET Core WebSocket sobre TLS con un protocolo JSON documentado. No usar SignalR en v1.

**Consecuencias.**
- Control total del framing, versionado, codigos de error y reconexion.
- Una dependencia menos y menos capas de abstraccion.
- Hay que implementar heartbeat, reconexion y backoff (ya previstos).
- Los proxies deben permitir WebSocket sobre 443.

**Alternativas.** SignalR (util si hubiera muchos clientes web y grupos dinamicos; no es el caso), gRPC (peor soporte para binario de baja latencia en este escenario), TCP crudo (mas problemas con proxies y firewalls).

---

## ADR-004: Sin WebRTC; UDP propio + WSS/TLS con STUN y relay

**Contexto.** Se requiere video en tiempo real, NAT traversal y compatibilidad con redes restrictivas, usando solo tecnologias Microsoft.

**Decision.**
- Senalizacion: WSS 443.
- Video: UDP propio con AES-256-GCM, sin retransmision, con keyframes bajo demanda y fallback WSS.
- Control y chat: canal fiable ligero sobre el mismo UDP P2P (ACK, retransmision, orden, ventana 64); fallback a TLS/TCP directo o WSS relay.
- Archivos: TLS/TCP directo si hay ruta; si no, WSS relay.
- NAT: candidatos locales, STUN integrado y relay propio autenticado (tipo TURN, no TURN completo).
- Interfaz `ITransportFactory` para incorporar QUIC/WebRTC en el futuro.

**Consecuencias.**
- Sin dependencias nativas de WebRTC ni FFmpeg.
- El relay WSS funciona a traves de proxies y firewalls que permiten 443.
- La latencia del control por relay depende de la ubicacion del servidor.
- Hay que implementar y probar STUN, hole punching, fragmentacion, anti-replay, control de congestion y el canal fiable ligero sobre UDP.
- El canal fiable sobre UDP debe manejar perdida, duplicados, reordenacion y reconexion; se cubre con el TestHarness y pruebas de caos.
- `System.Net.Quic` en .NET 10 no expone datagramas; no se usa para video.

**Alternativas.** WebRTC con libwebrtc/SIPSorcery (rompe el requisito de stack Microsoft, anade dependencia nativa y complejidad), QUIC streams (bloqueo de cabeza de cola para video), RDP embebido (no permite la UI y funcionalidad solicitadas), todo por relay WSS (peor latencia y coste de ancho de banda).

**Revision.** Fase 13, con mediciones de latencia, perdida y CPU en redes reales.

---

## ADR-005: Media Foundation H.264 con aceleracion hardware y fallback software

**Contexto.** Hay que codificar video en tiempo real con baja latencia, bajo consumo y compatibilidad.

**Decision.** Usar MFT de Media Foundation:
- H.264 con perfil compatible, sin B-frames, CBR y modo low-latency.
- Enumerar MFT hardware y usar D3D11 cuando este disponible.
- Fallback a MFT software si no hay hardware o falla.
- Enviar SPS/PPS en keyframes; solicitar keyframe tras perdida.
- Decodificar con MFT y renderizar NV12 mediante shader en `SwapChainPanel`.

**Consecuencias.**
- Codec nativo, acelerado y sin dependencias externas.
- La disponibilidad y calidad de MFT hardware varia por GPU y driver; hay que probar Intel, NVIDIA, AMD y ARM64.
- La implementacion COM/MF es compleja y requiere tests de roundtrip.
- H.264 tiene consideraciones de licencia; se revisan antes de distribuir.

**Alternativas.** FFmpeg (dependencia no Microsoft), AV1 (soporte hardware irregular), MJPEG (mucho mas ancho de banda), VP9 (peor soporte nativo).

---

## ADR-006: Windows.Graphics.Capture + Direct3D 11

**Contexto.** Se necesita capturar el escritorio con bajo coste y sin hooks fragiles.

**Decision.** Usar `Windows.Graphics.Capture` con `Direct3D11CaptureFramePool` free-threaded y `GraphicsCaptureSession`, seleccionando un monitor mediante `IGraphicsCaptureItemInterop`. Renderizar en `SwapChainPanel` con D3D11.

**Consecuencias.**
- API soportada y optimizada por Windows.
- Requiere Windows 10 1803+; se target 2004+.
- No captura la pantalla segura de UAC ni contenido DRM protegido.
- Hay que manejar cierre de sesion de captura, cambio de monitor, suspension y bloqueo.
- El cursor se puede incrustar o transmitir por separado.

**Alternativas.** GDI `BitBlt` (lento, no captura composiciones modernas), Desktop Duplication API (mas control pero mas compleja y ligada a DXGI), hooks (fragiles y bloqueados por seguridad).

---

## ADR-007: EF Core + SQLite en cliente y servidor

**Contexto.** Se necesita persistencia local fiable, consultas, migraciones y facilidad de pruebas.

**Decision.** EF Core 10 con SQLite. El cliente usa `ClientDbContext`; el servidor usa `ServerDbContext`. SQLite en modo WAL. SQL Server queda como opcion futura mediante un proveedor EF Core, sin cambiar la logica.

**Consecuencias.**
- Menos SQL manual y migraciones versionadas.
- SQLite es suficiente para un solo nodo y miles de dispositivos; el relay es el limite real.
- La base local no esta cifrada a nivel de pagina; los campos sensibles se cifran a nivel de aplicacion.
- Hay que cuidar el acceso concurrente y los tiempos de bloqueo.

**Alternativas.** SQL Server LocalDB (mas pesado), LiteDB (tercero, no Microsoft), archivos JSON (sin consultas ni transacciones), SQLite directo con ADO.NET (mas codigo).

---

## ADR-008: PBKDF2-SHA256 en lugar de Argon2id

**Contexto.** Se pide Argon2id o PBKDF2. .NET no incluye Argon2id en la BCL; anadir una dependencia de terceros contradice la preferencia por el stack Microsoft.

**Decision.** PBKDF2-SHA256 con 600.000 iteraciones, sal de 16 bytes y salida de 32 bytes. Se encapsula en `IPasswordHasher` con identificador de algoritmo para permitir migracion futura. La contrasena temporal tiene 12 caracteres aleatorios (~60 bits), lo que hace inviable un ataque offline.

**Consecuencias.**
- Implementacion con `Rfc2898DeriveBytes.Pbkdf2`, sin dependencias.
- 600k iteraciones consumen CPU (~0,2-0,5 s por intento), lo que tambien frena la fuerza bruta.
- Si se aprueba Argon2id mediante una libreria auditada, se puede migrar y rehash.

**Alternativas.** Argon2id con Konscious (tercero), BCrypt (tercero), PBKDF2 con menos iteraciones (debil).

---

## ADR-009: Identidad ECDSA/ECDH + DPAPI/CNG

**Contexto.** Se necesita identidad de dispositivo, autenticacion mutua y confidencialidad E2E.

**Decision.**
- Identidad: ECDSA P-256.
- Intercambio: ECDH P-256 efimero por sesion.
- Derivacion: HKDF-SHA256 con transcript.
- Cifrado: AES-256-GCM por direccion y plano.
- Almacenamiento: CNG no exportable o DPAPI.
- El servidor solo guarda claves publicas.

**Consecuencias.**
- Perfect forward secrecy por sesion.
- El servidor no puede descifrar contenido.
- El handshake es propio y requiere revision y vectores de prueba.
- La perdida de la clave privada implica re inscripcion.

**Alternativas.** Certificados X.509 con PKI propia (mas infraestructura), RSA (claves y firmas mas grandes), TLS mutuo con certificados de dispositivo (complica el relay E2E).

---

## ADR-010: CommunityToolkit.Mvvm

**Contexto.** MVVM en WinUI 3 requiere `INotifyPropertyChanged`, comandos y estados asincronos.

**Decision.** Usar `CommunityToolkit.Mvvm` 8.4.2, mantenido por Microsoft/.NET Foundation, con source generators. No se usa un framework MVVM de terceros.

**Consecuencias.**
- Menos boilerplate y menos errores.
- Una dependencia de desarrollo pequena.
- Si se exigiera cero dependencias externas, se puede sustituir por clases base propias sin cambiar los ViewModels.

**Alternativas.** Implementacion propia (mas codigo), Prism (mas pesado), ReactiveUI (modelo reactivo innecesario).

---

## ADR-011: Aplicacion sin empaquetar en desarrollo, MSIX en produccion

**Contexto.** Se necesita iterar rapido y, a la vez, distribuir de forma profesional.

**Decision.** En desarrollo, `WindowsPackageType=None`. En produccion, MSIX firmado con tarea de inicio y notificaciones. El servidor se distribuye como servicio de Windows y publicacion self-contained. `HostService` se instala por separado solo si se aprueba.

**Consecuencias.**
- Iteracion rapida sin reinstalar el paquete.
- MSIX facilita actualizaciones y identidad, pero limita elevacion y algunas APIs.
- La firma de codigo es obligatoria para evitar SmartScreen y para UIAccess.
- Los pickers requieren inicializacion con HWND en ambos modos.

**Alternativas.** Solo MSIX (iteracion mas lenta), solo instalador MSI (mas control pero mas mantenimiento), portable ZIP (poco profesional para produccion).

---

## ADR-012: JSON para control, binario para medios

**Contexto.** El protocolo debe ser legible, versionable y eficiente.

**Decision.**
- Senalizacion y mensajes de control/chat: JSON con source generation.
- Entrada, video y chunks de archivo: binario con `BinaryPrimitives` y cabeceras documentadas.
- Sin protobuf ni MessagePack.

**Consecuencias.**
- JSON facilita diagnostico y evolucion.
- Binario evita overhead en rutas calientes.
- Hay que mantener dos serializadores y validar ambos.
- Los tamanos y versiones se prueban con tests.

**Alternativas.** Todo JSON (mas overhead), todo binario (peor depuracion), protobuf (dependencia y esquema extra).

---

## ADR-013: Un controlador activo por anfitrion (configurable)

**Contexto.** Dos o mas controladores simultaneos pueden causar conflictos de entrada y confusion.

**Decision.** Por defecto, un solo controlador activo por anfitrion. Las solicitudes adicionales se rechazan con `HostBusy`; el usuario puede habilitar una cola de una solicitud o permitir multiples sesiones de solo visualizacion en una fase futura.

**Consecuencias.**
- Estado y entrada deterministas.
- Menos riesgos de seguridad y de rendimiento.
- La regla se aplica en el servidor con creacion atomica de sesion y en el anfitrion como segunda defensa.

**Alternativas.** Multiples controladores con arbitraje (complejo, no requerido), sin limite (peligroso).

---

## ADR-014: Consentimiento explicito y ausencia de acceso oculto

**Contexto.** El acceso remoto debe ser etico y legalmente defendible.

**Decision.** Cada sesion requiere aceptacion visible del usuario del anfitrion. No hay acceso desatendido en v1. El indicador de sesion no se puede ocultar. **Revocar todo** cierra sesiones y regenera la contrasena. No se implementan puertas traseras ni persistencia encubierta.

**Consecuencias.**
- Requiere presencia del usuario para conectar.
- Menor riesgo de abuso.
- Una futura modalidad desatendida exigira consentimiento previo, configuracion visible y revocacion clara; no se implementa por defecto.

**Alternativas.** Acceso desatendido por defecto (rechazado), aceptacion por notificacion sin abrir la app (rechazada).

---

## ADR-015: Logging JSONL propio sobre Microsoft.Extensions.Logging

**Contexto.** Se necesitan logs estructurados, con rotacion y sin dependencias innecesarias.

**Decision.** Implementar un `ILoggerProvider` que escribe JSON Lines en un archivo con cola acotada, rotacion por tamano, retencion y redaccion. En consola y Debug se usan los proveedores de Microsoft.

**Consecuencias.**
- Control del formato y de la redaccion.
- Menos dependencias.
- Hay que probar concurrencia, rotacion y fallos de disco.
- No se obtiene la madurez de Serilog; se compensa con pruebas.

**Alternativas.** Serilog (maduro pero tercero), NLog (tercero), solo ETW (dificil de exportar para soporte).

---

## ADR-016: MSTest 4 + TestHarness de caos

**Contexto.** Se necesitan pruebas unitarias, de integracion y de red reproducible.

**Decision.** MSTest 4.5.3, `Microsoft.NET.Test.Sdk` 18.12.0 y un harness de consola que simula latencia, perdida, cortes y transferencias. Las pruebas de medios se ejecutan en Windows con hardware real y se marcan como condicionales si no hay GPU/MFT.

**Consecuencias.**
- Stack de pruebas Microsoft.
- El harness permite reproducir fallos sin dos equipos.
- La validacion final de codecs y DPI requiere matriz de hardware.

**Alternativas.** xUnit/NUnit (terceros, buenos pero no Microsoft), pruebas manuales solamente (insuficiente).

---

## ADR-017: Un solo servidor, disenado para escalar

**Contexto.** La version 1 debe poder autohospedarse y soportar multiples clientes, sin asumir alta disponibilidad.

**Decision.** Un nodo ASP.NET Core con SQLite e presencia en memoria. Cada conexion y sesion usa estado aislado y canales asincronos. Las interfaces `IPresenceStore` y `ISessionStore` permiten un backplane futuro. El relay es el recurso limitante y se documenta.

**Consecuencias.**
- Despliegue simple y bajo coste.
- No hay failover automatico; si el servidor cae, los clientes reconectan.
- Escalar a varios nodos exigira un backplane (Redis/Azure SignalR) y almacenamiento compartido; fuera de v1.

**Alternativas.** Kubernetes/multinodo desde el inicio (complejidad prematura), servidor serverless (no soporta WebSocket de larga duracion ni relay).

---

## ADR-018: Icono de bandeja e indicador de sesion con Win32

**Contexto.** WinUI 3 no incluye un control de bandeja nativo.

**Decision.** Implementar una ventana oculta Win32 con `Shell_NotifyIcon`, menu contextual y callbacks. El indicador de sesion se muestra ademas en la ventana y en la taskbar.

**Consecuencias.**
- Sin dependencias de terceros.
- Hay que gestionar el ciclo de vida de la ventana oculta y el icono.
- Se prueba en Windows 10/11 y con escalado DPI.

**Alternativas.** H.NotifyIcon (tercero), no tener bandeja (peor experiencia y menos visibilidad de sesion).

---

## ADR-019: Chunking de 1 MiB y SHA-256 incremental

**Contexto.** La transferencia debe ser robusta, reanudable y verificar integridad sin cargar archivos completos.

**Decision.** Bloques de 1 MiB, escritura en archivo temporal, `IncrementalHash` SHA-256, diario SQLite y rename atomico. Limite de velocidad con token bucket.

**Consecuencias.**
- Memoria acotada y reanudacion sencilla.
- 1 MiB equilibra overhead y granularidad; se puede ajustar por configuracion.
- Los archivos muy grandes requieren comprobar espacio y manejar interrupciones.

**Alternativas.** Bloques de 64 KiB (mas overhead), archivo completo en memoria (inaceptable), sin hash (inaceptable).

---

## ADR-020: No se implementa audio, portapapeles ni acceso desatendido en v1

**Contexto.** El alcance es enorme; la prioridad es un producto funcional y seguro.

**Decision.** Aplazar audio, portapapeles bidireccional y acceso desatendido. Se documentan como extensiones y no se muestran como terminados.

**Consecuencias.**
- Menos superficie de ataque y menos complejidad en la primera version.
- La UI no debe prometer estas funciones.
- Se pueden anadir despues sin romper la arquitectura.

**Alternativas.** Incluirlos desde el inicio (riesgo de retraso y fallos), simularlos con UI (rechazado).
