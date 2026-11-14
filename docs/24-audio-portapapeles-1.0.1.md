# Audio, portapapeles y panel neutro — 1.0.1

> Confirmación posterior del usuario: audio perfecto entre equipos de casa y
> oficina al usar un tercer equipo para TeamViewer. El ruido inicial era un bucle
> porque TeamViewer y el host estaban en el mismo ordenador de casa. Ver informe 25.

## Uso

1. Actualiza visor, host y servidor: el protocolo añade permisos Audio=32 y
   Clipboard=64; un servidor antiguo puede rechazar esas solicitudes.
2. Antes de conectar, marca **Audio del sistema** o **Portapapeles de texto**.
   Se dejan desmarcados por defecto. El anfitrión debe conceder los permisos.
3. Durante la sesión, marca **Audio** o **Portapapeles** en la barra de control.
   Desmarca para detener. No se activan solos al volver a conectar.

El audio es la salida del equipo remoto hacia el controlador; **no captura el
micrófono**. Se usa WASAPI loopback, con NAudio.Wasapi 2.2.1. El formato nativo
se transporta sin compresión, con paquetes limitados a 64 KiB, cola de captura
de cuatro paquetes y búfer de reproducción de 300 ms. A 48 kHz estéreo float32
requiere unos 3,1 Mbit/s además del vídeo. Si cambia el dispositivo/formato,
puede ser necesario desactivar y reactivar Audio. Los errores de dispositivo
no deben cerrar la sesión ni abortar su WebSocket.

El portapapeles comparte **texto nuevo**, máximo 1 MiB UTF-8, en ambas direcciones.
No envía el contenido que ya había al activarlo, ni imágenes ni archivos. No se
almacena en el historial de la aplicación; los textos recibidos se excluyen del
historial y la sincronización de Windows. Una marca de origen evita rebotes y
que texto de una sesión remota se reenvíe automáticamente a otra sesión. No
copies información sensible mientras esté activado.

Las aplicaciones coalescen las actualizaciones de texto: como máximo una aplicación
UI pendiente por sesión, conservando solo el texto más reciente, y una lectura
local en curso. No se acumula una cola de dispatcher sin límite.

Los nuevos frames siguen usando SessionCrypto y el identificador de sesión.
Se comprueban permisos y activación al recibir. Audio entrante en un host se
descarta. Al desconectar se paran captura/reproducción y la vigilancia del
portapapeles. No se modifican políticas de seguridad, UAC, UIPI ni escritorio seguro.

## Verificaciones

- Solución Debug aislado y Release: cero advertencias/errores, **236/236 tests**
  en cada configuración.
- `scripts/test-session-features.ps1`: **15/15 comprobaciones reales**, usando
  servidor temporal, identidades sintéticas y dos clientes, sin tocar el servidor instalado.
- Probado: permisos nuevos, apagado inicial, texto cifrado en ambas direcciones,
  ContentChanged local, ausencia de eco, rechazo de UTF-8 inválido y más de 1 MiB,
  audio silencioso cifrado y reproducción WASAPI, rechazo de audio inverso,
  descarte con opciones apagadas, captura loopback nativa, limpieza y sesión chat antigua.
- La prueba de audio evita el bucle de sonido que producirían host y visor en el
  mismo ordenador. La captura loopback y el transporte/reproducción se comprueban
  por separado con silencio; **sonido audible entre dos equipos físicos pendiente**.
- El diagnóstico del portapapeles copia únicamente textos sintéticos, no imprime
  ni guarda el contenido previo del usuario. Puede dejar ese texto sintético copiado.
- Visor real: **10/10 comprobaciones UI** de selección de permisos, apagado por
  defecto, controles no disponibles sin sesión y presencia de ayuda.
- Matriz final de idiomas Debug: host 18/18, servidor 17/17, visor 18/18 en
  repetición con espera de disponibilidad UI. El primer intento del visor falló
  en la comprobación inicial de AccentColorButton antes de probar idiomas;
  ambas evidencias permanecen en `artifacts/diagnostics/languages-features-*`.

## Menú lateral

El tinte era el fondo Mica visible a través del panel transparente de NavigationView.
Los fondos expandido/compacto usan ahora `#F3F3F3` solo en Light, tanto en visor
como en host. Dark y alto contraste no se cambian. Color verificado en captura
real: `artifacts/diagnostics/neutral-navigation-viewer-release.json.purple-light.png`.

## Entrega

Publicación secuencial self-contained win-x64 y tres instaladores completados:

| Instalador en `artifacts/installers` | Hora 13/11/2026 | SHA-256 |
| --- | --- | --- |
| CSIViewer-Setup-1.0.1.exe | 22:36:26 | 95EEE20101445E131C4F3FE4DD2892203BA1BFC0AACE0A6321809E70ED890F6F |
| CSIViewer.Host-Setup-1.0.1.exe | 22:38:35 | 3C902020C432824E439B67BAE058E6A47C9373FDE59CF0D0672C44F0F1004201 |
| CSIViewerServidor-Setup-1.0.1.exe | 22:37:22 | FFE2283010D33E96E9AC1F3990DEE5C64AA656A7C5626F37F3E3963DB3CD647B |

Manifiesto: `artifacts/diagnostics/audio-clipboard-installers.json`. Ejecutables
publicados 1.0.1.0, instaladores 1.0.1. Dependencias NAudio y licencia MIT presentes
en visor/host; los payloads no incluyen identidades, contraseñas ni bases de datos.
Visor publicado: otras **10/10 comprobaciones UI** superadas.

No se han actualizado aplicaciones instaladas ni cerrado el visor del usuario.
El Debug de validación está en `artifacts/feature-validation`. El ZIP original
1.0.0 se conserva. La prueba audible entre dos PCs y la regresión completa de
archivos/fullscreen tras mover las estadísticas de la barra a una fila propia
siguen pendientes; no se afirma cobertura exhaustiva.
