# Rendimiento y estabilidad

Fecha de medición: 12/11/2026  
Configuración: Release, Windows, servidor local, host y cliente headless, 16 procesadores lógicos.

## Metodología

Se midió con el harness de pruebas (`tools/CSIViewer.TestHarness`) para aislar el coste de red, captura, códec y transferencia. Las pruebas de UI se hicieron con la aplicación WinUI real. Las cifras de CPU incluyen el arranque de .NET, EF Core y la inicialización de la sesión, por lo que son cotas superiores del coste de la operación concreta.

## Cambios aplicados

### 1. Conversión BGRA → NV12 sin buffers intermedios

Antes se creaban tres planos (`Y`, `U`, `V`) y luego se copiaban a un cuarto buffer NV12. Ahora la conversión escribe directamente en el buffer nativo bloqueado de Media Foundation.

Medición aislada, 1280×720, 40 frames:

| Versión | Tiempo | Memoria asignada | Por frame |
|---|---:|---:|---:|
| Anterior | 77,8 ms | 106,2 MB | 2,66 MB |
| Nueva | 52,4 ms | 0,4 MB | 0,01 MB |

Resultado: **1,49× más rápida** y **99,6 % menos asignaciones** en esta conversión.

### 2. Salida del codificador H.264 sin copia adicional

Se eliminó el `MemoryStream` intermedio. En el caso habitual de una sola salida se devuelve directamente el buffer producido; solo se concatena cuando el MFT entrega varios bloques.

### 3. Transferencia de archivos sin copia por bloque

Antes, cada bloque de 1 MiB se copiaba con `data.ToArray()` antes de escribirlo. Ahora se escribe directamente la memoria del payload recibido.

Medición de la copia eliminada, 300 bloques de 1 MiB:

```text
300 MB asignados en 48,4 ms
```

Esa asignación desaparece del camino de recepción.

### 4. Ensamblado WebSocket con buffer alquilado

Antes se usaba `MemoryStream` + `ToArray()` por mensaje. Ahora se usa `ArrayPool<byte>` y una única copia al buffer exacto. Se añadieron pruebas para mensajes pequeños, mensajes fragmentados de 1 MiB y cierre de conexión.

### 5. UI sin trabajo innecesario

- La página de control remoto deja de procesar frames cuando no está visible.
- La página de transferencias agrupa las actualizaciones de UI a 4 Hz en lugar de refrescar por cada evento.
- Se coalescen las actualizaciones de estado del cliente, Inicio e Historial.
- El refresco de la lista de transferencias solo se hace si la página está cargada.
- `DispatcherQueue` se usa solo para trabajo de UI; la red, el hash, la captura y la codificación siguen en segundo plano.

### 6. Registro del servidor

Se configuró el servidor para filtrar `Microsoft.EntityFrameworkCore` y `Microsoft.AspNetCore` a `Warning`. Antes, si faltaban los `appsettings` en la salida, el servidor registraba cada consulta SQL en `Information`, generando E/S y CPU innecesarios.

### 7. Condiciones de carrera en transferencias

- Se evita que el mismo archivo se envíe dos veces en paralelo (por ejemplo, reanudación automática y envío manual simultáneos).
- La recepción rechaza con `busy` un segundo intento sobre el mismo archivo temporal mientras el primero sigue abierto.
- La clave temporal se libera de forma determinista al completar, abortar o liberar la transferencia.

## Medición de transferencia de archivos

Prueba real: 64 MiB por el servidor relay local.

| Métrica | Resultado |
|---|---:|
| Tiempo de `SendFilesAsync` | 780 ms |
| Throughput | 82,0 MB/s |
| CPU cliente | 2,84 s |
| CPU host | 3,34 s |
| CPU servidor | 2,94 s |
| Working set host | 146,9 MB |

Medición anterior con el mismo tamaño: 786 ms, 81,4 MB/s, CPU 3,20/3,52/3,30 s. La mejora es pequeña pero consistente; la ganancia principal es la eliminación de 64 MB de asignaciones temporales en la recepción.

## Memoria en vídeo

Muestreo cada 5 s durante 30 s con host y un visor:

| Momento | Host WS | Cliente WS | Servidor WS |
|---|---:|---:|---:|
| 5 s | 382,6 MB | 118,3 MB | 122,2 MB |
| 10 s | 360,8 MB | 119,1 MB | 124,7 MB |
| 15 s | 401,2 MB | 119,3 MB | 121,6 MB |
| 20 s | 424,2 MB | 127,4 MB | 119,3 MB |
| 25 s | 423,5 MB | 135,3 MB | 129,4 MB |
| 30 s | 420,7 MB | 147,9 MB | 119,1 MB |

El host se estabiliza alrededor de 420 MB después del calentamiento; no se observa crecimiento monotónico continuo. La versión previa arrancaba en ~424 MB y subía hasta ~438 MB en la misma ventana.

La CPU de vídeo no se usa como comparación directa porque la captura WGC depende de cuánto cambie la pantalla y el número de frames procesados varió entre ejecuciones. La comparación fiable de la conversión es la medición aislada anterior.

## Pruebas de estabilidad

- Reanudación tras matar el cliente: `RESUMED_OFFSET=2097152`, `HOST_COMPLETED=True`, `FINAL_SIZE=8388608`.
- Transferencia de 64 MiB: `FILE_BENCH mbps=82,0`, `HOST_COMPLETED=True`.
- Vídeo H.264 real: frames JPEG iniciales y BGRA después de `video.config`, sin errores.
- Pruebas unitarias: **143/143**.
- Compilación Debug y Release: **0 avisos, 0 errores**.

## Pendiente de medir en hardware real

- GPU: la captura usa Windows.Graphics.Capture y Media Foundation; no se hizo un benchmark de aceleración hardware específico.
- Latencia percibida de ratón/teclado: no se dispone de un medidor end-to-end en este entorno.
- Ancho de banda WAN real: las pruebas fueron en localhost/LAN.
- Consumo en equipos de gama baja y con monitores 4K/alta tasa de refresco.
