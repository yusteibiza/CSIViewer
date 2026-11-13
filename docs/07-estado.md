# Estado del proyecto, funcionalidades y verificación

Actualizado: 2026-11-12
Estado: **build de desarrollo funcional; no listo para producción**

---

## 1. Resumen

La columna vertebral está implementada y probada de extremo a extremo:

- La aplicación WinUI 3 compila y arranca.
- Dos clientes se registran, autentican y conectan a través del servidor.
- El consentimiento y la contraseña temporal funcionan.
- El chat cifrado llega al otro extremo.
- El vídeo se captura con Windows.Graphics.Capture (fallback GDI), se codifica en H.264 con Media Foundation (fallback JPEG), se transmite directo en LAN o por relay y se renderiza en la UI.
- La transferencia de archivos funciona con SHA-256, bloques, reanudación y verificación; probada con archivos de 8 y 16 MiB, reconexión, pausa y cancelación.
- La UI se verificó con UI Automation: sesión activa y estadísticas de vídeo (~11,8 FPS en la prueba).

Quedan pendientes piezas importantes para considerarlo producto final: transporte P2P/TURN para Internet, firma del MSIX, HostService/UIPI/SAS, localización y pruebas de integración automatizadas completas.

---

## 2. Fases

| Fase | Estado | Evidencia |
| --- | --- | --- |
| 1. Arquitectura | Completada | Documentos y ADR |
| 2. WinUI, navegación y temas | Completada | App compila y arranca; navegación y tema implementados |
| 3. Identidad y contraseña | Completada | ECDSA/DPAPI, registro, pantalla de Inicio |
| 4. Servidor central | Completada (v1) | Registro, auth, presencia, WebSocket, SQLite, relay |
| 5. Seguridad E2E y consentimiento | Completada (v1) | ECDH/AES-GCM, PBKDF2, permisos, bloqueo |
| 6. Conexión entre equipos | Completada (LAN + relay) | TCP directo LAN y fallback relay |
| 7. Vídeo | Completada (v1) | Windows.Graphics.Capture + H.264 Media Foundation, fallback JPEG y GDI |
| 8. Entrada | Implementada, cobertura parcial | `SendInput`; falta prueba UIPI/SAS |
| 9. Chat | Completada | Harness y persistencia SQLite |
| 10. Archivos | Completada (v1) | Bloques, SHA-256, concurrencia controlada, progreso total, pausa/cancelación, reanudación automática, carpetas vacías y control de espacio |
| 11. Dispositivos/contactos/historial | Completada (v1) | SQLite y UI |
| 12. Resiliencia/configuración | Parcial | Timeouts, reconexión, logs, settings; faltan más pruebas de caos |
| 13. Optimización/pruebas | Parcial | 17 pruebas unitarias; falta matriz de hardware |
| 14. Distribución | Parcial | MSIX de prueba generado; falta firma, instalador del servidor y actualizador |

---

## 3. Funcionalidades verificadas

| Área | Estado | Verificación |
| --- | --- | --- |
| WinUI 3, Mica, navegación, 9 secciones | Verificado | La app arranca y navega |
| UI responsive | Verificado | 1920×1010, 1280×800 y 1024×768 sin recortes horizontales |
| Barra de título | Verificado | Controles del sistema visibles y separados de la barra de estado |
| Tema claro/oscuro/sistema | Verificado | Cambio en caliente, persistencia y superficies neutras de Windows 11 |
| Marca e iconos | Verificado | Icono CSIViewer en ventanas, bandejas y ejecutables; nombres CSIViewer/Servidor/Host |
| Bandeja del servidor | Verificado | Cerrar la ventana oculta CSIViewer Servidor y mantiene el servidor activo |
| UPnP | Implementado | Abre/cierra el puerto TCP en el router; si no hay UPnP, avisa y el servidor sigue en local |
| Modo desatendido | Implementado | Servicio CSIViewerHost como LocalSystem; enumera y lanza `CSIViewer.exe --host-mode --unattended` en cada sesión activa |
| ID de dispositivo y registro | Verificado | ID de 9 dígitos en `identity.json` y servidor |
| Contraseña temporal | Verificado | Generación aleatoria o personalizada; PBKDF2 y persistencia opcional con DPAPI |
| Autenticación por desafío ECDSA | Verificado | Harness y app registrada |
| Presencia online | Verificado | UI muestra "En línea" y snapshot del servidor |
| Conexión y consentimiento | Verificado | Harness host/controller y UI Automation |
| Conexión LAN directa | Verificado | `Direct LAN session transport established`; fallback relay probado |
| Cifrado E2E | Verificado | Chat, vídeo y archivos cruzan el relay cifrados |
| Chat | Verificado | `HOST_CHAT` y `CONTROLLER_OK` |
| Vídeo | Verificado | `VIDEO_OK`; Windows.Graphics.Capture activo; UI Automation mostró FPS |
| H.264 | Verificado | Captura directa BGRA→H.264 sin JPEG intermedio; codificador/decodificador Media Foundation y `VIDEO_OK` |
| FPS altos | Verificado | Presets 50/60/90/120; codificación y decodificación a 120 FPS; descarte de frames para evitar acumulación |
| Selección de monitor | Implementado | Host envía lista; controlador solicita cambio |
| Ratón/teclado | Implementado | `SendInput`; pendiente prueba de aceptación UIPI |
| Transferencia de archivos | Verificado | 8 MiB reales con reanudación, pausa, cancelación y carpetas; 16 MiB en pruebas unitarias |
| Pausa/reanudación persistente | Verificado | Parcial de 8 MiB reutilizado tras reiniciar el anfitrión |
| Cancelación | Implementado | `file.cancel` y limpieza de temporal |
| Dispositivos/favoritos | Verificado | Alta, edición y borrado manual; SQLite y UI |
| Listas personalizadas | Verificado | Crear, renombrar, eliminar y filtrar listas como Clientes o Casa; migración de grupos antiguos |
| Historial | Verificado | SQLite y UI de búsqueda |
| Contactos | Verificado | Contactos con dispositivos enlazados y conexión directa desde el contacto |
| Configuración | Verificado | Persistencia JSON |
| Servidor con interfaz | Verificado | Sin argumentos o `--modo=ui`; tema sistema/claro/oscuro; configuración, inicio y parada; health check |
| Servidor sin interfaz | Verificado | `--modo=servidor`; reutiliza la configuración guardada y admite parámetros CLI |
| Servidor como servicio | Verificado | `--modo=servicio`, integración con el Administrador de servicios y datos en `data\` o `%ProgramData%` si la carpeta no es escribible |
| HTTPS/WSS | Verificado | Certificado por huella en CurrentUser/LocalMachine; health HTTPS probado |
| Servidor STUN | Verificado | Binding UDP en 3478; `STUN_OK` contra CSIViewer y UPnP UDP opcional |
| Inicio con Windows | Implementado | Registro HKCU Run para cliente y servidor; el servidor usa `--modo=ui` y el servicio CSIViewerHost es Automático |
| Logs JSONL | Verificado | Archivo en `data\logs` junto al ejecutable |
| Notificaciones | Implementado | AppNotification; depende de registro del sistema |
| Pruebas unitarias | Verificado | 154/154 superadas |
| Estabilidad y concurrencia | Verificado | 17 PASS, 0 FAIL, 1 pendiente de hardware; soak de 180 s; sin fugas de memoria, sockets ni tareas |
| STUN diagnóstico | Verificado | `STUN_OK 88.22.10.230:50566` |

---

## 4. Pendiente prioritario

### 4.1 Vídeo profesional

- Validar aceleración hardware del codificador H.264 en la matriz de GPU; actualmente se usa el MFT de Media Foundation con fallback JPEG.
- Pipeline directo BGRA→H.264 implementado (sin JPEG intermedio). Pendiente la conversión/render NV12 en GPU y `SwapChainPanel`.
- Renderizado NV12 en `SwapChainPanel` sin conversión a JPEG.
- Ajuste adaptativo de bitrate y FPS por pérdida/RTT.
- Pruebas de matriz de GPU y HDR.

### 4.2 Conectividad P2P

- STUN cliente/servidor integrado; pendiente el hole punching ICE completo.
- Hole punching UDP y candidatos ICE simplificados.
- Relay UDP y WSS; elección de ruta visible.
- Canal fiable ligero sobre UDP para control/chat.
- Pruebas en NAT simétrico, CGNAT, firewalls corporativos y IPv6.

### 4.3 Archivos

- Transferencias por bloques de 1 MiB con hasta 3 archivos simultáneos y progreso individual/total.
- Reanudación desde el último byte confirmado tras desconexión o reinicio, con reanudación automática al reconectar.
- Pausa/reanudación en emisor y receptor mediante mensajes de control.
- Política de conflictos implementada: renombrar, sobrescribir u omitir, con confirmación explícita de sobrescritura.
- Verificación SHA-256 antes de mover el archivo temporal al destino final.
- Comprobación de espacio libre y errores de permisos con mensajes claros.
- Transferencia de carpetas, incluidas carpetas vacías.

### 4.4 Distribución y operación

- Firma de MSIX con certificado de desarrollo implementada; pendiente certificado de producción y actualizaciones.
- Servicio de Windows y HTTPS/WSS del servidor implementados; pendiente certificado público de producción.
- Instalador/paquete del servidor y guía de firewall.
- Instancia única e icono de bandeja implementados; falta un indicador global siempre visible si la ventana está cerrada.
- Localización es/en.
- Revisión de seguridad externa.

### 4.5 Privilegios

- UIPI/SAS y escritorio seguro requieren UIAccess firmado; no se implementa un bypass inseguro.
- Documentado: UAC, Ctrl+Alt+Del y la pantalla de bloqueo no se capturan.

---

## 5. Entorno verificado

| Comprobación | Resultado |
| --- | --- |
| SDK | .NET 10.0.404 |
| Windows App SDK | 2.6.5 |
| WinUI 3 + .NET 10 | Compila y arranca |
| Solución | `CSIViewer.slnx`, 13 proyectos |
| Cliente | Arranca y se registra (ID 980372190 en la prueba) |
| Servidor | `/api/health` OK; modo consola y modo UI verificados |
| Chat E2E | OK |
| Vídeo E2E | OK (`VIDEO_OK`) |
| Archivo 2 MiB E2E | OK (`FILE_OK`, SHA-256 verificado) |
| UI Automation | Sesión activa y estadísticas de vídeo |
| Pruebas unitarias | 154/154 |
| Servicio desatendido | CSIViewer.Host compila y arranca; la instalación real requiere administrador/LocalSystem |
| Logs | JSONL generado |
| Crash log | Sin errores tras el arranque |
| MSIX de prueba | Generado (`AppPackages/...msix`) sin firmar |
| Instancia única | Verificada: la segunda instancia sale |
| Bandeja del sistema | Inicializada con menú Abrir/Finalizar/Salir |

---

## 6. Decisiones por defecto aplicadas

- Nombre provisional: **CSIViewer**.
- Windows 10 2004+ y Windows 11.
- Servidor autohospedado con código de inscripción `DEV-1234`.
- Vídeo Windows.Graphics.Capture + H.264 Media Foundation con fallback JPEG/GDI.
- LAN directa por TCP; Internet por relay; P2P/TURN queda como siguiente hito.
- Acceso desatendido, audio y portapapeles fuera de v1.
- `HostService` no incluido todavía.
- Interfaz en español.

---

## 7. Cómo comprobar el estado

```powershell
dotnet build CSIViewer.slnxx -c Debug
dotnet test tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj -c Debug
```

Pruebas manuales:

1. Arranca el servidor.
2. Arranca un cliente y anota ID/contraseña.
3. Arranca otro cliente con `CSIVIEWER_DATA_DIR` distinto.
4. Conecta, acepta el consentimiento y comprueba vídeo, chat y archivos.
5. Cierra la sesión y revisa historial, dispositivos y logs.

Ver [guía de ejecución](08-guia-ejecucion.md).

---

## 8. Historial de cambios

| Fecha | Cambio |
| --- | --- |
| 2026-11-12 | Fase 1: arquitectura y plan |
| 2026-11-12 | Solución WinUI 3, UI, temas, identidad, servidor, E2E, conexión, vídeo JPEG, entrada, chat, archivos, agenda, historial, contactos, ajustes y pruebas |
| 2026-11-12 | Revisión visual: contenido responsive, barra de título corregida, tema oscuro neutro, radios de tarjeta reducidos y captura Windows.Graphics.Capture con fallback GDI |
