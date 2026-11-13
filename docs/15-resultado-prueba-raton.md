# Ratón: interferencia reproducida y prueba real del inyector

> **Actualización posterior:** el documento
> [16-reparacion-video-validacion-sesion.md](16-reparacion-video-validacion-sesion.md)
> amplía las pruebas a arrastre, rueda, teclado y sesión cliente-servidor-host.
> También matiza la causalidad: la sesión final funcionó con el servicio
> TeamViewer activo, sin otra sesión controlando el cursor; un watchdog elevado
> de la propia prueba conservó el foco e invalidó ensayos intermedios.
> No interpretar las conclusiones históricas siguientes como prueba de que
> cualquier TeamViewer instalado/en ejecución impide usar CSIViewer.

Fecha: 13/11/2026, prueba válida entre 15:16:03 y 15:16:06 (hora local).

## Conclusión acotada

**El inyector actual sí mueve y hace clic correctamente en ambos monitores
cuando TeamViewer está completamente detenido en este equipo.** No se cambió
el código del producto entre los ensayos fallidos y el ensayo satisfactorio.

La prueba A/B demuestra una interferencia asociada al estado activo de
TeamViewer en esta máquina/configuración. No identifica si se trata de su
bloqueo de entrada, un hook, otra opción o un defecto de esa aplicación.
No afirmar que cualquier instalación de TeamViewer bloquea CSIViewer.

No se ha validado aún de extremo a extremo la sesión WinUI cliente → red →
host con el escritorio remoto visible sin esa interferencia. La franja verde
y la congelación de vídeo siguen pendientes y no quedan explicadas por esta
prueba del ratón.

## Qué se hizo

1. Con TeamViewer activo se usó una ventana propia de diagnóstico WinForms y
   el InputInjector real de CSIViewer, en WinSta0/Default, sesión 1, PerMonitorV2.
2. SendInput devolvía 1, pero el cursor no llegaba al destino y la ventana no
   recibía los clics. También falló un ensayo de botón sin coordenadas después
   de situar el cursor mediante una llamada de contraste a SetCursorPos.
   No se incorporó SetCursorPos al producto.
3. El usuario autorizó explícitamente detener TeamViewer temporalmente y
   confirmó disponibilidad de acceso físico al equipo.
4. Un script elevado detuvo el servicio TeamViewer y cerró sus procesos UI,
   Desktop y auxiliares tv_w32/tv_x64. Un bloque finally y un tiempo máximo
   restauraban el servicio, sin modificar su inicio automático ni configuración.
5. Se ejecutó el mismo inyector desde un proceso nuevo, **no elevado**. En ese
   proceso no apareció tv_x64.dll. La ventana de prueba recibió los seis clics.
6. Se restauró el servicio TeamViewer y se volvió a abrir su UI. Estado final
   comprobado: Running, Automatic. No se detuvo ni reconfiguró CSIViewer.

La presencia de tv_x64.dll por sí sola no prueba causalidad. Lo significativo es
el cambio de resultado del ensayo al retirar temporalmente TeamViewer completo.
Las funciones exportadas user32 SendInput/SetCursorPos llevaban a win32u en las
inspecciones de solo lectura; no se constató un salto de esas entradas a una DLL
de terceros. No se alteraron hooks, DLL, memoria, privilegios ni restricciones.

## Resultados de clics reales en la ventana de diagnóstico

| Monitor (índice) | Destino | Clic recibido | Desfase |
|---|---|---|---|
| Principal (0) | 700,425 | 699,424 | -1,-1 px |
| Principal (0) | 960,560 | 959,559 | -1,-1 px |
| Principal (0) | 1220,700 | 1219,699 | -1,-1 px |
| Secundario (1) | -1380,335 | -1381,334 | -1,-1 px |
| Secundario (1) | -1120,470 | -1120,469 | 0,-1 px |
| Secundario (1) | -860,610 | -861,609 | -1,-1 px |

Se verificó movimiento, leftDown y leftUp con coordenadas del monitor y recepción
del clic por la ventana. Tolerancia declarada en la prueba: 1 píxel por eje.
No se prueba aquí precisión perfecta, DPI distinto, arrastre, rueda o teclado.
La posterior ampliación para esos casos no llegó a ejecutarse: se canceló su
solicitud UAC. No se reintentó la elevación ni se dieron esos casos por aprobados.

## Evidencia guardada

En `artifacts/diagnostics/`:

- `mouse-baseline-teamviewer-on-20261113.log`: prueba fallida con TeamViewer.
- `mouse-isolated-teamviewer-off-20261113-151603.log`: salida del ensayo válido.
- `mouse-isolated-teamviewer-off-20261113-151603.json`: seis resultados, Passed=true.
- `teamviewer-isolation-20261113-151603.log`: parada y restauración del servicio.

Fuentes de la ventana de diagnóstico (fuera de la solución del producto):
`%LOCALAPPDATA%\Temp\opencode\mouse-injection-probe`.

## Condición de funcionamiento que ya se ha comprobado

Para la siguiente prueba de CSIViewer, usar acceso físico al equipo o un canal
que no controle su entrada y mantener TeamViewer completamente cerrado, incluido
su servicio, durante el ensayo. Esta es la condición A/B comprobada; no está
demostrado que baste con dejar de mover el ratón, cerrar solo una conexión o
cambiar una opción concreta. No se debe cerrar el único acceso remoto del
usuario sin consentimiento y alternativa local.

No sustituir SendInput ni quitar permisos para competir con un bloqueo de otro
programa. Tampoco es necesario reinstalar CSIViewer para repetir esta prueba:
los instaladores actuales ya contienen el inyector que superó los seis casos.

## Siguiente validación necesaria

1. Con TeamViewer realmente detenido y acceso físico disponible, conectar el
   visor de PC-YUSTE al host PUESTO2 y probar primero el monitor principal.
2. Clic en elementos conocidos, arrastre y rueda. Recoger trazas del mismo evento
   en ambos extremos y confirmar efecto en la aplicación del host.
3. Comprobar monitor secundario de forma separada. Si el vídeo está congelado,
   verificar físicamente la reacción del host: una imagen vieja puede ocultar
   el efecto correcto de una entrada real.
4. Solo después abordar la franja verde y el bloqueo de vídeo, sin atribuirlos
   a TeamViewer por extensión ni repetir cambios no demostrados.
