# Host: chat temporal, consentimiento y casillas — 1.0.1

Esta revisión sustituye el historial persistente del host descrito en el informe 26.
El visor mantiene su historial por dispositivo, exportación y borrado manual.

## Cambios

- El host usa `ChatHistoryOptions.RetainHistory=false`, registrado solo en su
  contenedor de dependencias. Las demás aplicaciones conservan el valor true.
- Los mensajes del host solo viven en memoria durante la sesión. No se escriben
  en el repositorio, no se cargan conversaciones antiguas y se vacían al terminar
  la sesión por cualquiera de los extremos o perder la conexión.
- La inicialización del host elimina las conversaciones locales heredadas de
  versiones previas. No afecta a los datos del visor. No implica borrado forense
  de páginas SQLite o copias externas.
- El fin de sesión invalida publicaciones pendientes. Un mensaje tardío de una
  sesión eliminada no puede volver a poblar el chat temporal.
- El host oculta selector/exportar/borrar, elimina borradores al salir de la
  sesión y deshabilita el acceso al chat sin sesión con permiso Chat.
- La ayuda del host explica expresamente el chat temporal, el historial del visor
  y la restauración de la ventana para aprobar conexiones.
- Si se requiere consentimiento manual, el host restaura su ventana minimizada
  o muestra la oculta antes de presentar los permisos. No cambia los modos de
  aceptar/rechazar automáticamente ni concede permisos por mostrar la ventana.
  Se respetan las restricciones de activación y escritorio seguro de Windows.
- El usuario aclara que el visor también puede recibir conexiones: se aplica
  Restore/Show/Activate también en ConsentService del visor, manteniendo su
  historial y sin conceder permisos automáticamente. UI entrante visor: 8/8
  comprobaciones aprobadas (minimizar, restaurar, esperar consentimiento,
  recibir chat y conservarlo al finalizar).
- Audio/Portapapeles de la barra: `VerticalContentAlignment=Center`, sin ancho
  mínimo nativo de 120 DIP y con 8 DIP de separación entre casilla y texto.

## Validación

- Primer Debug: 255/255 pruebas y 31/31 comprobaciones de sesión real cifrada,
  incluyendo dos rondas de chat temporal (corte por visor y por host), nueva
  sesión vacía y conservación del historial en visor.
- Añadidas regresiones para no persistir/cargar, descartar publicaciones tardías,
  mantener historial del visor, ocultar acciones solo en host y espacio/alineación
  de las casillas. La advertencia CA1068 del primer build se corrigió colocando
  CancellationToken al final de la firma.
- Debug y Release finales sin advertencias/errores: 255/255 pruebas en cada
  configuración. Catálogos es/en/fr/ca
  completos con 942 entradas y placeholders conservados.
- Host UI nativa Debug y Release: 26/26 comprobaciones en cada una. Dos conexiones reales con host
  minimizado: se restaura, muestra permisos y espera aprobación. Selector y
  exportar/borrar no aparecen. Mensaje visible durante sesión, borrador y mensajes
  vaciados tras corte remoto, y botón Chat deshabilitado tras ambos cortes.
- El primer diagnóstico de consentimiento falló por codificación del harness
  PowerShell 5 sin BOM: la ventana sí estaba visible. Guardar el script externo
  como UTF-8 con BOM corrigió la búsqueda de nombres acentuados.
- Barra y emojis visor Debug: 8/8; captura real con separación y centrado:
  `artifacts/diagnostics/chat-emoji-viewer-release.json.feature-checkbox-alignment.png`
  (el sufijo release corresponde al probe, en este ensayo el visor era Debug).
- UI entrante visor Release también pasa 8/8; barra/emojis Release 8/8.
- Historial visor Release: 25/25 en
  `temporary-host-viewer-history-release-retry.json`. El primer intento no generó
  archivo tras el clic nativo Guardar; se mantuvo abierto el picker sin error de
  aplicación. La repetición completa, sin cambiar producción, exportó los 601
  mensajes y verificó borrado, persistencia y permisos previos a conexión.
- Los informes anteriores de UI del host con historial ya no son contratos de
  esta revisión. Publicación final y tanda completa de paquetes en ejecución.

No se instalan aplicaciones ni se modifica el servidor instalado. Se conserva
la tarea SYSTEM del host y el ZIP original 1.0.0. No se hace commit ni push.

## Tanda completa solicitada por el usuario

El usuario pide regenerar TODOS los instaladores con la misma fecha. Se publican
secuencialmente visor, host UI, host agent y servidor, y se compilan los tres
instaladores 1.0.1. Al finalizar se asigna a los tres EXE la misma marca de
modificación de la tanda; no se modifica la firma ni el contenido por esa marca.
El manifiesto final se guarda en
`artifacts/diagnostics/installers-complete-batch-1.0.1.json`.

El payload del servidor contenía un log diagnóstico antiguo. No se elimina ni
se lee su contenido para empaquetar; `server.iss` ahora excluye directorios data/logs,
bases de datos, settings de ejecución, logs y PDB. El servidor instalado sigue
intacto; regenerar su instalador no ejecuta servicio ni instalación.

### Resultado final

Los tres instaladores se generaron y sus hashes se verificaron. Fecha/hora de
modificación común: **14/11/2026 00:51:35 +01:00**, versión **1.0.1.0**.
La publicación del servidor necesitó reintentos de copia por coincidir con un
servidor QA temporal; finalizó correctamente sin intervenir en el instalado.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | 383ECAEE3E197212FC8DBF8C1D88618985D701DBB062CF84377DA998DA0A6591 |
| CSIViewer.Host-Setup-1.0.1.exe | CE9AE36F0A419D6209A80F9D648B77BE24EC27221835F849BC04815188A9A450 |
| CSIViewerServidor-Setup-1.0.1.exe | DE03210934E759E93148446B31572C491FB497CC7708CC18249015470E1715C5 |
