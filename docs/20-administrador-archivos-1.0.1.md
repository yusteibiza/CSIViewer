# Administrador de archivos — 1.0.1

> Los paquetes y hashes de esta primera entrega fueron sustituidos por la revisión
> de contraseñas del informe `21-contrasenas-dispositivos-servidor-1.0.1.md`, que
> también incorpora el servidor y documenta la eliminación solicitada de los EXE 1.0.0.

## Instaladores generados

En `artifacts/installers/`, sin sustituir los de 1.0.0:

| Archivo | Fecha local (13/11/2026) | SHA-256 |
|---|---|---|
| `CSIViewer-Setup-1.0.1.exe` | 19:16:04 | `979DFBCD3F6E2F4348F9941BA116594B418F4201657163A903D1E389C4F9488F` |
| `CSIViewer.Host-Setup-1.0.1.exe` | 19:17:17 | `03CA932F2C8FFD9784DD94092ADE27AA83B6357A672C653298B5A397A98DA0FE` |

Versiones Win32 de instaladores, visor, host y agente verificadas: 1.0.1.0.
Los ocho ensamblados compartidos de ambos payloads coinciden por SHA-256 y fueron
los usados en la última prueba de 21 casos. El hash del ZIP de seguridad 1.0.0 y
los hashes de sus dos instaladores se comprobaron sin cambios al terminar.
No se actualizaron automáticamente las aplicaciones instaladas.

## Uso

1. Actualizar visor y host a 1.0.1; el servidor existente sigue siendo compatible.
2. Conectar con los permisos de archivos necesarios y abrir **Transferencia de archivos**.
3. A la izquierda navegar por **Este equipo** y a la derecha por **Equipo remoto**.
   Unidades, Subir, Ir, Actualizar y la ruta editable permiten navegar; doble clic
   solo abre carpetas, nunca ejecuta archivos. Selección múltiple con Ctrl/Mayús.
4. Elegir una carpeta de destino real en el panel opuesto, seleccionar archivos o
   carpetas y pulsar **Enviar →** o **← Recibir**.
5. El resumen permanece abajo. **Opciones y progreso de transferencias** despliega
   los selectores anteriores, progreso por archivo, pausa/reanudación/cancelación
   y las opciones de limpieza ya existentes en cada producto.

Los paneles se actualizan al completar transferencias; también existe Actualizar.
La carpeta interna `.rdpart` no aparece en el listado y se excluye al enviar el
contenido de otra carpeta. No se añadió borrado, renombrado ni ejecución remota.

## Diseño y permisos

- Reutiliza RemoteClient.Files, cifrado de sesión, bloques, IncomingFileTransfer,
  SHA-256, consentimiento del receptor y política de conflictos existentes.
- Nuevos controles cifrados `file.browse`, `file.pull` y sus respuestas, confirmación
  de carpetas y aviso de finalización del trabajo solicitado. El servidor no
  interpreta estos contenidos y no requiere actualización.
- El manejo del controlador recibe ahora la sesión que descifró cada mensaje de
  archivos, igual que el host: no utiliza la sesión primaria para resolver destinos
  o respuestas de otro cliente.
- Cada carpeta de destino se representa por un identificador aleatorio, limitado
  a la sesión autenticada que lo emitió. Se invalida al desconectar. Un remitente
  no puede indicar una ruta absoluta arbitraria como destino de una oferta.
- Con permiso solo de envío, el cliente ve carpetas de destino, pero no nombres
  ni tamaños de archivos que no tenga permiso de recibir. Con solo recepción no
  se emiten identificadores para escribir en el equipo remoto.
- Tras confirmar un diálogo se vuelve a comprobar estado, permiso y destino.
  Los envíos y bloques recibidos también comprueban permisos durante el trabajo.
- La navegación no eleva privilegios ni sigue rutas UNC, unidades de red,
  enlaces/junctions o puntos de reanálisis. Solo accede como el usuario del proceso.
- Lectura de carpetas y preparación de envíos fuera del hilo UI/bucle de recepción.
  Las subidas solicitadas no bloquean nuevas navegaciones. Si hay un trabajo
  equivalente ocupado, se devuelve un error, no una segunda ejecución paralela.
- Paginación de 200 elementos por respuesta; orden de carpetas/nombres dentro de
  cada página. Máximo 100 elementos seleccionados para una solicitud de recepción,
  10.000 archivos/carpetas por preparación y 256 destinos distintos por sesión.

## Correcciones necesarias en la transferencia reutilizada

El emisor espera ahora la confirmación final de integridad del receptor antes de
marcar un archivo como completado. Se conserva el envío en el diccionario hasta
esa confirmación y se vincula el resultado a su sesión. Esto evita que un archivo
vacío reciba el resultado antes de que el emisor pase a Verificando y quede ahí.
Un rechazo/error parcial ya no se presenta como éxito con cero archivos.
Las carpetas del administrador también se confirman, incluidas las vacías.

La solicitud de recepción tiene acuse inicial y un aviso final que comunica éxito
o errores del remitente (incluso fallos anteriores a una oferta de archivo).

## Persistencia y reanudación

Migración aditiva `Transfers.BrowserDestination`, conservando registros antiguos.
Contiene referencia de sesión y destino del envío, no credenciales. Se comprueba
si la columna existe antes de ALTER TABLE y la actualización es idempotente.

Pausa/reanudación dentro de una sesión siguen disponibles. Tras desconectar, los
identificadores de destino no se reutilizan automáticamente: seleccionar de nuevo
archivos y carpeta autoriza el nuevo destino. La infraestructura de parciales
puede recuperar bytes compatibles en esa carpeta. No se envía por defecto a
Descargas ni a otro equipo al reanudar un trabajo del administrador.

## Visor y host independientes

Ambas páginas XAML incorporan los paneles. Conservan sus controles históricos
propios. `FileTransferPage.Browser.cs` se compila también en el host mediante Link
y `CSIVIEWER_HOST`; no hay dependencia de ejecución del host sobre CSIViewer.exe.
El agente de arranque sigue separado, basado en la tarea SYSTEM CSIViewerHost.

## Pruebas

- Solución Debug y Release: cero errores/advertencias; **183/183** tests en ambas.
- Nuevos tests de rutas, listado, paginación, compatibilidad de ofertas antiguas,
  destinos ligados a sesión, invalidación al cerrar, y migración SQLite preservando
  registros antiguos y persistiendo el contexto nuevo.
- **21/21 casos** de sesión real con identidades y carpetas de diagnóstico aisladas:
  listado, destinos, envío/recepción, carpeta con archivo binario y subcarpeta vacía,
  archivos vacíos, hashes, confirmación final, renombrado de conflictos, pausa,
  reanudación, cancelación, rechazo de consentimiento en ambas direcciones, error
  devuelto al solicitante, destino inválido, red rechazada, cierre y permisos
  unidireccionales/tokens de otra sesión. Repetidos con las DLL compartidas exactas
  de los payloads publicados.
- Visor WinUI real: **7/7 casos** Debug y Release publicado, incluyendo pulsar ambos
  botones, comprobar contenido en las carpetas elegidas y deshabilitar al cerrar.
- Regresión WinUI de fullscreen/ventana/cierre: **15/15** en Release publicado.
- No se modificaron instalaciones actuales ni se detuvo TeamViewer.

Evidencia en `artifacts/diagnostics/`:

- `file-browser-live.json` y `file-browser-published-live.json`
- `ui-file-browser-debug.json` y `ui-file-browser-release.json` (y capturas PNG)
- `ui-fullscreen-101-regression.json`

Harness reproducible en `%LOCALAPPDATA%\Temp\opencode\session-input-probe`:
SessionProbe, `test-files.ps1`, `test-viewer-ui.ps1 -Files`. Solo el harness acepta
automáticamente los diálogos de sus identidades temporales; el producto conserva
su servicio de consentimiento. Credenciales de prueba protegidas con DPAPI, no
impresas. No equivale a una prueba física PC-YUSTE → PUESTO2.

## Compatibilidad y pendientes

- El nuevo administrador requiere **1.0.1 en ambos extremos**. Con 1.0.0 no responde
  a navegación y se muestra un aviso; las ofertas clásicas sin destino siguen
  siendo compatibles con la carpeta de Descargas configurada.
- No se garantiza navegación de carpetas protegidas, red o placeholders de nube
  que Windows presente como reparse points; se informa del acceso no disponible.
- Pendiente confirmación del usuario entre dos equipos físicos, otros DPI,
  directorios muy grandes y transferencias prolongadas. No se afirma riesgo cero
  ni que se hayan vuelto a probar manualmente todas las funciones del producto.
- Copia e instaladores 1.0.0 conservados; ver `19-versiones-backup.md`.
