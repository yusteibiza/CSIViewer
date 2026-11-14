# Espaciado uniforme de configuración — 1.0.1

La captura del usuario muestra un hueco excesivo entre las tarjetas superiores
y Acceso al inicio de Windows. La cuadrícula declaraba cuatro filas Auto para
la versión estrecha, pero en escritorio utilizaba solo dos. RowSpacing seguía
reservando separación para las dos filas vacías.

Se ajustan visor y host:

- Dos filas reales en dos columnas; cuatro filas solo en la disposición estrecha.
  Se añaden/eliminan RowDefinitions al cambiar ancho, sin dejar filas vacías.
- Separación horizontal/vertical de tarjetas y bloques uniformada a8 unidades.
- Cabecera con una fila en ancho normal y dos solo cuando los botones bajan de
  línea, evitando otra separación reservada por una fila vacía.
- Se elimina la cuarta columna vacía de la carpeta de descargas, alineando
  Examinar con el borde derecho del contenido.
- Acceso al inicio de Windows y Privacidad siguen en tarjetas de ancho completo.

No se alteran opciones, seguridad, arranque, captura, chat ni lógica de sesión.
Se conservan las mejoras de los informes41 y42. No se ejecutan pruebas visuales,
aplicaciones ni instaladores por instrucción del usuario; solo compilación y
empaquetado Release. No se modifica ninguna instalación o servidor en ejecución.

Tres instaladores1.0.1.0 en raíz `artifacts/installers/`, fecha común y hashes en
`artifacts/diagnostics/settings-spacing-installers-1.0.1.json`.

Entrega final **14/11/2026 20:52:46 +01:00**. Compilación sin errores ni avisos,
cinco payloads y tres instaladores terminados, sin ejecutar pruebas.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | D5978E1CEF864868073E88032D12C5004B13EA6B44D0649A8A3DF5C01B3137B1 |
| CSIViewer.Host-Setup-1.0.1.exe | C3A9BE2482D33ECDC5568E73CD63269DE07739597F5FD8B6427F0E025A9D90A9 |
| CSIViewerServidor-Setup-1.0.1.exe | 00F658B54EBA2B46F15F52CFBF55EF961457D14C8D1C0FCFB4AF31F2B5C24A9B |
