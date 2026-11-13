# Ayuda y color de énfasis — 1.0.1

Ayuda interna actualizada en visor, host y servidor para explicar el administrador
local/remoto, carpetas de destino, Enviar/Recibir, selección múltiple, permisos,
conflictos, progreso, verificación y reautorización del destino tras desconectar.
Retiradas las indicaciones antiguas de arrastrar archivos y de reanudar cualquier
destino automáticamente.

Nuevo selector: Windows, azul, verde, turquesa, violeta, rosa, naranja y rojo.
En visor/host hay un botón junto al tema y un combo en Apariencia (incluida la
configuración compacta del host). En servidor está junto al combo de tema.
Preferencia AccentColor independiente por aplicación, persistente y compatible
con archivos de ajustes anteriores. Aplicación inmediata, tonos claro/oscuro y
texto contrastado. No cambia colores semánticos de estado/advertencia/error ni
las burbujas del chat. Recursos específicos de alto contraste usan Windows;
la prueba física de alto contraste y cambios de color del SO sigue pendiente.

Paleta pura en Core y código WinUI compartido compilado en cada EXE independiente;
el agente sin UI no necesita selector. En servidor guardar apariencia no cambia
registro de inicio automático ni aplica campos de red/servicio pendientes.

Verificación: builds Debug/Release sin errores/advertencias, 205/205 tests en cada
configuración. 33/33 controles sobre UIs reales por configuración (visor 12,
host 12, servidor 9), incluidos cambio visible fuera de la muestra, tema,
persistencia, reinicio, menú/combo sincronizados y nueva ayuda. Evidencia:
`artifacts/diagnostics/accent-{viewer,host,server}-{debug,release}.json` y PNG.
Las capturas finales usan PrintWindow y el HWND raíz, no una región del escritorio
que pudiera estar tapada por otras ventanas. Regresiones Release: archivos 7/7
y fullscreen/ventana/cierre 15/15 (`ui-accent-*-regression.json`).

Para probar el servidor sin cerrar la ventana instalada se añadió el mismo
interruptor diagnóstico CSIVIEWER_ALLOW_MULTI del visor/host, condicionado aquí
a un CSIVIEWER_SERVER_DATA_DIR explícito. Solo se usaron datos temporales. No se
pararon servicios, TeamViewer ni aplicaciones instaladas.

Paquetes de esta entrega (13/11/2026), antes de la petición posterior de idiomas:

| Archivo en artifacts/installers | Hora | SHA-256 |
|---|---|---|
| CSIViewer-Setup-1.0.1.exe | 19:53:27 | 006ED6A9A22AFB7ED297ED15D61F31A56BB3D7901DFECE1B4F3CBAA0B8BB9878 |
| CSIViewer.Host-Setup-1.0.1.exe | 19:54:38 | 54655ECBEC0D232F15D7344743BA09C6E537EDF8901CBFBE787602FA07D698B1 |
| CSIViewerServidor-Setup-1.0.1.exe | 19:55:33 | 0253351A5A85592CBB26D33C24A65850B9CF5840EC4E43082FCE3910CE6F95B4 |
