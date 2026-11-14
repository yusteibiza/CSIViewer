# Revisión estática WinUI y recorrido de vídeo

## Alcance y límites

Revisión de código, inventario XAML/Roslyn y contratos automatizados de fuentes.
**No se ha ejecutado una inspección interactiva completa de WinUI ni VMware.**
Los contratos de texto/XML evitan regresiones concretas, pero no ejecutan
DispatcherQueue, ContentDialog, pickers, compositor, Narrador ni controladores.
No se ha usado SendInput en esta continuación; la integración de chat sustituye
entrada/captura por backends que fallan si se intenta acceder al escritorio.

## Superficies y ciclo de vida revisados

| Superficie (visor y host salvo indicación) | Hallazgo/estado de revisión |
|---|---|
| App/MainWindow | Ventana única, ocultación/bandeja distinta de cierre real; `MainWindowSessionBanner` se limpia al cerrar; DisposeAsync del cliente se observa en cierre con best-effort. Falta validar salida real mientras hay transferencias |
| Home/Host | Páginas cacheadas; refrescan estado desde AppState; visibilidad/minimización del host no equivale a terminar sesión. Exige P02/P03/V02 en laboratorio |
| RemoteControl | Cola UI mantiene último frame; dispatcher evita acumular renders; generación y visibilidad evitan aplicar JPEG antiguo tras unload. `LostFocus`/`PointerCaptureLost` liberan entrada; timer de toolbar se detiene en unload. Corregidos DataWriter sin finally y solicitudes de monitor sin observación de fallos |
| Chat | Controller compartido, selector/historial/carga con versión; suscripciones ligadas a páginas cacheadas durante vida de aplicación. Falta recorrido interactivo de borrar/exportar/notificar y medición de memoria tras navegación repetida |
| FileTransfer/Browser | Timer de refresco se detiene en unload; revisiones local/remota invalidan cargas viejas; navegación remota tiene timeout. Transferencias autorizadas pueden continuar al cambiar de página, intencionalmente |
| Devices | Navegación precarga identidad/credenciales y selección; eliminación necesita validar de nuevo visualmente selector/borrador. No se ha probado pulsando en UI esta etapa |
| Contacts/ContactEditorDialog | Campos/validación/tema explícitos; flyout se oculta en finally. Corregidos reentrancia de editar/borrar y propagación sin captura de errores de persistencia en async void; usa InfoBar ya existente |
| History | Filtrado sobre historial de AppState, página cacheada. Borrado/picker y sus errores requieren recorrido manual |
| Settings/HostSettings | Loaded recarga configuración; selección vacía es staging intencional, no función simulada. Guardar protege doble operación; Defaults/Restore no son Guardar. Pickers, task y UAC no se han ejercitado contra el equipo habitual |
| Help/About | Recursos/localización y navegación cacheada; no se ha sustituido su diseño ni eliminado métodos sin referencias |
| Servidor MainWindow | Ahora fallo de consulta SCM no habilita Crear; acciones se bloquean también durante operaciones locales/de servicio; confirmaciones no pueden duplicarse; fallos de confirmación se muestran. Se mantiene captura de errores de Start/Stop local |
| Temas/AccentAppearance | Light/Dark/Default/HighContrast, RequestedTheme Default para Sistema y ActualThemeChanged para barra. Cambio de acento en UISettings se despacha a UI. Colores de estado y superficie remota fijos no se consideran automáticamente defectos: se necesita contraste/contexto visual |

Las páginas usan `NavigationCacheMode.Required`: las suscripciones en constructor
no se vuelven a añadir en cada Loaded. Mantenerlas al navegar es intencional;
esto no prueba por sí solo que no existan referencias retenidas tras cerrar la
aplicación. No se han borrado suscripciones indiscriminadamente para ocultarlo.

Pendiente de P13/P14/P15: cada Click/SelectionChanged/TextChanged real,
tabulación, foco, Narrador, DPI, diálogos abiertos al cambiar tema, rechazo de
dispatcher al cerrar, pickers concurrentes y cierre mientras esperan operaciones.
La compilación comprueba firmas/conexión de eventos XAML, no sus efectos reales.

## Recorrido del incidente VMware

1. **Captura:** `WindowsGraphicsCapture.TryStart` crea D3D11 hardware y captura un
   **monitor**, no la ventana minimizada. FramePool free-threaded de 2 buffers;
   frame gate descarta solapamientos. El cursor capturado se desactiva para no
   superponerlo al cursor local del visor. Una GPU sin respuesta puede dejar
   pendiente `SoftwareBitmap.CreateCopyFromSurfaceAsync`, sin excepción inmediata.
2. **Generaciones/cierre:** runs de WGC retirados no publican frames nuevos. El
   Dispose nativo se mueve al background y tiene límite de espera desde cliente;
   un driver colgado puede sobrevivir a ese límite. Corregidas dos referencias
   COM ABI propias que no se liberaban tras FromAbi (device/item). Comprobación
   estática y build, **sin prueba de fugas D3D real** ni atribución causal al freeze.
3. **Recuperación de captura:** `RemoteClient.CaptureRecovery` distingue escritorio
   quieto de demanda visual pendiente. Falta de primer frame >8 s o silencio >4 s
   tras cambio solicita WGC→GDI. Las nuevas pruebas usan timestamps sintéticos y
   cancelan el recovery antes de habilitar el estado simulado; no capturan pantalla.
   Esto comprueba la decisión del watchdog, no que VMware vuelva a producir imagen.
4. **Codificación:** `SendVideoFrameAsync` usa `_videoSendBusy` y `_encoderGate`;
   descarta nuevas capturas si está ocupado, sin colas ilimitadas de imágenes.
   `H264Codec.cs` usa transforms de Media Foundation y buffers en memoria; no se
   ha establecido que el códec de la sesión real fuera un pipeline hardware de
   VMware. Fallo del encoder produce fallback JPEG; cambio de dimensiones lo
   reinicializa/descarta según la ruta existente.
5. **Transmisión:** vídeo/control comparten señalización serializada; AUD-005
   ordena nonce/envío por crypto. Un send de vídeo detenido por backpressure
   puede mantener encoder/send-busy, aunque la captura siga completando frames.
   El watchdog de captura **no prueba progreso de envío**. No se añadió un timeout
   ciego que abortara el WebSocket y las otras sesiones.
6. **Recepción/decodificación:** cola H.264 acotada y generaciones; el decode no
   corre en bucle de red. Si la cola se llena o falla el decoder, se pide JPEG
   independiente en vez de perder P-frames silenciosamente. No existe evidencia
   en los logs entregados para localizar el último ProcessOutput de esa sesión.
7. **Renderizado:** BGRA→WriteableBitmap en hilo UI; JPEG usa BitmapImage y valida
   generación tras awaits. Esto usa el compositor WinUI, no un swapchain propio
   de la aplicación con manejo explícito de DeviceRemoved. No hay demostración
   de un reset de D3D del visor en el incidente. Las estadísticas dec/ui solo se
   actualizan al mostrar frames: no proporcionan una línea temporal completa.
8. **Entrada:** cursor local fluido en A no demuestra cursor remoto ni reacción
   de aplicaciones en B. Logs históricos: 14 trazas emparejadas, SendInput 1/1,
   error 0, posiciones observadas luego fijas. Aceptación por SendInput no prueba
   consumo por el escritorio/aplicación. El invitado local responsive y posterior
   recuperación descartan un bloqueo total permanente, no un fallo transitorio
   del escritorio/driver/captura/entrada.

## Conclusión

**Incidente NO resuelto ni reproducido en este entorno.** Las correcciones de
robustez y fugas no justifican declararlo cerrado. Faltan métricas correlacionadas
de captura→encode→send→receive→decode→render durante V01–V07 y logs del invitado
en el intervalo. No se han desactivado H.264, WGC, aceleración 3D, VMware Tools
ni optimizaciones sin evidencia. Consultar `45-validacion-manual-release.md` para
las acciones exactas, resultados esperados y registros que debe recoger el usuario.
