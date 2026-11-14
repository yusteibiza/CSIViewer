# Contactos: etiqueta de equipos y filas compactas — 1.0.1

## Cambios

- Encima de los botones de conexión aparece **Equipos vinculados**, porque sus
  nombres corresponden a dispositivos, no a listas.
- Avatar, datos/equipos y acciones pasan a tres columnas en una sola fila. Los
  equipos ya no ocupan una segunda fila debajo de la altura de Editar/Eliminar.
- Avatar de 32 DIP, padding de fila de 6 DIP, botones de 28 DIP mínimos y menos
  separación. Editar y Eliminar mantienen el mismo ancho y alineación vertical.
- Se conservan hover temático, favoritos, datos ampliados, edición y selección
  múltiple. Nombre con elipsis; detalles y notas limitados a dos líneas, con
  tooltip del texto completo. Los equipos siguen ajustándose a varias líneas
  cuando realmente son muchos, sin una altura fija que los corte.
- Aplicado a las vistas de Contactos de visor y host, con traducciones es/en/fr/ca.

## Validación

- Debug y Release sin advertencias/errores: 271/271 pruebas en cada una.
- UI Debug y Release: 13/13 comprobaciones en cada una, Light/Dark, etiqueta encima de los botones y
  hover real. La fila sintética con notas y dos equipos mide 123/124 px en el
  entorno de prueba; no se fija una altura que pueda cortar otros contenidos.
- Captura Light revisada: nombre y equipos juntos, acciones iguales y centradas,
  sin la antigua separación vertical de dos filas.
- Catálogos completos es/en/fr/ca: 965 claves por idioma.
- Publicación/empaquetado terminado; manifiesto final
  `artifacts/diagnostics/contacts-compact-installers-1.0.1.json`.

Se añaden dos contratos XAML para comprobar que etiqueta y equipos comparten la
columna de datos, y que las acciones ya no fuerzan una fila superior independiente.
El harness nativo comprueba hover Light/Dark, etiqueta sobre los botones y altura
de la fila sintética. No se modifica persistencia, navegación ni conexión.

Los instaladores de 10:57:01 son anteriores a este ajuste. No se instalan
aplicaciones ni se modifica el servidor en ejecución. ZIP original conservado.

## Instaladores finales

Versión 1.0.1.0, fecha/hora común **14/11/2026 11:10:42 +01:00**, hashes verificados.
No se ejecutaron instaladores.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | EB3D8AFC332A5DDB253F3BA306FA58B81E4195AD410BBE59696B32CAC0AC6B4F |
| CSIViewer.Host-Setup-1.0.1.exe | 9926A38BBEEFF579776A2B79E39536638BB6D1022DF3EE4319109E790EF0F389 |
| CSIViewerServidor-Setup-1.0.1.exe | 91F7DAD79D1A624CBBA416B10549B8DD527B974D2F2D7A53C36D902F860636CF |
