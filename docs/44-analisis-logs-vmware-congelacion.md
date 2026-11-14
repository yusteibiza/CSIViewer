# Lectura de registros de la congelación VMware

> La prohibición histórica de pruebas que figura más abajo correspondía a la
> lectura inicial de logs. La auditoría posterior autoriza pruebas aisladas.
> El usuario confirmó después que la consola local de la VM respondía y que la
> sesión se recuperó tras esperar; una reconexión posterior funcionó de inmediato.
> Eso NO identifica ni demuestra resuelta la causa. Continuación y matriz de
> reproducción: `45-validacion-manual-release.md` y `46-revision-ui-video.md`.

## Entradas proporcionadas por el usuario

- `F:\Temp\CSIViewer-diagnostics-20261114-212000.zip`: diagnóstico y log del visor.
- `F:\Temp\csiviewer.host-20261114.log`: registro del host del Windows10 invitado.

Se leen en modo de solo lectura. El ZIP se consulta en memoria, sin extraer
archivos sobre el proyecto. No se copian registros completos ni datos privados
al repositorio. No se ejecutan aplicaciones, pruebas ni comandos de entrada.

## Última sesión coincidente

- SessionId `3a0fad9b-b427-4ffc-baf7-c3c26e7f4aad` en ambos extremos.
- VisorPID8964, hostPID5612. El host inició el proceso sobre20:45hora local;
  los logs solo informan versión1.0.1.0 y no identifican inequívocamente el lote.
- Inicio sobre21:19:23 en reloj visor,21:19:27 en reloj host (+01:00).
- Transporte Relay, permisos ViewScreen/ControlInput/Chat/archivos concedidos.
- Host usa WGC+H.264,1920x1080,90FPS,25Mbps. El diagnóstico del visor muestra
  sus preferencias locales60FPS; no son necesariamente las del host.
- Se emparejan14 trazas de entrada enviadas y recibidas. Todas se aceptan por
  SendInput (Requested=1,Inserted=1,LastError=0), incluyendo clics posteriores.
- Las primeras posiciones observadas cambian. Tras el primer clic, el host
  observa CursorX=1186,CursorY=129 repetidamente, incluso cuando el movimiento
  solicitado cambia hasta la esquina izquierda. Es una pista de entrada/estado
  del escritorio invitado, no únicamente una observación de la imagen del visor.
- SendInput aceptado NOdemuestra que el escritorio/aplicación procese el evento.
- No hay error explícito de captura/codificación/decodificación en esta sesión,
  ni cierre/desconexión registrado en el tramo aportado. La advertencia del visor
  de puntero fuera de imagen corresponde al margen de la imagen, no demuestra
  la causa de la congelación.
- La diferencia entre relojes en trazas emparejadas es aproximadamente3,895s,
  casi constante. No interpretarla como latencia de red medida: no están
  sincronizados y no hay un intercambio RTT en estas trazas.
- El host aportado termina en21:19:32hora local, pocos segundos después de
  iniciar la sesión. No tiene métricas periódicas que permitan conocer el último
  fotograma capturado, enviado, decodificado o mostrado. Ausencia de warnings
  no demuestra que el vídeo continúe ni que el watchdog estuviera funcionando.
- Advertencias H264backlog de la mañana y errores inicialeslocalhost son de
  sesiones anteriores y no se deben atribuir a esta congelación. Los errores
  ALTER TABLE repetidos son intentos de migración capturados al arrancar, no
  una excepción de aplicación no controlada en el instante del fallo.

## Conclusión y siguiente dato necesario

La entrada llega al host: no es simplemente que el visor no transmita clics ni
que falte permiso ControlInput. No hay evidencia suficiente para afirmar que
WGC, H264, VMware Tools o la GPU sean la causa exacta. No desactivar opciones
ni cambiar políticas/privilegios indiscriminadamente.

Preguntar si **el Windows invitado responde al usar directamente la consola de
VMware mientras el visor está congelado**. Esto separa un bloqueo del invitado
de un fallo de captura/inyección remota. Si sigue sin quedar claro, ampliar
telemetría pasiva de captura/envío/decodificación/escritorio, con identificación
inequívoca de compilación. No ejecutar nuevas pruebas por la instrucción vigente.

No hay cambios de runtime ni instaladores nuevos derivados de esta lectura.
Última entrega sigue siendo14/11/2026 20:52:46 +01:00 (informe43).
