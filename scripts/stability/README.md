# Pruebas de estabilidad y concurrencia

Batería automatizada para ejecutar los escenarios de estabilidad de CSIViewer contra los binarios **Debug** del repositorio.

## Requisitos

- Windows 10/11.
- PowerShell 7.4 o superior (`pwsh`).
- Haber compilado antes:

```powershell
dotnet build CSIViewer.slnx -c Debug
```

Los escenarios usan los ejecutables:

```text
src\CSIViewer.Server\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CSIViewerServidor.exe
src\CSIViewer\bin\Debug\net10.0-windows10.0.19041.0\win-x64\CSIViewer.exe
tools\CSIViewer.TestHarness\bin\Debug\net10.0-windows10.0.19041.0\CSIViewer.TestHarness.exe
```

## Uso

Batería completa:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\stability\run-stability.ps1 -Profile full
```

Un escenario concreto:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\stability\run-stability.ps1 -Scenarios 06
```

Varios escenarios:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\stability\run-stability.ps1 -Scenarios 01,03,18
```

Soak más corto:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\stability\run-stability.ps1 -Scenarios 15 -SoakSeconds 60
```

Salida personalizada:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\stability\run-stability.ps1 `
  -OutputDir "$env:TEMP\csiviewer-stability"
```

La salida contiene:

```text
report.md        Informe legible con la tabla de resultados y evidencias.
results.json     Resultados estructurados.
<escenario>\     Logs del servidor, hosts, clientes y métricas STATS.
```

## Escenarios

| # | Escenario | Tipo |
|---:|---|---|
| 1 | Conectar y desconectar repetidamente | Ejecutada |
| 2 | Dos clientes accediendo al mismo host | Ejecutada |
| 3 | Múltiples solicitudes simultáneas | Ejecutada |
| 4 | Pérdida de conexión durante una sesión | Simulada (proceso suspendido) |
| 5 | Reconexión después de una caída | Simulada (servidor congelado) |
| 6 | Caída del servidor | Ejecutada |
| 7 | Reinicio del cliente | Ejecutada |
| 8 | Transferencia de archivos grandes | Ejecutada |
| 9 | Interrupción de transferencias | Ejecutada |
| 10 | Cancelación de operaciones | Ejecutada |
| 11 | Cambio de resolución | Pendiente de hardware real |
| 12 | Cambio de monitor | Ejecutada |
| 13 | Cierre de ventanas durante operaciones activas | Ejecutada |
| 14 | Cambio de tema durante una sesión | Simulada (UI real + sesión del harness) |
| 15 | Uso prolongado de la aplicación | Ejecutada |
| 16 | Errores de autenticación | Ejecutada |
| 17 | Sesiones expiradas | Ejecutada |
| 18 | Revocación de permisos durante una conexión | Ejecutada |

## Métricas

El harness imprime líneas `STATS` con:

- memoria administrada, working set y memoria privada;
- hilos, handles y cola del thread pool;
- recolectores GC.

El orquestador añade métricas del proceso mediante `Get-Process` y `Get-NetTCPConnection` / `Get-NetUDPEndpoint`:

- conexiones TCP establecidas y en escucha;
- sockets UDP;
- handles e hilos.

Las comprobaciones de fugas comparan la primera y la última muestra de cada escenario. Los umbrales están documentados en el informe:

- memoria administrada: +48 MB en ciclos, +64 MB en soak;
- hilos: +8;
- handles: +100.

## Aislamiento

- Cada escenario usa un puerto propio entre 5201 y 5218 y un directorio de datos propio.
- Al terminar cada escenario se detienen los procesos lanzados por la batería.
- La batería detiene cualquier `CSIViewer.TestHarness` que encuentre, también de ejecuciones anteriores.
- No modifica la resolución del escritorio ni la configuración del usuario.
- Los escenarios con UI usan `--data-dir` o `CSIVIEWER_SERVER_DATA_DIR` para no tocar los datos reales.

## Limitación conocida

El servidor limita a **5 solicitudes de conexión por minuto y por conexión de señalización**. El escenario 1 lo comprueba de forma explícita: 5 ciclos correctos, rechazo limpio `rate_limited` y recuperación a los 65 s.
