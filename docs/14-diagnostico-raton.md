# Diagnóstico del ratón — 13/11/2026

> Actualización posterior: [15-resultado-prueba-raton.md](15-resultado-prueba-raton.md)
> documenta la prueba A/B que sí consiguió seis movimientos y clics reales
> usando el inyector existente con TeamViewer detenido. Los ensayos fallidos
> relatados aquí son anteriores a ese aislamiento. La cadena completa del
> escritorio remoto y los defectos de vídeo siguen pendientes de validación.

Estado: **PENDIENTE DE VALIDACIÓN REAL**. Esta instrumentación no corrige
el mapeo, los permisos, la ruta de transporte ni el problema del monitor 2.

### Nueva prueba del usuario (13/11/2026, 14:55–14:56 local)

El usuario confirma que siguen el ratón sin control efectivo, la franja verde y
la imagen congelada. Se localizaron las trazas del host instalado instrumentado,
PID 24256, sesión `5c05a2bd-a4b6-4241-809f-e204f68d385f`.
Los eventos sí se reciben/descifran y alcanzan el inyector tras los permisos.
Por ejemplo, Trace `1776b728c7284159846dbf6b3f0f3255`: leftDown (42329,45253),
monitor (-1920,0) 1920x1080, absoluto virtual (18141,33940), Inserted=1,
LastError=0, DPI=2. El cursor observado inmediatamente fue (2521,0), fuera del
monitor seleccionado, y se repitió esa posición en varios eventos consecutivos.
No se considera prueba de que el clic llegara a la aplicación destino.

La evidencia descarta una pérdida total de eventos antes del host **en esta
prueba concreta**. El siguiente diagnóstico debe examinar qué sucede con la
entrada aceptada por SendInput (observación posterior, escritorio/contexto,
posibles restricciones/interferencias) sin cambiar APIs ni saltarse seguridad.
La medición actual del cursor es inmediata y no constituye por sí sola una
confirmación del procesamiento final de Windows.

Se conservaron 112 registros filtrados de esta prueba en
`artifacts/diagnostics/mouse-trace-20261113-150120-7a1b1f6b.jsonl`.
Los instaladores se han regenerado posteriormente (visor 15:00:50 y host
15:01:12) para cambiar exclusivamente el layout de chat: ambos tipos de globo
a la izquierda, enviados con sangría de 28 px. Por ello los hashes de paquetes
de las 14:32/14:33 que aparecen más abajo son históricos, no los paquetes actuales.

Verificación posterior: solución Debug y Release compilada con 0 errores y
0 advertencias; 159/159 pruebas correctas en ambas configuraciones. Las dos
pruebas nuevas cubren JSON previo sin traza y conservación de la traza a través
del cifrado/descifrado; no prueban eventos WinUI ni inyección real en Windows.

Ejecutables compilados:

- `src/CSIViewer/bin/Release/net10.0-windows10.0.19041.0/win-x64/CSIViewer.exe`
- `src/CSIViewer.Host/bin/Release/net10.0-windows10.0.19041.0/win-x64/CSIViewer.Host.exe`

No copiar solamente los EXE a otro equipo: necesitan sus archivos acompañantes
y el runtime/configuración de despliegue correspondientes.

## Qué cambia

- Cada evento de ratón enviado tiene `TraceId` opcional en el JSON cifrado.
  Los clics y la rueda se registran siempre; los movimientos, uno de cada 30.
- `Input UI event`: recepción WinUI de pulsación/liberación/rueda, posición
  relativa a RemoteSurface, tamaño de superficie/frame, permiso y resultado del
  mapeo. Un rechazo previo al envío queda visible aquí.
- `Input UI mapped`: coordenadas normalizadas enteras que se entregan al servicio.
- `Input transport result`: `Written=true` solo tras completar SendBinaryAsync.
  **No es un acuse del host.** La sobrecarga actual usa relay, no LAN directa.
- `Input received`: host tras descifrar, antes de comprobar autorización.
- `Input rejected`: rechazo de autorización, con TraceId para eventos muestreados.
- `Input injection result`: coordenadas absolutas enviadas, escritorio virtual,
flags, número solicitado/insertado por SendInput, último error Win32 y cursor
observado inmediatamente después mediante GetCursorPos.

GetCursorPos comparte el contexto DPI del hilo; sus valores no se deben asumir
como píxeles físicos. Otro dispositivo puede mover el cursor entre ambas llamadas.
Se registra también la conciencia DPI del hilo (0=no consciente, 1=sistema,
2=por monitor; -1=inválida), sin modificarla.
LastError no demuestra la causa: Windows no garantiza identificar un bloqueo
UIPI y, si la inserción tuvo éxito, ese valor no debe interpretarse como fallo.
La aceptación por SendInput tampoco prueba que la aplicación destino procesara
el clic. No se registran contenidos de teclado ni texto en estas trazas nuevas.

Los fallos de descifrado de frames con cabecera Input se registran muestreados
(primero y después cada 30); esa cabecera aún no está autenticada. En esa fase
no se puede recuperar el TraceId cifrado. No se ha instrumentado todavía cada
descarte de la cola de recepción ni todas las rutas heredadas de sesión.

## Prueba siguiente

1. Ejecutar visor y host compilados con esta instrumentación en dos equipos.
   Los instaladores nuevos de las **14:32/14:33 sí incluyen** la traza;
   los anteriores de las 14:07/14:08 no la incluyen.
2. En el monitor principal del host abrir una ventana normal, no elevada,
   con objetivos conocidos (p. ej. una lista de elementos).
3. Hacer clic arriba, centro y abajo; después arrastrar y usar la rueda.
   Registrar la hora y qué objetivo se esperaba/qué elemento se seleccionó.
4. Recoger logs de ambos extremos y seguir el mismo TraceId por las etapas
   anteriores. Conservar también SessionId y timestamps UTC.
5. Comparar coordenadas, monitor y posición observada antes de modificar nada.
   No concluir que no hubo eventos porque falte una línea muestreada.

Logs: host `%LOCALAPPDATA%\CSIViewer.Host\logs`; visor en su directorio de
datos configurado (`logs`). Los logs del visor de prueba anterior estaban en
`%LOCALAPPDATA%\Temp\opencode\device-dot-top-test\viewerdata\logs`.

## Correcciones a la interpretación de AI_HANDOFF.md

- La atribución causal a SetCursorPos/DPI no quedó demostrada por una prueba
  controlada; no repetirla como diagnóstico verificado.
- El antiguo mensaje `Input sent` podía aparecer sin envío efectivo.
- La falta de logs de puntero en el visor de prueba local no demuestra por sí
  sola un problema de hit testing (puede ser otra instancia o una prueba inválida).
- Hay entradas históricas `Input injected` en el host, pero no validan ni la
  posición final ni los binarios actuales.

## Prueba local aislada del inyector (13/11/2026)

Se creó una ventana WinForms de diagnóstico fuera del proyecto, usando el
InputInjector real y un contexto PerMonitorV2, igual que el manifiesto del host.
La prueba verifica con WindowFromPoint que el punto pertenece a su propia ventana
antes de mover y antes de enviar clics. Si no coincide, aborta sin pulsar.

Resultado: **prueba no superada, sin validación del clic**.

- Monitor principal: 2560x1440 en (0,0).
- Monitor secundario: 1920x1080 en (-1920,0).
- Escritorio virtual: (-1920,0), 4480x1440.
- Proceso en sesión 1; escritorio del hilo y escritorio de entrada: Default.
- Clip del cursor: (-1920,0)-(2560,1440), sin restricción adicional observada.
- Destino en ventana propia: (700,425).
- Entrada normalizada: (17920,19342); absoluto virtual enviado: (38326,19342).
- SendInput: solicitado 1, insertado 1, LastError 0, conciencia DPI 2.
- GetCursorPos y GetPhysicalCursorPos tras 80 ms: (-931,308), no el destino.
- Una llamada de contraste a mouse_event con los mismos valores absolutos
  tampoco llevó el cursor al destino tras 200 ms. Esta API solo se usó en el
  diagnóstico temporal: **no se cambió el inyector del producto**.
- En otros intentos la ventana objetivo estaba oculta por otra ventana y la
  prueba abortó. No se atribuye eso al producto ni a un componente concreto.

Esto no permite distinguir todavía entre interferencia del entorno de ejecución
y un fallo de la entrada en Windows; no demuestra una causa DPI ni un defecto
de WinUI/red. No se han probado clics reales satisfactorios ni la cadena completa.
Se detuvieron las pruebas sintéticas para no producir clics fuera de la ventana.
La posición original del cursor se restauró al finalizar; no se detuvieron
procesos, tareas ni servicios existentes.

Prueba temporal y registros reproducibles en:
`%LOCALAPPDATA%\Temp\opencode\mouse-injection-probe`.
Último registro detallado: `probe-native-baseline.log`.
La prioridad continúa siendo obtener una traza del visor y host instrumentados
con entrada física y objetivos conocidos, no sustituir APIs de inyección a ciegas.

## Paquetes nuevos y comprobación de contenido

Publicación Release nueva de visor, host UI y agente; compilación Inno Setup
finalizada correctamente. No se ejecutaron los instaladores en esta máquina.
No se regeneró el servidor, ya que no necesita cambios para esta traza.

| Instalador | Fecha local | SHA-256 |
|---|---|---|
| `artifacts/installers/CSIViewer-Setup-1.0.0.exe` | 13/11/2026 14:32:45 | `747688ECAAED35EAB178059A864AC43726B9D55B93A9F49E02B3F8E275F57053` |
| `artifacts/installers/CSIViewer.Host-Setup-1.0.0.exe` | 13/11/2026 14:33:08 | `94F6CA928BF33C36CFADF38BAEC337AEE2B2419006A434544772BF3A593F8705` |

Se comprobaron marcadores de traza en los ensamblados publicados de ambas UI
y Services. Services, Media y Protocol tienen el mismo SHA-256 en ambos payloads.
Esto verifica el contenido de origen del empaquetado, no una instalación real.

## Recogida acotada de logs

Nuevo script `scripts/collect-input-diagnostics.ps1`: lee los logs, exporta
solo etapas y campos de diagnóstico permitidos y no modifica los originales.
Excluye mensajes originales completos, excepciones, chat, texto y acciones de
teclado. Probado con Windows PowerShell 5.1 y un fixture que incluye datos privados
ficticios y una línea JSON incompleta: se exportaron solo los 2 eventos esperados,
sin el marcador privado ni KeyCode.

Ejecutar en ambos equipos inmediatamente después de reproducir (rango por
defecto: últimos 20 minutos). Desde la raíz del proyecto:

```powershell
powershell.exe -NoProfile -File scripts/collect-input-diagnostics.ps1 -OutputDirectory "$env:USERPROFILE\Desktop\CSIViewer-diagnostico"
```

También se puede copiar únicamente el script al otro equipo y ejecutarlo allí.
Si el visor es portable o tiene datos personalizados, especificar su carpeta:

```powershell
powershell.exe -NoProfile -File scripts/collect-input-diagnostics.ps1 -LogDirectory 'D:\CSIViewer\data\logs' -OutputDirectory "$env:USERPROFILE\Desktop\CSIViewer-diagnostico"
```

Si no exporta eventos, comprobar la ruta, el rango temporal y la versión instalada;
un fichero vacío no prueba que se perdieran eventos. Compartir los `.jsonl` de
ambos extremos, hora de la prueba, monitor y destino esperado/observado de los clics.
