# Versiones 1.0.0 y 1.0.1 — 13/11/2026

> Registro de preparación del versionado. El administrador de archivos se implementó
> posteriormente; estado actual e instaladores en `20-administrador-archivos-1.0.1.md`.

## 1.0.0: estado confirmado por el usuario

Antes de cambiar ninguna versión se creó una copia completa del proyecto:

`artifacts/backups/CSIViewer-1.0.0-20261113-183951.zip`

- Incluye fuentes, documentación, tests, herramientas, archivos ocultos,
  compilaciones y los instaladores actuales. Solo se excluye la propia carpeta
  de copias para evitar incluir el archivo mientras se genera.
- 6636 archivos comprobados individualmente por tamaño y SHA-256 al leer el ZIP.
- Manifiesto: `CSIViewer-1.0.0-20261113-183951.manifest.json`, en la misma carpeta.
- SHA-256 del ZIP: `42A9D1E061042870749687B73F96FD79724703369ADFBFCFAD7F52D555520FC1`.
- No se han reemplazado ni eliminado los instaladores 1.0.0 existentes.

Para recuperar el estado, extraer el ZIP en una carpeta vacía, no encima del
desarrollo actual. Guardar también una copia en otro disco para protección ante
fallos de esta unidad. El ZIP puede contener configuración o material diagnóstico
local: no publicarlo como código abierto sin revisar su contenido.

## 1.0.1: desarrollo

Se fija versión común 1.0.1 (ensamblados/archivos 1.0.1.0) y se actualizan los
valores predeterminados del empaquetado. La versión Win32 del instalador deriva
ahora de MyVersion para no conservar por error 1.0.0.0.

Función prevista: administrador de archivos local/remoto con navegación y envío/
recepción, reutilizando transferencia existente y conservando autenticación,
permisos y autorización. Aún no implementada en esta preparación de versión.
No se publican instaladores 1.0.1 hasta incorporar y comprobar esa función.
