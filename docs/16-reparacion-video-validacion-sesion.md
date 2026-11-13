# Reparación de vídeo y validación de sesión — 13/11/2026

## Resultado actual y límites de la comprobación

- Entrada: se verificaron clics, arrastre, rueda, scancode de teclado y texto
  Unicode desde un cliente de diagnóstico conectado al **host instalado**, por
  el servidor real. Ambos monitores pasaron. No se sustituyó InputInjector.
- Vídeo: se reprodujo y corrigió el cálculo erróneo del plano de crominancia
  NV12 con altura codificada mayor que la visible. Se probó el códec real y
  la recepción de vídeo cambiante en cuatro selecciones de monitor.
- Saturación: se provocó una pausa de 300 ms en la decodificación; el cliente
  pidió JPEG y continuó recibiendo imagen cambiante, sin mantener una cadena
  H.264 con referencias perdidas.
- Build Debug y Release: 0 errores, 0 advertencias. Tests: **166/166** en ambos.

**No es una validación manual exhaustiva con dos equipos físicos.** El cliente
de diagnóstico corre en PUESTO2, usa RemoteClient/servidor/host y una ventana de
prueba propia, no los eventos/renderizado de la página WinUI del visor. No se
deben presentar estos resultados como prueba de todos los escenarios ni como
confirmación del usuario. Sigue pendiente esa última validación en PC-YUSTE.

## 1. Ratón y condiciones del entorno

La primera prueba aislada del documento 15 pasó al detener TeamViewer completo.
En esta continuación pasaron además arrastre, rueda, teclado y texto, sin
TeamViewer, en los dos monitores (`mouse-extended-without-teamviewer.json`).

Sin embargo, varias pruebas de sesión posteriores fallaron porque la consola
elevada del **propio watchdog de diagnóstico** (PID 13692, integridad 0x3000)
conservaba el primer plano. El cliente de prueba era de integridad media.
Ocultar su ventana no liberó el primer plano; la solicitud normal de activación
de la ventana de prueba seguía devolviendo false. No se usó AttachThreadInput,
UIAccess, desactivación de UIPI ni cambios de seguridad para forzar la entrada.

Se finalizó el watchdog mediante su fichero `done`, restaurando TeamViewer.
Después, la prueba de sesión pasó con **el servicio TeamViewer Running**, sin
otra sesión controlando el ratón. Esto matiza la conclusión anterior:

> La presencia o instalación de TeamViewer por sí sola **no demuestra** que
> bloquee CSIViewer. El aislamiento inicial cambió el resultado, pero hubo
> condiciones de foco y permisos que también influyeron. No se identificó una
> opción concreta de TeamViewer como causa universal. No recomendar detener
> servicios o elevar CSIViewer indiscriminadamente.

El inyector no cambió en esta intervención. El control normal de Windows funciona
en las pruebas finales. La entrada sobre ventanas elevadas/escritorio seguro
sigue limitada por Windows; no intentar saltarse esa frontera.

## 2. Causa reproducida de la franja de color

`H264Decoder` convertía NV12 suponiendo que el plano UV empezaba en
`stride * alturaVisible`. Media Foundation entrega una superficie codificada
alineada a bloques:

| Imagen visible | Búfer de salida del decodificador |
|---|---|
| 640x360 | 640x368 |
| 1920x1080 | 1920x1088 |
| 1280x720 | 1280x720 |
| 2560x1440 | 2560x1440 |

La lectura de UV empezaba ocho filas antes en los dos primeros casos. Con un
patrón rojo en las primeras 32 filas se reprodujo un error medio absoluto de
color de ~81 niveles/canal en esa franja. Tras usar la altura codificada de
MF_MT_FRAME_SIZE, el mismo patrón pasó a ~0–0,05 niveles. 720p y 1440p permanecieron
correctos. La salida confirmada del MFT era NV12 (subtipo 3231564e...).

### Cambio

`src/CSIViewer.Media/H264Codec.cs`:

- GetOutputLayout lee stride y altura codificada por cada salida.
- DecodeNv12Frames conserva ambos valores con los bytes de cada fotograma.
- Nv12ToBgra usa la altura codificada para localizar UV y solo entrega el área
  visible al renderizador. Valida dimensiones pares y tamaño del búfer.
- Se añadieron pruebas con padding vertical, stride horizontal y truncamiento.

La primera inspección temporal por reflexión del subtipo se ejecutó con una
firma de puntero incorrecta y terminó con access violation. Ese ensayo no se
usa como prueba válida. Se corrigió la herramienta externa y se repitió toda
la medición antes/después; el código del producto no sufrió ese fallo.

## 3. Arranque tardío y referencias H.264

Antes, el decodificador entregaba la primera imagen en la entrada 24 y solo
36 salidas en 60 entradas. El código buscaba IMFAttributes directamente en el
transform, en vez de usar IMFTransform.GetAttributes.

Se usa GetAttributes y, para el decodificador Microsoft, ICodecAPI con
CODECAPI_AVLowLatencyMode en **VT_UI4**. La documentación oficial advierte que
este decodificador no acepta VT_BOOL para esa propiedad. El ensayo con VT_BOOL
devolvió E_INVALIDARG; VT_UI4 devolvió S_OK. La prueba posterior produjo la primera
salida en la entrada 5 y 57–58 salidas/60. No se afirma latencia cero: todavía hay
fotogramas en vuelo.

Referencias:
- https://learn.microsoft.com/en-us/windows/win32/medfound/mf-low-latency
- https://learn.microsoft.com/en-us/windows/win32/medfound/codecapi-avlowlatencymode

`src/CSIViewer.Services/RemoteClient.Session.cs`:

- La cola ya no elimina silenciosamente el H.264 más antiguo (DropOldest).
  Los P-frames dependen de referencias previas; no son imágenes independientes.
- La capacidad sigue limitada a 2. Si se llena, se solicita el fallback JPEG
  existente, una sola vez, y se dejan de publicar H.264 de esa generación.
- Cada trabajo de vídeo lleva una generación. Una configuración nueva o fin
  de sesión invalida trabajos ya retirados de la cola pero aún esperando el
  gate del decodificador. Evita decodificar el monitor anterior con la nueva
  configuración.
- SingleReader=false refleja que también se drena la cola al reconfigurar.

Este fallback puede reducir FPS, pero evita conservar una imagen H.264 corrupta.
No se cambió la preferencia guardada del usuario ni el bitrate/perfiles. Un nuevo
cambio de monitor configura una nueva generación y puede volver a usar H.264.

## 4. Prueba de sesión real de diagnóstico

Cliente temporal independiente, identidad de pruebas propia, sin tocar identidad,
contraseña, configuración o historial del usuario. Se leyó la contraseña del
host mediante DPAPI para el handshake, solo en memoria y sin imprimirla.
El host conserva su consentimiento automático y comprobación de contraseña.
No se omitieron permisos ni ControlOwner.

Ruta: cliente RemoteClient → cifrado → relay del servidor → host instalado →
InputInjector → ventana WinForms propia. Vídeo: WGC del host → H.264 → servidor →
RemoteClient corregido → comprobación del color de la ventana de diagnóstico.
Se animó esa ventana entre rojo y azul cada 250 ms; no se guardaron capturas del
escritorio del usuario, solo conteos/metadatos y patrones sintéticos.

### Prueba final con sobrecarga inducida

| Caso | Resultado |
|---|---|
| Principal: vídeo durante 3 s | 35 frames, ambos colores observados |
| Principal: clic real | recibido en (719,440), destino (720,440) |
| Principal: arrastre, rueda, tecla A, texto | todos recibidos |
| Secundario: vídeo durante 3 s | 64 frames, ambos colores observados |
| Secundario: clic real | recibido en (-1360,349), destino (-1360,350) |
| Secundario: arrastre, rueda, tecla A, texto | todos recibidos |
| Vuelta al principal | 16 frames JPEG, ambos colores observados |
| Vuelta al secundario | 54 frames, ambos colores observados |

**8/8 comprobaciones finales Passed=true.** Se observó el aviso de saturación y
se verificó que JPEG continuaba recibiéndose con contenido cambiante. Los conteos
son de esta prueba local, no una promesa de rendimiento entre dos máquinas.
Las esperas de asentamiento están incluidas; no son mediciones puras del tiempo
de conmutación. Los intentos fallidos previos se conservan en Temp y no se mezclan
con los resultados satisfactorios.

Después del empaquetado se copiaron las DLL exactas Media y Services del payload
publicado al cliente de diagnóstico (igualdad SHA-256 comprobada) y se repitieron
las ocho comprobaciones: **8/8 Passed=true**. Conteos de vídeo en esa repetición:
37, 59, 13 JPEG y 61 frames en las cuatro fases. Registros definitivos:
`artifacts/diagnostics/session-final-published-binaries.json` y `.log`.
TeamViewer seguía Running/Automatic y su UI estaba abierta al terminar. No se
usó una DLL vieja del banco de pruebas para esta validación final.

## 5. Evidencia y archivos

`artifacts/diagnostics/`:

- `mouse-extended-without-teamviewer.json` y `.log`
- `video-padded-height-before.log`
- `video-padded-height-after.log`
- `session-input-video-overload-recovery.json` y `.log`

Fuentes de herramientas temporales en `%LOCALAPPDATA%\Temp\opencode\`:
`mouse-injection-probe`, `video-probe`, `session-input-probe`. No son otra solución
del producto. Se crearon varias identidades `CSIViewer-QA-input-*` para sesiones
aisladas; no se borraron registros/contactos ajenos para ocultar esas pruebas.

Cambios del producto:

- `src/CSIViewer.Media/H264Codec.cs`
- `src/CSIViewer.Media/AssemblyInfo.cs` (acceso de tests a conversión interna)
- `src/CSIViewer.Services/RemoteClient.Session.cs`
- `tests/CSIViewer.UnitTests/Nv12LayoutTests.cs`
- `tests/CSIViewer.UnitTests/H264RoundTripTests.cs`

No se reescribieron autenticación, chat, archivos, instaladores ni arquitectura.
El chat sigue con ambos globos a la izquierda y enviados con sangría de 28 px.

## 6. Instaladores y restauración

Publicación Release completa del visor y host UI; ensamblados compartidos Media y
Services comprobados idénticos en ambos payloads. Instaladores Inno generados:

| Paquete | Fecha local | SHA-256 |
|---|---|---|
| `artifacts/installers/CSIViewer-Setup-1.0.0.exe` | 13/11/2026 16:12:24 | `11CFF004EF2266318F3DDC05288EF8D6A6943C9F3236CE6F1B1307D58D45026B` |
| `artifacts/installers/CSIViewer.Host-Setup-1.0.0.exe` | 13/11/2026 16:12:51 | `BC1B265D640674912F7D8EA3CEFAAF431FA4E22D52DB8976509DA679DC02C0CE` |

No se ejecutaron estos instaladores ni se actualizaron silenciosamente los
programas instalados del usuario. **Actualizar especialmente el visor**: allí
se decodifica el vídeo y sin actualizarlo seguirá el código anterior. El servidor
no necesita una actualización para estos cambios.

TeamViewer: servicio restaurado el 13/11 a las 16:07:27; UI abierta de nuevo y
estado Running/Automatic comprobado al finalizar. Se había configurado un tiempo
máximo de parada de 25 minutos, pero se restauró antes mediante `done`.
No se dejó una parada pendiente ni se alteró el tipo de inicio. El host se
reinició una vez para aislar su contexto y quedó de nuevo en ejecución.

## 7. Validación que falta

Instalar los paquetes nuevos y probar desde PC-YUSTE el visor WinUI completo
contra PUESTO2: clics en lista, arrastre, rueda, teclado, pantalla completa,
principal/secundario y contenido en movimiento durante una sesión prolongada.
No ejecutar otro control remoto simultáneo ni probar sobre UAC/ventanas elevadas
para diagnosticar primero el control normal. No dar el trabajo por confirmado
por el usuario hasta recibir esa comprobación real.
