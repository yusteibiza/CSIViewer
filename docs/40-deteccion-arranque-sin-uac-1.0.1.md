# Detección del arranque configurado por el instalador — 1.0.1

## Causa confirmada mediante lectura

Tras instalar con inicio desatendido, el agente está ejecutándose pero el panel
muestra estado inaccesible. La consulta COM real a `CSIViewerHost` desde el
usuario sin elevación devuelve **0x80070005, E_ACCESSDENIED**. El CIM no devuelve
resultados: esto no demuestra ausencia de tarea (aclaración de la inspección39).
No se elevó ni se cambió la tarea para obtener estos datos.

La instalación creaba la tarea SYSTEM con la DACL administrativa predeterminada,
que no permite consultar su estado desde la UI sin elevar. El error estaba en
la provisión de permisos de lectura, no en la activación realizada por el usuario.

## Cambios

- Registro desde instalador o panel aplica, mediante la API oficial
  `RegisteredTask.SetSecurityDescriptor`, la DACL de la **única tarea del host**:
  `D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)`.
- SYSTEM y Administradores mantienen control total; Usuarios únicamente lectura
  (`0x120089`). No se concede ejecutar, modificar, borrar ni cambiar permisos.
  No se modifica la carpeta raíz del Programador ni ninguna otra tarea.
- Se relee y comprueba el permiso antes de iniciar/declarar la activación.
- El instalador controla ahora el código de salida de la operación; si falla
  presenta un error y no oculta el fallo tras una instalación aparentemente correcta.
- Actualizar sin marcar arranque repara solo lectura de una tarea preexistente
  del agente SYSTEM esperado. No crea tareas, ni habilita una tarea deshabilitada.
- Estado desconocido no habilita Activar ni Desactivar. Ready/Running muestran
  Activado/Activado(en ejecución), con Activar inactivo y Desactivar activo.
- Texto de estado envuelto en una columna limitada; se elimina el recorte visto
  en la captura. Mismo ajuste en visor y host independiente.
- Consulta normal no pide UAC; se mantiene la consulta administrativa explícita
  como recuperación para tareas anteriores a esta corrección.

La corrección se aplicará al **instalar la nueva entrega**: no se han cambiado
los permisos ni la tarea instalados durante el desarrollo. El usuario puede
actualizar solo el host; no necesita reinstalar/interrumpir su servidor.

## Validación y entrega

Compilación Release, publicación y empaquetado; **no pruebas, no instalaciones,
no reinicios, no activaciones/desactivaciones reales** por instrucción del usuario.
La lectura sin elevación tras instalar la corrección queda pendiente del usuario,
igual que Winlogon real.

Diagnóstico sin secretos:
`artifacts/diagnostics/host-task-query-access-1.0.1.json`.
Tres instaladores1.0.1.0 directamente en `artifacts/installers/`, marca común y
SHA-256 en `artifacts/diagnostics/task-read-status-installers-1.0.1.json`.

Entrega final: **14/11/2026 20:01:30 +01:00**. Release sin errores/advertencias,
cinco payloads y tres instaladores terminados; catálogos actualizados (973claves
base). No se ejecutaron pruebas ni instaladores.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | 9F7EA2E9727F2726C1C938E3FB0C932A0455DAF8DE151809C635F7ED98379475 |
| CSIViewer.Host-Setup-1.0.1.exe | 00A6B1701267E408D30C7C23AD8982175D330A03698D85D9876A5C696133ED23 |
| CSIViewerServidor-Setup-1.0.1.exe | 7CD853A1AB60F1CC68968B5791AA3F450B166A707A9D39134838A41814F83E6E |

Referencia Microsoft:
https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks
