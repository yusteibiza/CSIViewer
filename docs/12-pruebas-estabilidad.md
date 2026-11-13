# Pruebas de estabilidad y concurrencia

Informe de la batería automatizada ejecutada sobre los binarios **Debug** de CSIViewer.

Ejecución final: **2026-11-12 21:28**, perfil `full`, 11,9 minutos.

## Resumen

- **17 escenarios PASS**, **0 FAIL**, **1 PENDING**.
- Duración de la batería completa: **11,9 minutos**.
- Escenarios ejecutados con procesos reales: servidor, hosts, controladores, transferencias y las interfaces WinUI.
- Sin fugas de memoria apreciables en los ciclos, en dos oleadas concurrentes ni en un soak de 180 s.
- Sin sockets acumulados ni tareas pendientes al finalizar.
- El único escenario pendiente es el cambio real de resolución del escritorio, que no se ejecuta para no alterar el equipo de pruebas.

## Entorno

- SO: Microsoft Windows 10.0.26300.
- .NET: 10.0.14.
- Procesadores lógicos: 16.
- Monitores detectados: 2.
  - `\\.\DISPLAY1`: 2048×1152, principal.
  - `\\.\DISPLAY2`: 1920×1080, secundario.
- Perfil: `full`.
- Batería: `scripts/stability/run-stability.ps1`.

## Método

Cada escenario se ejecuta en un puerto propio (5201–5218) y en un directorio de datos propio:

```text
<salida>\<escenario>\
  server.log / server.err.log
  host.log / host.err.log
  client-*.log / client-*.err.log
```

Las métricas se obtienen de dos fuentes:

- El harness imprime líneas `STATS` con memoria administrada, working set, memoria privada, hilos, handles, hilos del thread pool y trabajos pendientes.
- El orquestador consulta el proceso con `Get-Process` y los sockets con `Get-NetTCPConnection` y `Get-NetUDPEndpoint`.

Clasificación usada en el informe:

- **Ejecutada:** el escenario se ejecutó con procesos y red reales.
- **Simulada:** se provocó una condición equivalente (suspender un proceso, congelar el servidor) sin cambiar hardware ni red física.
- **Pendiente:** requiere un equipo o una intervención que no se realizó.

Reproducción:

```powershell
dotnet build CSIViewer.slnx -c Debug
pwsh -NoProfile -ExecutionPolicy Bypass `
  -File scripts\stability\run-stability.ps1 -Profile full -SoakSeconds 180
```

## Resultados

| # | Escenario | Tipo | Resultado | Evidencia |
|---:|---|---|---|---|
| 1 | Conectar y desconectar repetidamente | Ejecutada | PASS | 7/7 ciclos; límite de 5/min rechazado limpiamente y recuperado a los 65 s; memoria +0,5 MB; hilos 26→22; handles 540→534; TCP 4; tareas pendientes 0 |
| 2 | Dos clientes accediendo al mismo host | Ejecutada | PASS | 2 sesiones simultáneas aceptadas, iniciadas y finalizadas |
| 3 | Múltiples solicitudes simultáneas | Ejecutada | PASS | 2 oleadas de 11: 20 aceptadas y 2 rechazadas por `host_full`; working set 131,4→142,6 MB; handles 590→597 |
| 4 | Pérdida de conexión durante una sesión | Simulada | PASS | Cliente suspendido 35 s; el host cerró la sesión (`active=0`); servidor estable |
| 5 | Reconexión después de una caída | Simulada | PASS | Servidor congelado 25 s; enlace recuperado y nueva sesión establecida |
| 6 | Caída del servidor | Ejecutada | PASS | Servidor detenido 6 s y reiniciado; clientes reconectados; health OK |
| 7 | Reinicio del cliente | Ejecutada | PASS | Cliente terminado, sesión liberada y reconexión correcta con la misma identidad |
| 8 | Transferencia de archivos grandes | Ejecutada | PASS | 64 MiB completados en 759 ms (84 MB/s); tamaño final verificado |
| 9 | Interrupción de transferencias | Ejecutada | PASS | Reanudada desde 2.097.152 bytes; estado `Interrupted` y después `Completed`; archivo final de 8 MiB |
| 10 | Cancelación de operaciones | Ejecutada | PASS | 32 MiB cancelados en 2 MiB; sin archivo final; proceso terminado |
| 11 | Cambio de resolución | Pendiente | PENDING | Requiere cambiar la resolución real del escritorio; no se modificó el equipo |
| 12 | Cambio de monitor | Ejecutada | PASS | 2 monitores; cambio 0→1 con vídeo activo tras el cambio (2048×1152 y 1920×1080) |
| 13 | Cierre de ventanas durante operaciones activas | Ejecutada | PASS | Ventana del servidor cerrada; proceso vivo, health OK y sesión remota intacta |
| 14 | Cambio de tema durante una sesión | Simulada | PASS | Tema cambiado 3 veces en la UI real con una sesión remota activa |
| 15 | Uso prolongado de la aplicación | Ejecutada | PASS | 180 s; 1320 fotogramas; memoria −1 MB; hilos −30; handles −119; TCP 4; tareas pendientes 0 |
| 16 | Errores de autenticación | Ejecutada | PASS | 5 fallos controlados; bloqueo activado; autenticación correcta con identidad nueva |
| 17 | Sesiones expiradas | Ejecutada | PASS | Contraseña temporal de 1 min rechazada tras 65 s; sin sesión creada |
| 18 | Revocación de permisos durante una conexión | Ejecutada | PASS | Sesión revocada; contraseña anterior rechazada; contraseña nueva aceptada |

## Memoria, sockets y tareas

### Ciclos de conexión

En 7 ciclos completos:

- memoria administrada: **+0,5 MB**;
- hilos: **26 → 22**;
- handles: **540 → 534**;
- sockets TCP abiertos al final: **4**;
- trabajos pendientes del thread pool: **0**.

### Concurrencia

Dos oleadas consecutivas de 11 solicitudes, con 10 sesiones simultáneas por oleada:

- working set del servidor: **131,4 MB → 142,6 MB**;
- handles: **590 → 597**;
- 20 sesiones aceptadas y 2 rechazos limpios `host_full`.

La segunda oleada no añadió memoria ni handles de forma acumulativa: el servidor reutiliza sus buffers.

### Soak de 180 s

- 1920 fotogramas recibidos;
- memoria administrada: **−1 MB**;
- hilos: **−30**;
- handles: **−119**;
- sockets TCP: **4**;
- trabajos pendientes del thread pool: **1** (sin acumulación).

### Procesos y sockets al final

Al terminar la batería no quedaron procesos `CSIViewerServidor`, `CSIViewer`, `CSIViewerHost` ni `CSIViewer.TestHarness`, ni puertos de prueba en escucha. La comprobación se hizo con `Get-Process` y `Get-NetTCPConnection`.

## Hallazgos

### 1. Límite de 5 solicitudes de conexión por minuto

El servidor rechaza la sexta solicitud de conexión realizada sobre la misma conexión de señalización dentro de un minuto:

```text
rate_limited: Demasiadas solicitudes de conexión. Espera un minuto.
```

Es una protección deliberada. La prueba verifica que:

- las 5 primeras conexiones funcionan;
- el rechazo es limpio, sin excepción ni bloqueo del cliente;
- tras 65 s la misma conexión vuelve a conectar.

**No se modificó** el límite: cambiarlo reduciría la protección antiabuso. Queda documentado como límite de diseño.

### 2. Captura WGC en escritorio estático

Windows.Graphics.Capture entrega fotogramas cuando cambia el contenido. En un escritorio estático, el harness puede recibir muy pocos fotogramas y su temporizador de vídeo de 20 s termina la sesión de prueba. No es un fallo del producto: la UI conserva el último fotograma y la sesión sigue viva por heartbeat.

Para el soak se fuerza la captura GDI, que produce fotogramas de forma continua y permite medir el pipeline durante 180 s.

### 3. Sin fugas detectadas

Los umbrales usados fueron:

- memoria administrada: +48 MB en ciclos y +64 MB en soak;
- hilos: +8;
- handles: +100.

Ningún escenario los superó. En concurrencia, la segunda oleada tampoco acumuló memoria.

## Correcciones realizadas

### Desplegable de nivel de logs

El `ComboBox` original dibujaba su popup hacia abajo y quedaba recortado por la barra de tareas cuando el control estaba cerca del borde inferior.

Se sustituyó por un `DropDownButton` con `Flyout` colocado arriba y elementos propios:

- celda azul vertical de 3×18 px para el elemento seleccionado;
- filas de 36 px con 8 px de separación;
- ancho de 300 px y altura mínima de 40 px, iguales para el botón y el panel;
- padding horizontal de 12 px y truncado con elipsis;
- apertura hacia arriba para no salir de la pantalla;
- foco inicial en el nivel seleccionado.

Aplicado en cliente y servidor.

### Infraestructura de pruebas

Se añadieron al harness:

- modo `cycle` con recuperación tras `rate_limited`;
- modo `reconnect` para medir caída y recuperación;
- modo `monitor` para enumerar y cambiar monitores;
- modo `stats` y muestras periódicas con memoria, hilos, handles y thread pool.

Se añadió la batería `scripts/stability/` con informe automático en Markdown y JSON.

## Pendiente de equipos reales

1. **Cambio real de resolución del escritorio** (escenario 11). No se ejecutó para no alterar la configuración del equipo. El cambio de monitor entre 2048×1152 y 1920×1080 sí se ejecutó y valida el cambio de resolución de captura.
2. **Pérdida de red física** (cable/Wi-Fi). Los escenarios 4 y 5 se simularon suspendiendo el cliente y congelando el servidor; no se desconectó la red real.
3. **Sesión controlada desde la UI del cliente para el cambio de tema.** El escenario 14 cambia el tema en la UI real mientras hay una sesión del harness activa; la sesión no pertenece a la UI.
4. **Servicio `CSIViewerHost` con UAC real**, escritorio seguro y ventanas elevadas, fuera del alcance de esta batería.
5. **Soak superior a 10 minutos y más de 100 ciclos**, opcional para una validación de larga duración.

## Artefactos

La ejecución deja:

```text
report.md
results.json
<escenario>\*.log
```

La ruta usada en esta ejecución está en el informe generado por el script. El procedimiento y los escenarios están documentados en `scripts/stability/README.md`.
