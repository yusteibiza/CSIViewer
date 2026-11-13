# Riesgos tecnicos y limitaciones de Windows

Version: 1.0 (Fase 1)
Fecha: 2026-11-12

Este documento existe para no prometer lo que Windows no permite y para planificar mitigaciones antes de que aparezcan en produccion.

---

## 1. Limitaciones duras de Windows

### 1.1 Seguridad de escritorio (UAC, Ctrl+Alt+Del, bloqueo)

- `Windows.Graphics.Capture` **no captura el escritorio seguro de UAC**, la pantalla de bloqueo ni el escritorio de inicio de sesion. Son escritorios distintos y aislados por el sistema.
- Un proceso de usuario normal **no puede inyectar entrada en ventanas elevadas** de otro proceso de mayor integridad (UIPI).
- **Ctrl+Alt+Del** es una secuencia de atencion segura (SAS). No se puede sintetizar con `SendInput`. Solo un servicio con privilegios y la directiva `SoftwareSASGeneration` habilitada puede usar `SendSAS`.
- **Win+L** bloquea el equipo remoto y suele cortar la sesion; no se puede evitar de forma fiable.
- Algunas combinaciones (Alt+Tab, Ctrl+Alt+Esc, teclas de medios) las intercepta el sistema antes de la app. Se pueden reenviar mediante menus de teclas especiales y hooks, pero no todas.

**Mitigacion.** Dialogo claro, boton de teclas especiales, `HostService` opcional para entrada elevada/SAS, y documentacion de que UAC se atiende localmente. No se promete control total del escritorio seguro.

### 1.2 Contenido protegido

- Ventanas con DRM (Netflix, apps protegidas) pueden aparecer en negro.
- Algunas superficies de hardware y juegos exclusivos a pantalla completa no se capturan o lo hacen con artefactos.
- HDR y color amplio pueden verse lavados si se captura en SDR; la conversion correcta requiere tone mapping y se evaluara en Fase 13.

**Mitigacion.** Detectar captura negra/estancada, avisar, permitir cambiar de monitor o modo ventana. No se intenta eludir protecciones.

### 1.3 Sesiones de Windows y escritorio remoto

- Si el usuario remoto inicia sesion en otra sesion de Windows (Fast User Switching), la captura de la sesion original puede dejar de estar disponible.
- Bloqueo de pantalla, suspension e hibernacion interrumpen la captura.
- Un reinicio del equipo remoto cierra la sesion; no se puede sobrevivir sin un servicio y una politica de acceso desatendido (fuera de v1).

**Mitigacion.** Watchdog de captura, deteccion de `CaptureSession.Closed`, mensaje de reconexion y cierre limpio.

### 1.4 DPI, multi-monitor y escalado

- Los monitores pueden tener DPI, rotacion y escalado distintos. Un mapeo incorrecto hace que el puntero no coincida.
- Cambiar la resolucion o desconectar un monitor invalida el frame pool y el encoder.
- La captura devuelve pixeles fisicos; la UI usa unidades efectivas.
- La version 1 muestra **un monitor a la vez**, seleccionable y cambiable durante la sesion. El mosaico de todos los monitores a la vez queda para una fase posterior.
- El "ajuste automatico de resolucion" de la version 1 escala el frame al viewport y ajusta el bitrate; no cambia la resolucion fisica del monitor remoto. Cambiar la resolucion del anfitrion es una opcion futura y requiere confirmacion.

**Mitigacion.** Per-monitor DPI awareness V2, enumeracion con `EnumDisplayMonitors`/`GetMonitorInfo`, conversion explicita, pruebas a 100/150/200% y multi-monitor.

### 1.5 Firewall, NAT y redes corporativas

- Windows Firewall puede bloquear el puerto de escucha UDP/TCP del anfitrion. El usuario vera un aviso o la conexion directa fallara.
- NAT simetrico y CGNAT impiden el hole punching UDP; se requiere relay.
- Muchas redes bloquean UDP y permiten solo 443/TCP; el video debe caer a WSS.
- Proxies corporativos pueden romper WebSocket si no esta permitido.
- IPv6 puede estar deshabilitado o ser inconsistente.

**Mitigacion.** Relay WSS 443, deteccion de ruta, timeouts, mensajes de diagnostico y prueba de conectividad. No se puede garantizar P2P en todas las redes.

### 1.6 Codecs y hardware

- La disponibilidad de MFT H.264 hardware depende de GPU, driver y edition de Windows.
- Algunos drivers tienen bugs con low-latency, resoluciones impares o colores; hay que validar.
- El MFT software puede no estar disponible en algunas instalaciones N/KN sin Media Feature Pack.
- H.264 tiene patentes; la distribucion comercial debe revisarse legalmente.

**Mitigacion.** Enumerar MFT hardware/software, fallback, deteccion de errores, matriz de hardware y revision legal antes de distribuir.

### 1.7 WinUI 3 y Windows App SDK

- No hay plantilla `dotnet new winui` instalada en este entorno; el proyecto se crea manualmente.
- Mica solo esta disponible en Windows 11; en Windows 10 se usa Acrylic o color solido.
- No hay control de bandeja nativo; se implementa con Win32.
- Los pickers requieren inicializar con el HWND de la ventana.
- Las notificaciones de Windows App SDK requieren registro y, en algunos casos, empaquetado MSIX.
- La elevacion de una app MSIX esta limitada; UIAccess requiere firma y ubicacion de confianza.
- `Application.RequestedTheme` y los dialogos tienen arboles visuales separados; el tema debe aplicarse explicitamente a cada uno.

**Mitigacion.** Pruebas tempranas en Fase 2, fallbacks, interop documentada y matriz Windows 10/11.

### 1.8 Almacenamiento y archivos

- Nombres reservados (`CON`, `PRN`, `AUX`, `NUL`, `COM1`...), puntos/espacios finales y rutas largas pueden fallar.
- Archivos bloqueados por otro proceso no se pueden sobrescribir.
- Poco espacio en disco o cuotas de usuario interrumpen transferencias.
- Los reparse points y enlaces simbolicos pueden escapar del destino.
- No se puede garantizar que un antivirus no cuarentene un archivo durante la transferencia.

**Mitigacion.** Validacion estricta de rutas, archivo temporal, comprobacion de espacio, manejo de errores, no seguir enlaces y reintentos controlados.

### 1.9 Elevacion y servicio

- Un servicio LOCAL SYSTEM es un objetivo de alto valor. Cualquier IPC mal protegido es una vulnerabilidad grave.
- `HostService` no debe aceptar comandos arbitrarios; solo entrada de una sesion autorizada.
- La instalacion del servicio requiere administrador y debe ser visible y reversible.

**Mitigacion.** Named pipe con ACL, tickets de sesion, validacion de PID/sesion, minimo privilegio, auditoria y prueba de seguridad. Si no se puede garantizar, no se implementa.

---

## 2. Riesgos tecnicos del proyecto

| Riesgo | Probabilidad | Impacto | Mitigacion |
| --- | --- | --- | --- |
| Pipeline Media Foundation mas complejo de lo previsto | Alta | Alto | Vertical slice en Fase 7, prototipo tecnico acotado antes de la UI final, fallback software, pruebas de roundtrip |
| NAT traversal falla en demasiadas redes | Alta | Medio | Relay WSS 443 como fallback obligatorio; medir tasa de exito; documentar |
| Latencia del relay de control insuficiente | Media | Medio | Control fiable sobre UDP P2P; servidor cercano; medir; MsQuic se evalua en Fase 13 |
| Canal fiable sobre UDP con perdida/reordenacion | Media | Medio | ACK/retransmision/ventana, pruebas de caos, fallback TCP/WSS automatico |
| Handshake E2E con fallos sutiles | Media | Muy alto | Revision criptografica, vectores, tests de replay/MITM, no inventar primitivas |
| Fugas de memoria en captura/codec | Media | Alto | Perfilado, disposicion determinista, pruebas de 8 h |
| UI se congela por trabajo sincrono | Media | Medio | Analisis de hilos, async, DispatcherQueue, tests de carga |
| SQLite bloqueado o corrupto | Baja | Alto | WAL, busy timeout, backups, migraciones probadas, transacciones cortas |
| Caida del servidor | Media | Alto | Sesiones directas activas continuan; relayadas se cortan; backoff; no se crean sesiones nuevas hasta recuperar |
| Firewall/antivirus bloquea la app | Alta | Medio | Relay 443, firma, documentacion, diagnostico |
| Falta de hardware para probar | Media | Medio | Matriz minima, VMs, pruebas con usuarios; documentar lo no probado |
| Crecimiento del alcance | Alta | Alto | Fases cerradas, lista de pendientes, no prometer audio/portapapeles/desatendido |
| Dependencia de una sola persona | Media | Alto | Documentacion, pruebas, ADR, codigo claro |

---

## 3. Riesgos de seguridad y privacidad

| Riesgo | Mitigacion | Residual |
| --- | --- | --- |
| Servidor comprometido | E2E, sin contrasenas, relay opaco, pinning | Metadatos y DoS |
| Suplantacion en el primer contacto | Fingerprint visible, pinning TOFU, verificacion out-of-band opcional | Usuario acepta una huella desconocida |
| Fuerza bruta | 60 bits, PBKDF2 600k, bloqueo, rate limit | Ataque distribuido limitado |
| Replay | Nonces, contadores, ventana | Fallo de implementacion; tests |
| Path traversal | Validacion y confinamiento | Enlaces/reparse; se rechazan |
| Archivo malicioso | No auto-ejecutar, aviso, hash | Usuario lo ejecuta manualmente |
| Logs con secretos | Redaccion y pruebas | Error humano; revision |
| Acceso no autorizado | Consentimiento visible, permisos, revocacion | Ingenieria social o equipo comprometido |
| Crypto propia | Primitivas estandar, encapsulacion, revision externa | Diseno no auditado formalmente |

**Recomendacion.** Antes de un despliegue comercial, realizar una revision de seguridad independiente del handshake, del protocolo y del servicio privilegiado.

---

## 4. Riesgos legales y de cumplimiento

- El acceso remoto debe tener consentimiento informado. La app debe mostrar quien accede, que permisos y como finalizar.
- La grabacion de sesiones no esta incluida; si se anade, requiere consentimiento y politica de retencion.
- La transferencia de archivos puede transportar datos personales; se debe documentar el cifrado y la retencion.
- H.264 y otras tecnologias pueden tener patentes; revisar licencias antes de distribuir.
- La firma de codigo y la politica de privacidad son obligatorias para una distribucion profesional.
- Las leyes de ciberseguridad de cada pais pueden exigir registro de acceso y cooperacion con autoridades; no se implementa nada que obstruya la ley.

---

## 5. Lo que no se puede prometer

- Control total de la pantalla de UAC, Ctrl+Alt+Del o el escritorio de bloqueo sin privilegios especiales.
- Captura de contenido DRM protegido.
- Conexion directa en el 100% de las redes.
- Latencia de menos de 100 ms en cualquier Internet.
- Funcionamiento sin servidor si no hay ruta directa.
- Acceso desatendido en la version 1.
- Cero falsos positivos de antivirus o SmartScreen sin firma.
- Compatibilidad con todas las GPU y drivers.

Estas limitaciones se comunican en la UI y en la documentacion; no se ocultan.

---

## 6. Plan de contingencia por fase

- **Fase 2:** si WinUI 3 2.6.5 falla, validar con 1.8.260921001 y registrar el cambio; no mezclar versiones.
- **Fase 4:** si SQLite no soporta la concurrencia esperada, cambiar a SQL Server mediante el proveedor EF Core; la interfaz de repositorio no cambia.
- **Fase 6:** si el relay WSS no cumple latencia, priorizar servidores cercanos y evaluar UDP fiable.
- **Fase 7:** si un MFT hardware falla, deshabilitarlo por GPU/driver y usar software; si el software no esta, ofrecer modo de menor calidad y avisar.
- **Fase 8:** si UIPI bloquea casos criticos, implementar `HostService` con revision de seguridad o documentar la limitacion.
- **Fase 10:** si la reanudacion no es fiable, bloquear la publicacion del archivo hasta verificar el hash completo y permitir reintento.
- **Fase 12:** si hay fugas, priorizar el cierre de recursos sobre nuevas funciones.
- **Fase 14:** si la firma de codigo no esta lista, no distribuir como producto final; usar build interno.
