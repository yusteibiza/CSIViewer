# Registro de eventos y diagnóstico

## Formato

Los logs se guardan en formato JSON Lines, una línea por evento:

```json
{
  "ts": "2026-11-12T18:22:42.5352794+00:00",
  "level": "Information",
  "category": "CSIViewer.Services.RemoteClient",
  "eventId": 2100,
  "event": "AuthenticationSucceeded",
  "message": "Authentication succeeded for device 325697649, session 1019f3ed-...",
  "properties": { "DeviceId": "325697649", "SessionId": "1019f3ed-..." },
  "scopes": { "SessionId": "1019f3ed-...", "RemoteDeviceId": "325697649", "Role": "Host" },
  "exceptionType": null,
  "exceptionMessage": null,
  "exception": null,
  "pid": 5892,
  "tid": 8
}
```

## Niveles

- `Debug`
- `Information`
- `Warning`
- `Error`
- `Critical`

Se configuran desde:

- Cliente: **Configuración → Diagnóstico de red → Nivel de detalle de logs**.
- Servidor: interfaz del servidor o `--log-level=Debug|Information|Warning|Error|Critical`.
- El cambio se aplica sin reiniciar el proceso.

## Eventos registrados

| ID | Evento |
|---:|---|
| 1000 | `AppStarted` |
| 1001 | `AppStopping` |
| 1002 | `DiagnosticsExported` |
| 2000 | `ConnectionOpening` |
| 2001 | `ConnectionOpened` |
| 2002 | `ConnectionClosed` |
| 2003 | `ConnectionStateChanged` |
| 2010 | `ReconnectAttempt` |
| 2011 | `ReconnectSucceeded` |
| 2012 | `ReconnectFailed` |
| 2100 | `AuthenticationSucceeded` |
| 2101 | `AuthenticationFailed` |
| 2102 | `AuthenticationRejected` |
| 2103 | `AuthenticationRateLimited` |
| 2200 | `SessionStarted` |
| 2201 | `SessionEnded` |
| 2300 | `TransferStarted` |
| 2301 | `TransferCompleted` |
| 2302 | `TransferFailed` |
| 2303 | `TransferInterrupted` |
| 2304 | `TransferResumed` |
| 9000 | `UnhandledException` |
| 9001 | `BackgroundTaskFailed` |

## Correlación por sesión

Las operaciones de sesión abren un scope estructurado con:

- `SessionId`
- `RemoteDeviceId`
- `RemoteName`
- `Role` (`Host` o `Controller`)

Las transferencias añaden:

- `TransferId`
- `FileName`
- `SessionId`

Así se puede reconstruir todo lo ocurrido en una sesión concreta aunque haya varias sesiones simultáneas.

## Rotación y límites

- Archivo actual: `csiviewer-YYYYMMDD.log` en cliente y `csiviewer-servidor-YYYYMMDD.log` en servidor, dentro de `data\logs` junto al ejecutable.
- Rotación al superar **10 MiB**.
- Límite total aproximado: **200 MiB**.
- Retención: **30 días**.
- La limpieza se ejecuta al arrancar, al rotar y periódicamente durante la ejecución.

## Protección de datos

- Se redactan contraseñas, tokens, secretos, cabeceras `Authorization`/`Bearer`, códigos de inscripción, nonces, claves de sesión, credenciales embebidas en URLs y claves privadas.
- No se registran cuerpos de archivos ni contenido de pantalla.
- Los logs de diagnóstico exportados **no incluyen** `settings.json`, identidad, contraseñas, tokens, códigos de inscripción ni claves privadas.
- La exportación solo incluye los logs estructurados actuales; los logs legacy no se incluyen.

## Tolerancia a fallos

Todas las operaciones del logger están protegidas:

- Un fallo de disco, serialización o rotación no se propaga.
- Tras varios fallos consecutivos, el proveedor deja de escribir durante un minuto y la aplicación continúa.
- Los handlers de excepciones no capturadas escriben tanto en el log estructurado como en el crash log de emergencia.

## Herramientas de diagnóstico

Cliente y servidor incluyen:

- **Abrir carpeta de logs**.
- **Exportar diagnóstico**: genera un ZIP con `diagnostics.txt` y los logs estructurados.
- **Probar servidor** y **Probar STUN** en el cliente.
- El ZIP se abre automáticamente en el Explorador.

El resumen de diagnóstico incluye versión, sistema operativo, arquitectura, procesadores, estado de conexión, sesiones activas, configuración de vídeo/logs y último error redactado.

## Verificación

- Logs JSON reales generados en cliente, servidor y harness.
- Sesiones con `scopes.SessionId`.
- Autenticación, conexión, desconexión, reanudación y finalización de transferencia verificadas.
- Exportación ZIP de cliente y servidor verificada.
- Redacción de secretos cubierta por pruebas unitarias.
- Rotación y límite de almacenamiento cubiertos por pruebas unitarias.
- **154/154 pruebas unitarias correctas.**
- Compilación Debug y Release: **0 avisos, 0 errores.**
