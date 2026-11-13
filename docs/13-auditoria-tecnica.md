# Auditoría técnica completa

Fecha: 2026-11-12. Auditoría sobre el árbol actual de CSIViewer (WinUI 3 + ASP.NET Core, .NET 10).

## 1. Resumen ejecutivo

La aplicación está **funcionalmente completa en el alcance local/LAN** y las mejoras recientes conviven correctamente: compila en Debug y Release, la batería de estabilidad pasa 17 escenarios de 18 (el restante requiere hardware real) y las 154 pruebas unitarias pasan.

No está lista para producción en Internet ni para despliegues desatendidos sin una revisión adicional. Los motivos están en la sección de riesgos: código de inscripción por defecto débil, HTTP por defecto en local, instalador elevado con un archivo temporal, configuración del servicio en la línea de comandos, ausencia de TURN/ICE, falta de revisión criptográfica formal y varios escenarios que requieren hardware/administrador.

### Problemas corregidos en esta auditoría

1. **Contraseña temporal mostrada con guiones que no autenticaba.** Se mostraba `ABCD-EFGH-IJKL`, pero el hash se calculaba sobre `ABCDEFGHIJKL`. Se añadió normalización de la entrada temporal (guiones, espacios y minúsculas), manteniendo las contraseñas personalizadas sin normalizar.
2. **Logs de texto y crash logs sin límite.** `server.log`, `host.log` y `ui-crash.log` crecían indefinidamente. Se añadió rotación a `.1` con límites de 5 MiB y 2 MiB.
3. **El dispositivo D3D11 de Windows.Graphics.Capture no se liberaba explícitamente.** Se libera al detener la captura.
4. **Carrera al liberar el semáforo de envío del servidor.** Un envío en vuelo podía llamar a `Release` sobre un semáforo ya liberado durante el cierre; ahora la excepción de cierre se absorbe.
5. **La ventana cerrada podía quedar retenida** por el delegado estático `RemoteClient.MainWindowSessionBanner`; se limpia al cerrar.
6. **Los códecs H.264 no se liberaban de forma explícita en el apagado del cliente**; ahora se liberan tras detener la captura.
7. **El instalador elevado usaba un script temporal en `%TEMP%`.** Ahora pasa el script como `-EncodedCommand`; ya no hay código ejecutable en un archivo temporal que un atacante local pueda sustituir.
8. **Los logs incluían rutas absolutas de archivos.** Se sustituyeron por rutas relativas o nombres de archivo.
9. **Código muerto** en `ServerSettingsStore` eliminado y documentación de pruebas actualizada.

## 2. Arquitectura y organización

| Proyecto | Responsabilidad | Valoración |
|---|---|---|
| `CSIViewer.Core` | Permisos, estados, utilidades de dominio | Correcta, sin dependencias de UI |
| `CSIViewer.Protocol` | DTO, frames, validación de mensajes | Correcta |
| `CSIViewer.Security` | Identidad, ECDH, AES-GCM, PBKDF2, caducidad, pinning | Correcta, con revisión criptográfica pendiente |
| `CSIViewer.Application` | Ajustes, transferencia entrante, rutas | Correcta |
| `CSIViewer.Networking` | WebSocket, relay, API, STUN, LAN directa | Correcta |
| `CSIViewer.Media` | Captura WGC/GDI, H.264, entrada | Correcta |
| `CSIViewer.Infrastructure` | SQLite, DPAPI, ajustes, logs | Correcta |
| `CSIViewer.Services` | Orquestación: identidad, conexión, sesiones, archivos | Correcta; `RemoteClient` es grande pero está dividido por responsabilidades |
| `CSIViewer` | UI WinUI | Correcta; páginas grandes |
| `CSIViewer.Server` | Servidor ASP.NET Core + UI WinUI | Correcta; `ServerHost` y `MainWindow` son grandes |
| `CSIViewerHost` | Servicio/Lanzador desatendido | Correcta |
| `CSIViewer.UnitTests` | 154 pruebas | Buena cobertura de seguridad, red y archivos |
| `CSIViewer.TestHarness` | Herramienta de pruebas | No forma parte del producto |

- No hay referencias circulares; la solución compila con la configuración actual.
- `RemoteClient` aparece 7 veces porque es una clase `partial` dividida por archivos, no una duplicación.
- `App`, `MainWindow` y `CrashLog` aparecen dos veces porque pertenecen a dos ejecutables distintos.
- `DirectHello` y `Entry` son tipos anidados en clases diferentes; no son duplicados de tipo.
- **Deuda de mantenibilidad:** `CrashLog`, la activación de instancia única y la exportación de diagnóstico están duplicadas entre cliente y servidor. No es un fallo funcional, pero conviene unificarlas en un pequeño proyecto compartido.
- No se encontraron `TODO`, `FIXME`, `NotImplementedException` ni métodos vacíos con apariencia de implementación. Los únicos bloques vacíos son constructores, `catch (OperationCanceledException)` deliberados y pruebas.
- `dotnet format --verify-no-changes` no pasa: detecta 2.789 finales de línea, 876 espacios y 1 codificación fuera de la política de `.editorconfig`. Es deuda de formato, no un fallo funcional; conviene ejecutar `dotnet format` una vez y añadirlo al CI.

## 3. Conexiones, sesiones y reconexión

- Máquina de estados explícita y validada (`Disconnected`, `Connecting`, `Authenticating`, `Connected`, `Reconnecting`, `Failed`, etc.).
- Heartbeat con watchdog y reconexión con backoff, jitter, límite de reintentos y cancelación.
- Sesiones simultáneas limitadas por host (10 por defecto), una sesión por controlador/host y una única concesión de control.
- Limpieza de sesiones al desconectar, al cerrar, al revocar y al expirar.
- **Verificado:** dos clientes simultáneos; dos oleadas de 11 solicitudes (20 aceptadas, 2 `host_full`); caída y reinicio del servidor; reinicio del cliente; cliente congelado; revocación; expiración.
- **Hallazgo:** el servidor limita a 5 solicitudes de conexión por minuto y por conexión de señalización, y a 10 por dispositivo. Es una protección deliberada; la prueba verifica rechazo limpio y recuperación a los 65 s.

## 4. Autenticación y seguridad

### Verificado

- Identidad ECDSA P-256; registro con código de inscripción y desafío firmado.
- Contraseña temporal PBKDF2-SHA256 con 600.000 iteraciones y sal aleatoria; comparación en tiempo fijo.
- Contraseña personalizada protegida con DPAPI.
- Contraseña temporal con caducidad configurable; se rechaza tras expirar.
- Bloqueo por intentos fallidos y limitación por IP; los tests cubren cinco fallos, bloqueo y expiración.
- Sesión E2E: ECDH P-256, HKDF-SHA256, AES-256-GCM, nonce por contador, comprobación de replay y transcripción firmada por ambos extremos.
- Pinning de claves de dispositivos conocidos; una clave cambiada se rechaza.
- Transporte remoto sin HTTPS rechazado salvo activación explícita; loopback permitido para desarrollo.
- Rutas de archivos validadas contra traversal, rutas absolutas, nombres reservados y caracteres inválidos.
- Archivos recibidos en temporal, SHA-256 verificado y movimiento final solo tras validar.
- Permisos comprobados en host y controlador; el host concede la intersección y solo un cliente tiene el control.

### Riesgos

1. **Código de inscripción por defecto `DEV-1234`.** El servidor avisa, pero un despliegue real debe cambiarlo en el primer uso.
2. **HTTP por defecto en localhost** y HTTP remoto solo con aceptación explícita. Para Internet se debe usar HTTPS/WSS con un certificado válido.
3. **Código de inscripción del servidor como argumento del servicio.** El script `install-server-service.ps1` lo incluye en la línea de comandos del servicio, visible para administradores locales en la configuración del servicio. Conviene una configuración protegida con ACL o DPAPI de máquina.
4. **Protocolo criptográfico propio.** Usa primitivas de la BCL y tiene pruebas, pero no ha pasado una revisión criptográfica externa.
5. **Entrada en ventanas elevadas/UAC:** `SendInput` no atraviesa UIPI ni el escritorio seguro. Es una limitación de Windows, no un fallo, y debe documentarse en el producto.
6. **Datos en reposo:** la identidad y la contraseña personalizada usan DPAPI, pero el historial de chat y de conexiones se guarda en SQLite en claro bajo la carpeta de datos. Si esa carpeta puede leerla otro usuario o se copia a un medio compartido, el historial queda expuesto. Se recomienda cifrado de campos sensibles o SQLCipher, además de ACL estrictas.
7. **Servidor local y navegador:** `/ws` y el registro no validan `Origin`; con el código por defecto, una página web local podría intentar registrar un dispositivo y sondear hosts. Endurecer con validación de `Origin`, token CSRF o un código fuerte obligatorio.

## 5. Captura, control, chat y archivos

- **Captura:** Windows.Graphics.Capture con pool *free-threaded*, cursor, límite de FPS, fallback GDI y watcher de cambios de pantalla. El índice de monitor remoto se valida y cae al primario. Verificado con dos monitores (2048×1152 y 1920×1080) y cambios de monitor con vídeo activo tras cada cambio.
- **WGC en escritorio estático:** puede entregar pocos fotogramas porque Windows solo entrega cambios. No es una fuga; la UI conserva el último fotograma. El soak usa GDI para medir el pipeline continuo.
- **Control:** `SendInput` solo con permiso `ControlInput`, propietario del control y sesión cifrada; libera teclas/botones al terminar. Limitado por UIPI/UAC.
- **Chat:** cifrado dentro de la sesión, validación de longitud/caracteres, historial SQLite y UI con estados de permiso.
- **Archivos:** bloques de 1 MiB, hasta 3 transferencias, SHA-256, temporal + sidecar, reanudación con offset confirmado, cancelación, pausa, reanudación automática y control de espacio. Verificado con 64 MiB (794 ms, 80 MB/s), reanudación desde 2.097.152 bytes y cancelación de 32 MiB sin archivo final. La enumeración de carpetas excluye `ReparsePoint`.

## 6. Errores, logs y diagnóstico

- Los manejadores de UI, AppDomain y tareas no observadas escriben en el log estructurado y en el crash log, sin tumbar el proceso.
- Ajustes corruptos del servidor fallan en cerrado (sin volver al código de desarrollo).
- Logs JSONL con niveles Debug/Information/Warning/Error/Critical, scopes por sesión/transferencia, rotación, retención, límite total y redacción de secretos.
- **Corregido:** `server.log`, `host.log` y `ui-crash.log` ahora rotan y tienen límite.
- Exportación de diagnóstico en ZIP con `diagnostics.txt`, logs estructurados y crash log redactado; no incluye identidad, contraseñas, tokens ni códigos de inscripción.
- **Riesgo:** los logs son locales; no hay telemetría central ni alertas. Para soporte remoto se recomienda un colector opcional.

## 7. Rendimiento y liberación de recursos

- Medidas previas documentadas en `docs/10-rendimiento.md`: BGRA→NV12 directo, H.264 sin `MemoryStream`, lector WebSocket con pool, sin copia por bloque y coalescencia de UI.
- **Esta auditoría:**
  - 7 ciclos: memoria +0,5 MB, hilos 26→22, handles 540→534, TCP 4, tareas pendientes 0.
  - Concurrencia: dos oleadas de 11, working set 131,4→142,6 MB, handles 590→597.
  - Soak 180 s: 1.320 fotogramas, memoria −1 MB, hilos −30, handles −119, tareas pendientes 0.
  - Sin procesos ni puertos de prueba al terminar.
- **Corregido:** liberación explícita del dispositivo D3D11, de los códecs H.264 y del semáforo de envío en cierre.
- **Riesgo menor:** `GdiScreenCapture.Start` usa `StopAsync().GetAwaiter().GetResult()` para detener una captura previa. No se observó bloqueo, pero conviene convertirlo en `StartAsync` para no bloquear el hilo que lo invoque.
- **Riesgo de UX:** la exportación de diagnóstico copia logs y crea el ZIP de forma síncrona dentro de un manejador de UI. Con el límite actual de 200 MB puede congelar la ventana varios segundos. Conviene mover copia y compresión a `Task.Run`/I/O asíncrona.
- **Riesgo menor:** `DpapiIdentityStore` y `DpapiCustomPasswordStore` no tienen un semáforo de escritura; dos guardados simultáneos podrían competir por el mismo `.tmp`.
- **Riesgo de UX:** varios manejadores de UI son `async void` sin `try/catch` (por ejemplo, regenerar contraseña o guardar un toggle). El manejador global evita el cierre, pero el usuario puede no recibir feedback. Conviene un patrón común de error en las vistas.
- **Riesgo de evolución:** el esquema SQLite usa `EnsureCreated` y `ALTER TABLE` manuales, sin migraciones EF versionadas; es funcional hoy, pero frágil para futuras versiones.

## 8. Temas y UI

- Diccionarios de tema `Default`, `Light`, `Dark` y `HighContrast`; colores de marca/estado y fondos adaptados.
- Tema del sistema mediante `ElementTheme.Default`; cliente y servidor aplican el tema y actualizan la barra de título.
- El fondo negro del visor remoto y las superposiciones oscuras son intencionales para vídeo, no un fallo de tema.
- **Corregido anteriormente:** el desplegable de nivel de logs usa una celda azul vertical, se abre hacia arriba y mantiene anchos/altos/espaciados consistentes; ya no se recorta.
- **Pendiente:** validación visual completa en tema claro, oscuro, alto contraste y DPI 100/125/150/200 % en hardware real. El escenario de tema se ejecutó con UI real, pero la sesión remota pertenecía al harness.

## 9. Persistencia y configuración

- `settings.json` atómico (temporal + `File.Move`), validación y valores por defecto.
- Identidad y contraseña personalizada con DPAPI; código de inscripción protegido en ajustes.
- `serversettings.json` atómico y fallo cerrado ante corrupción.
- Tamaño/posición/maximizado de ventanas persistidos y limitados al área visible; servidor y cliente.
- Datos portables en `data` junto al ejecutable, con migración desde `%LOCALAPPDATA%` y limpieza de restos antiguos.
- **Riesgo de despliegue:** en una instalación bajo `Program Files`, el servidor cae a `%ProgramData%` y el host a `%ProgramData%`; el cliente puede caer a `%LOCALAPPDATA%` si la carpeta del host no es escribible. Está documentado, pero conviene que el instalador fije rutas explícitas.
- **Riesgo de servicio:** la UI y el servicio pueden usar archivos de ajustes distintos; el código de inscripción del servicio se pasa por línea de comandos. Hay que unificar la configuración del servicio.

## 10. Dependencias y compatibilidad

- `net10.0` / `net10.0-windows10.0.19041.0`, WinAppSDK 2.6.5, EF Core 10.0.15, MSTest 4.5.3.
- `NuGetAudit` activo; `dotnet list package --vulnerable --include-transitive` no encontró vulnerabilidades.
- `dotnet list package --outdated` solo propone la familia 11.0.0, que implicaría un cambio de plataforma; no se actualiza en esta auditoría.
- Publicación self-contained (`win-x64`) para cliente, host y servidor; WinAppSDK self-contained en cliente/servidor no MSIX.
- Compilación con `CI=true` y `TreatWarningsAsErrors`: **0 advertencias, 0 errores**.
- **Deuda menor:** `Microsoft.AspNetCore.Mvc.Testing` y `Microsoft.EntityFrameworkCore.Design` figuran en las versiones centrales sin un proyecto que los use.

## 11. Pruebas ejecutadas

| Prueba | Resultado |
|---|---|
| `dotnet build -c Debug` | 0 advertencias, 0 errores |
| `dotnet build -c Release` | 0 advertencias, 0 errores |
| `dotnet build` con `CI=true` | 0 advertencias, 0 errores |
| Pruebas unitarias | **154/154** |
| Batería de estabilidad completa | **17 PASS, 0 FAIL, 1 PENDING** |
| Contraseña mostrada con guiones | Conexión real correcta (`CONNECTED=True`, `OK=True`) |
| Paquetes vulnerables | Ninguno |
| Procesos/sockets al terminar | Sin procesos ni puertos de prueba abiertos |

La única prueba pendiente es el cambio real de resolución del escritorio; el cambio de resolución de captura se ejercitó indirectamente con dos monitores de resoluciones distintas.

## 12. Riesgos pendientes

### Seguridad

- Código de inscripción de desarrollo por defecto.
- Instalador desatendido con script temporal elevado (TOCTOU local).
- Código de inscripción del servicio visible en la línea de comandos.
- Protocolo criptográfico propio sin auditoría externa.
- Servidor local acepta WebSocket/HTTP sin validación de `Origin`; con el código por defecto, una página web local podría intentar registrar un dispositivo y sondear hosts. Endurecer con `Origin`, token CSRF o código fuerte.
- HTTP remoto permitido solo por decisión explícita; no usar en Internet sin TLS.

### Estabilidad y despliegue

- Servicio `CSIViewerServidor`/`CSIViewerHost` no instalado realmente con UAC/LocalSystem en esta auditoría.
- Multi-monitor se verificó con dos pantallas; quedan pendientes DPI, cambio de resolución real, pantalla completa y equipos con configuraciones distintas.
- Soak de 180 s; no se ejecutó una prueba de 10+ minutos/24 h.
- No hay TURN/ICE; Internet depende del relay.
- Sin telemetría central ni actualizador firmado.

### Calidad

- Duplicación de utilidades entre cliente y servidor.
- Archivos grandes (`RemoteClient.Session.cs`, `ServerHost.cs`, `MainWindow.xaml.cs`).
- Ausencia de pruebas de integración MSTest para el servidor completo; la cobertura E2E está en PowerShell/harness.
- Cobertura de DPI/alto contraste pendiente.
- `dotnet format --verify-no-changes` falla por finales de línea y espacios (2.789 + 876 incidencias); no afecta a la compilación, pero debe normalizarse y añadirse al CI.

## 13. Recomendaciones priorizadas

### P0, antes de producción

1. Probar la instalación real del servicio con UAC/LocalSystem; el comando codificado ya elimina el archivo temporal de código, pero la instalación real no se ejecutó en esta auditoría.
2. No pasar secretos por la línea de comandos del servicio; usar configuración protegida por ACL/DPAPI de máquina.
3. Obligar a cambiar `DEV-1234` en el primer arranque y rechazar códigos débiles.
4. Exigir HTTPS para cualquier servidor no loopback (ya está el interruptor; convertir la excepción en el camino normal).
5. Validar `Origin` en `/ws` y en el registro, o exigir un token de instalación.
6. Revisión criptográfica externa del protocolo de sesión.

### P1, fiabilidad

1. Convertir `GdiScreenCapture.Start` en asíncrono.
2. Añadir semáforo a los almacenes DPAPI.
3. Excluir reparse points al enviar carpetas.
4. Unificar la configuración del servicio y de la UI.
5. Probar instalación real de servicios, DPI, cambio de resolución real y pantalla completa en una matriz de equipos.

### P2, mantenibilidad

1. Extraer utilidades compartidas (`CrashLog`, instancia única, exportación de diagnóstico).
2. Dividir `ServerHost`/`MainWindow` por responsabilidades.
3. Añadir pruebas de integración MSTest y CI con build + unit + estabilidad.
4. Añadir telemetría opcional y un colector de diagnóstico.
5. Revisar las reglas de análisis suprimidas y migrar logs a `LoggerMessage`.

## 14. Estado real de preparación

| Escenario | Estado |
|---|---|
| Uso local supervisado en un solo equipo | **Apto con condiciones** (código fuerte, HTTPS si sale de loopback) |
| LAN de confianza, asistencia puntual | **Apto con condiciones**; revisar firewall y usar HTTPS |
| Internet / clientes reales | **No listo**: faltan TURN/ICE, TLS de producción, endurecimiento del servidor y revisión criptográfica |
| Empresa / desatendido | **No listo**: instalador/servicio, configuración protegida, auditoría y matriz de hardware pendientes |
| Producción crítica | **No listo** |

La aplicación no debe presentarse como “lista para producción” todavía. Sí está en un estado sólido para pruebas controladas y uso cotidiano supervisado en local/LAN, con las limitaciones indicadas.
