# Contactos: espaciado legible y franja de cabecera — 1.0.1

## Petición

La fila anterior resultaba demasiado apretada; el rótulo «Equipos vinculados»
estaba pegado al botón. Se pide distinguir los botones de equipos/listas por
color y corregir una franja azul bajo la cabecera.

## Cambios

- Fila con padding10 y columnas separadas16, conservando la distribución compacta
  de tres columnas sin apilar los equipos debajo de las acciones.
- Datos separados4; rótulo con margen superior6 e inferior4, además del espaciado
  del panel. Botones de equipos mínimo32, fuente13, padding12,5 y separación8.
- Equipos con fondo verde suave y texto verde contrastado, según tema. Recursos
  HighContrast usan colores de botón del sistema. Se preserva el hover nativo.
- Editar/Eliminar mantienen mismo ancho y separación8 entre ambos.
- RootGrid de la ventana con PageBackgroundBrush, cubriendo también el hueco
  que deja SessionBanner cerrado con margen inferior y donde asomaba Mica.
- Aplicado a visor y host. No cambia persistencia, permisos ni conexión.

## Validación

Contratos XAML actualizados para espaciado, colores temáticos y fondo raíz.
Debug/Release sin advertencias/errores y 281/281 pruebas en cada configuración.
UI Release aislada: 19/19 comprobaciones de separación real, colores de botones,
hover y píxeles de cabecera/menú en Light/Dark. Fila sintética157/156px; más aire
sin volver a la antigua disposición de dos filas independientes.
Cabecera/menú RGB243,243,243 en claro y32,32,32 en oscuro. Capturas revisadas.
Informe `artifacts/diagnostics/contacts-spacing-viewer-release.json`.

Publicación de cuatro payloads y tres instaladores terminada. A petición del
usuario, la entrega final permanece directamente en `artifacts/installers/`,
sin subcarpetas para las entregas futuras. Los tres archivos se copiaron y sus
hashes se verificaron, con fecha común **14/11/2026 11:40:22 +01:00** y versión
1.0.1.0. Manifiesto final
`artifacts/diagnostics/contacts-spacing-installers-1.0.1.json`.
No se instala nada ni se modifica el servidor del usuario.
