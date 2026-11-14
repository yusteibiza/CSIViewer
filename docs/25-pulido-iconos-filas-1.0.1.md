# Iconos, espaciado y filas — 1.0.1

## Cambios de presentación

Aplicados en visor y host, sin cambiar navegación, permisos, conexión ni
funcionamiento de audio/portapapeles:

- **Archivos:** Subir (flecha arriba), Ir (flecha derecha) y Actualizar (recarga)
  son botones FontIcon de 32 DIP. Unidades conserva texto. Tooltips y nombres
  accesibles usan las traducciones existentes; se mantienen handlers e IDs de Go/Refresh.
- **Barra de sesión:** Audio y Portapapeles ajustan su ancho al contenido.
  El CheckBox de WinUI reserva 120 DIP por defecto: se elimina ese mínimo solo
  en esas dos casillas y se centra verticalmente su contenido.
- **Dispositivos:** CASA y otras listas/favoritos pasan de la primera línea de
  metadatos a una columna centrada respecto a toda la fila y los botones.
  El contenedor estira el contenido horizontalmente.
- **Hover:** fila completa con SubtleFillColorSecondaryBrush, respetando el tema
  y sin selección permanente. RowHover comparte implementación entre aplicaciones,
  actualiza el recurso si cambia el tema y restaura el fondo al salir/descargar.
  No se conecta automáticamente al hacer clic en la fila.

## Verificación

- Debug aislado compila sin advertencias/errores.
- `artifacts/diagnostics/layout-polish-viewer-debug.json`: **21/21 comprobaciones UI**.
- Dos dispositivos y lista CASA creados en datos temporales, sin contraseñas.
  Tooltips/nombres de seis iconos, tamaño compacto y Unidades textual comprobados.
  Navegación local Ir/Subir/Actualizar verificada con carpetas temporales.
- Ambos centros verticales CASA/Editar a <=2 px; hover Light y Dark verificado
  comparando píxeles antes/después en capturas PrintWindow reales.
- Los primeros ensayos detectaron el hover ausente con SelectionMode=None.
  Se añadió RowHover. La prueba necesitó movimiento SendInput no coalescido y
  sacar el cursor a una zona cliente de cabecera (no a la barra de título).
  Los ensayos anteriores se conservan como diagnóstico, no como aprobados.
- Se añaden seis regresiones de contratos XAML embebidos para visor/host:
  iconos accesibles, ancho mínimo de casillas y posición/tema de etiquetas.
- Debug/Release pasan **242/242 tests**, compilaciones sin avisos/errores.
- Visor publicado pasa también **21/21 comprobaciones UI**:
  `artifacts/diagnostics/layout-polish-viewer-published.json`.
- Instaladores visor/host regenerados correctamente; servidor sin cambios.

## Instaladores

En `artifacts/installers`, versión 1.0.1. Manifiesto completo:
`artifacts/diagnostics/layout-polish-installers.json`.

| Paquete | Hora 13/11/2026 | SHA-256 |
| --- | --- | --- |
| CSIViewer-Setup-1.0.1.exe | 23:25:34 | FB1F5F4A089EA4D0F66A1615E1BCB8E453DD58B63C3ED0E6F263333DBEB23A1B |
| CSIViewer.Host-Setup-1.0.1.exe | 23:26:46 | 44A4E05BBEFBEB364FE078F03D2D6545FF55A02590101E0568A8681A066AA955 |

El instalador del servidor permanece en 22:37:22 con el mismo hash del informe
24, porque esta revisión solo cambia presentación de visor/host. No se ejecutó
ningún instalador. Los payloads no incluyen datos de usuario y el hash original
del ZIP 1.0.0 volvió a verificarse sin cambios.

## Contexto confirmado por el usuario

El audio entre equipos casa/oficina se oye perfectamente al usar un tercer
ordenador para TeamViewer. El bucle inicial aparecía porque el mismo ordenador
de casa era host de CSIViewer y cliente de TeamViewer hacia la oficina. No se
cambiaron audio ni coordenadas para ocultar ese circuito de realimentación.

No se han actualizado instalaciones ni alterado el servidor. El ZIP 1.0.0 permanece.
