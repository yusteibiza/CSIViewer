# Chat por dispositivo y revisión de permisos — 1.0.1

## Cambios

- Visor y host: selector de dispositivo, conversación filtrada y consulta offline.
- Borrado con confirmación: solo elimina el historial local del dispositivo elegido.
  No elimina mensajes en el otro equipo ni cierra la sesión.
- Exportación nativa a texto UTF-8: incluye el historial completo, no solo los
  últimos 500 mensajes cargados en pantalla.
- Borradores separados por dispositivo y envío dirigido al peer seleccionado,
  sin recurrir a otra sesión si ese dispositivo no está conectado.
- Más de 400 emojis Unicode en ocho categorías, renderizados por Windows.
  Se conserva la sangría de 28 px de las burbujas enviadas, alineadas a la izquierda.
- Al navegar desde un dispositivo guardado se conservan ID y contraseña, pero
  se espera a que el usuario revise permisos y pulse Conectar. Audio y portapapeles
  se pueden solicitar antes de conectar. La barra activa se actualiza cuando
  cambian los permisos concedidos; no concede permisos no solicitados.
- Ayuda actualizada y catálogos es/en/fr/ca revisados: 940 entradas por idioma.

## Verificaciones

- Debug y Release finales: 253/253 pruebas en cada configuración, compilaciones
  sin advertencias ni errores, con los catálogos y la ayuda finales.
- Idiomas Release: 18/18 comprobaciones visor y 18/18 host; es/en/fr/ca,
  persistencia, cambio en vivo, comandos dentro de límites en dos anchos y reinicio.
- UI nativa Debug y Release: 25/25 comprobaciones visor y 19/19 host en cada configuración, con bases de datos
  sintéticas aisladas: filtrado, cancelación, exportación de 601 mensajes,
  Unicode, borrado local, reinicio y revisión previa de permisos.
- Sesión real cifrada: 21/21 comprobaciones, incluyendo audio/portapapeles,
  envío dirigido, respuesta host, rechazo de peer desconocido, borrado local
  y envío de nuevos mensajes después de borrar.
- Emojis UI nativa: 8/8 comprobaciones en visor Debug y Release, insertando 🍏 desde
  Comida y bebida y verificando recepción en el peer cifrado.
- Binarios publicados usados por los instaladores: visor 25/25 y host 19/19
  comprobaciones de chat aprobadas; emoji visor 8/8 con recepción cifrada.
  Confirmadas también las casillas Audio/Portapapeles antes de conectar desde
  dispositivo guardado en el visor publicado.
- El error anterior de Invoke era del harness: seleccionaba un contenedor de
  GridView en vez del botón de emoji. El clic nativo validado funciona sin
  modificar el código de producción.
- El intento de reutilizar esa prueba de emojis en host no aplica: su ventana
  oculta el menú de navegación del visor y no expone NavRemote. Se conserva el
  diagnóstico fallido sin contarlo como verificación del host. El chat del host
  y sus acciones offline sí pasan sus 19 comprobaciones; no se afirma prueba
  nativa de envío con emoji desde host (el controlador y catálogo son compartidos).

Los informes se guardan en `artifacts/diagnostics`. Los ensayos fallidos previos
se mantienen como diagnóstico y no se cuentan como aprobados.

## Límites y preservación

No se han actualizado aplicaciones instaladas ni modificado el servidor instalado.
El host conserva su tarea programada SYSTEM, sin cambiar a servicio ni eludir
UIPI/UAC/escritorio seguro. El servidor no necesita cambios para esta revisión.
No se afirma verificación exhaustiva de DPI/alto contraste, sesiones prolongadas
ni de todas las combinaciones de archivos y pantalla completa posteriores al cambio.
El ZIP original 1.0.0 se conserva. No se ha hecho commit ni push.

## Instaladores finales

Publicación secuencial self-contained win-x64 y compilación Inno Setup correctas.
Versiones 1.0.1.0 verificadas; payloads visor/host sin bases de datos, settings,
logs ni PDB. No se ejecutaron instaladores. Manifiesto:
`artifacts/diagnostics/chat-installers-1.0.1.json`.

| Paquete | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | 6107FBAEFF68E93B128718E4A87DEFC47C40BCC2CECA48CA1957FE902200D815 |
| CSIViewer.Host-Setup-1.0.1.exe | B7E2CC81D6379731AF09E4597F61B19D8465C6E14AE7965B216D825CAD0487D4 |

Servidor sin cambios: `FFE2283010D33E96E9AC1F3990DEE5C64AA656A7C5626F37F3E3963DB3CD647B`.
ZIP original 1.0.0 nuevamente verificado:
`42A9D1E061042870749687B73F96FD79724703369ADFBFCFAD7F52D555520FC1`.
