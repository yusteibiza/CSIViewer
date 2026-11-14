# Configuración global y estado de arranque — 1.0.1

## Interfaz común

Visor, host independiente, configuración de host del visor y servidor tienen
tres botones en la cabecera, alineados a la derecha y adaptables al ancho:

- **Valores por defecto**: carga los valores predeterminados de los campos
  editables. No guarda, no borra datos ni cambia identidad o credenciales.
- **Restablecer**: descarta el borrador y recupera los últimos valores guardados.
- **Guardar configuración**: valida y guarda todos los campos de esa página.

Tema, énfasis, idioma, consentimiento, perfil de calidad y nivel de logs dejan
de aplicarse/guardarse automáticamente desde estas páginas. Solo se aplican al
guardar. Se eliminan los botones de guardado de cada sección.

El borrador reside en los controles; los ajustes compartidos se clonan antes de
guardar. Tras persistir se actualiza el estado compartido. Los cambios de
servidor del visor/host reconectan; la calidad se aplica a la captura activa.
Un fallo de aplicación posterior al guardado se muestra como advertencia, no
como si la persistencia hubiese fallado.

Probar conexión utiliza la URL y autorización HTTP del borrador sin guardarlo.
Se mantienen como acciones independientes probar, elegir carpeta, generar
certificado, gestionar tareas/servicios y borrar datos. Restablecer/valores por
defecto no ejecutan ninguna de esas acciones ni revierten sus efectos.

En el servidor, iniciar y crear servicio utilizan la configuración **guardada**,
no guardan silenciosamente el borrador. Guardar no reinicia un servidor activo:
los cambios de red se usan en su próximo inicio. La colocación de ventana sigue
guardándose sin incorporar los cambios pendientes de los controles.

## Arranque del host

Se enlaza `src/Shared/UnattendedServiceManager.cs` en host y visor. Las fuentes
antiguas se conservan pero se excluyen de compilación. Ambos administran la
tarea SYSTEM `CSIViewerHost` que ejecuta `CSIViewer.Host.Agent.exe`.

Se consulta el estado numérico de Task Scheduler mediante COM; acceso denegado
ya no se presenta como ausencia. Actualizar puede pedir elevación para una
consulta de solo lectura. El último estado verificado se identifica expresamente
cuando no se puede volver a consultar. Los botones usan estados tipados y no
comparaciones con textos traducidos. Las operaciones elevadas verifican el
resultado y lo devuelven por código de salida, sin archivos temporales elevados.
No se declara éxito si no se confirma la creación/eliminación de la tarea.

## Entrega

Se compila y empaqueta Release; **no se ejecutan pruebas ni instaladores** por
instrucción del usuario. No se consulta ni modifica la tarea instalada, ni se
modifica el servidor en ejecución. La validación real de Winlogon sigue pendiente.
Se conserva la copia original 1.0.0.

Los tres instaladores 1.0.1.0 se entregan directamente en `artifacts/installers/`
con una misma fecha de modificación. Fecha y SHA-256 de esta entrega en
`artifacts/diagnostics/global-settings-installers-1.0.1.json`.

Entrega final: **14/11/2026 16:10:31 +01:00**, compilación Release sin errores ni
advertencias y cinco payloads publicados. No se han ejecutado pruebas, sondas
ni instaladores. Catálogos es/en/fr/ca actualizados, 975 claves base.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | EFD36D2D61319E6F8410B65BCC93302F15BE31070AD36AB9E7EBB64DD60B262E |
| CSIViewer.Host-Setup-1.0.1.exe | BDA1523C2D66987A30947881478E498B558D472C8764832A3B13F2A3B7249F32 |
| CSIViewerServidor-Setup-1.0.1.exe | 668561566000ACD643E916AF99B7265AE2463A308FD579D2ED6D6E2A2964CE98 |
