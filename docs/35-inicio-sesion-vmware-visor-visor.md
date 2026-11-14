# Inicio de sesión de Windows y revisión visor → visor

## Estado y autorización

El usuario solicita poder escribir la contraseña de Windows tras reiniciar y
revisar también visor → visor. Confirma disponer de una VM **VMware**. No ha
Confirmó después **Windows 10**. Faltan nombre de la VM y canal de ejecución en ella.
No se reinicia el equipo de trabajo ni se instalan componentes en él.

**El inicio de sesión remoto NO está implementado ni validado por este cambio.**
No presentar un diagnóstico ni una prueba del escritorio normal como soporte
del login, de UAC o de Ctrl+Alt+Supr. No desactivar UAC, escritorio seguro, SAS,
UIPI o directivas para hacerlo funcionar. UIAccess por sí solo no implementa un
host antes de que exista una sesión de usuario.

## Causa arquitectónica

El agente actual es un lanzador SYSTEM mediante la tarea CSIViewerHost. Obtiene
tokens con WTSQueryUserToken y lanza la UI como el usuario en winsta0/default.
Sin usuario autenticado ese camino no proporciona un host operativo de máquina.
Identidad, ajustes y contraseña personalizada pertenecen al perfil del usuario;
copiar blobs DPAPI CurrentUser a SYSTEM no es una migración válida ni segura.

## Requisitos para implementar el soporte

1. Runtime de máquina separado de la UI y configuración explícita por un
   administrador: identidad/credencial CSIViewer de máquina, revocación, rutas con
   ACL solo SYSTEM/Administradores. Nunca guardar la contraseña Windows.
2. Mantener el arranque por la tarea SYSTEM CSIViewerHost; el broker vive fuera
   de la UI del usuario. No cambiar el servicio instalado durante desarrollo.
3. Autenticar primero al visor con el protocolo existente. Solo después conceder
   un ticket efímero ligado a sesión, permisos efectivos, propietario del control,
   sesión Windows y caducidad. Prohibir comandos/rutas/ejecutables arbitrarios.
4. Worker de captura/entrada con derechos legítimos para la sesión interactiva;
   IPC local con ACL restringida, autenticación de extremos y límites de tamaño.
   No ampliar ACL de escritorios ni usar acceso del proceso UI para saltarse UIPI.
5. Gestionar arranque sin logon, bloqueo/desbloqueo, cambio de usuario, cierre de
   sesión y desaparición del worker; fallar cerrado y liberar todas las teclas.
6. SAS solo mediante mecanismos soportados y configuración explícita compatible
   con Windows. No simular Ctrl+Alt+Supr con SendInput ni cambiar políticas en
   silencio. Teclado/layout, PIN/contraseña y monitor deben verificarse en VM.
7. No permitir archivos, portapapeles ni audio de un contexto SYSTEM al escritorio
   de login. No reutilizar implícitamente sesiones/consentimiento entre usuarios.
8. Identidad estable y transición a la UI sin duplicar conexiones al servidor ni
   perder revocación. Contraseñas/teclas nunca en logs.

## Diagnóstico preparado para VMware

Script `scripts/test-login-vmware.ps1`, solo para ejecutar **dentro de la VM**.
Requiere `-ConfirmVm` y comprueba que Manufacturer/Model sea VMware. No captura
pantalla, envía entrada, lee contraseñas ni modifica instalaciones de CSIViewer.

Primera prueba como usuario en PowerShell dentro de la VM:

```powershell
.\test-login-vmware.ps1 -ConfirmVm -Minutes 3
```

Mientras recoge datos, bloquear la VM desde su consola y desbloquearla localmente.
Informe en `%LOCALAPPDATA%\CSIViewerLoginDiagnostic\`. Solo versión/build de
Windows, IDs/estados de sesiones y accesibilidad/nombre del escritorio; sin nombres
de usuario, texto tecleado, pantallas ni credenciales.

Para observar el arranque antes del login, opcional y **solo en VM**, tomar antes
una instantánea. PowerShell como administrador:

```powershell
.\test-login-vmware.ps1 -Mode InstallBootProbe -ConfirmVm -Minutes 5
```

Prepara una tarea temporal distinta, `CSIViewerLoginDiagnostic`, sin ejecutarla
ni reiniciar automáticamente. Script/reportes SYSTEM bajo ProgramData con ACL
solo SYSTEM/Administradores. Reiniciar manualmente desde la consola VMware y
esperar unos segundos antes de iniciar sesión. Informes bajo
`%ProgramData%\CSIViewerLoginDiagnostic\`.

Quitar la tarea tras recoger los informes (conserva archivos para revisión):

```powershell
.\test-login-vmware.ps1 -Mode RemoveBootProbe -ConfirmVm
```

La sesión0 no comparte el escritorio interactivo: un error OpenInputDesktop en
SYSTEM sesión0 no demuestra por sí solo qué podrá hacer un worker en sesión1.
Este script sirve de línea base, **no habilita control del login**.

## Visor receptor

Se amplía FeatureProbe con `--ui-controller-control`: solicita pantalla/control/
chat al visor WinUI real. El harness espera consentimiento manual explícito,
comprueba restauración desde minimizado y vídeo, y activa una ventana desechable
para verificar clic, texto sintético y arrastre mediante la sesión cifrada.
Solo envía entrada cuando la ventana de prueba posee el foco y a coordenadas
dentro de ella. No se prueba sobre ventanas del usuario ni sobre escritorio seguro.

El emisor en esta prueba es el cliente compartido ejecutado por FeatureProbe,
no una segunda ventana WinUI: no confundir con una prueba física en dos equipos.

Se corrige además un riesgo de registro: el receptor redacta la acción de entrada
de tipo `text`, igual que el emisor, para no registrar texto/contraseñas inyectadas.
La prueba verifica que su texto sintético no aparezca en los logs del receptor.

Validación nativa del receptor Debug y Release: **17/17** en cada uno, vídeo/clic/texto/arrastre reales
en fixture, restauración/consentimiento, fin de sesión, historia y redacción de
texto en logs. Informe `artifacts/diagnostics/viewer-incoming-control-release.json`
(la última ejecución corresponde al receptor Release; antes se verificó Debug).
Emisor 6/6 en el informe `.peer.json`. Primeros intentos fallaron por selección de
patrón UI/variable HOME reservada y conversión JSON de PowerShell5, no por la
entrada del producto; corregidos en el harness.

Script diagnóstico: sintaxis PowerShell y C# embebido compilados, sin ejecutar
prueba/tarea alguna en el equipo actual. Paquete para copiar dentro de la VM:
`artifacts/diagnostics/CSIViewer-Login-Windows10-VMware-diagnostico.zip`.
Contiene solo el script y las instrucciones. Pendientes informes de esa VM.

Debug y Release limpios287/287 cada uno, incluidas4 pruebas de redacción de entrada.
Repetición UI con receptorRelease aprobada17/17. No generar instaladores que se anuncien como
soporte de login hasta completar implementación, revisión de seguridad y matriz
real en VM. Los últimos instaladores entregados siguen siendo los de11:49:06.

## Resultado del primer diagnóstico del usuario

Archivo `C:\Users\AntonioYuste\Desktop\login-probe-20261114-121106-9988.jsonl`:
Windows10Pro build19045, proceso usuario/sesión1;42 muestras Default accesible y
2 muestras OpenInputDesktop denegado/Error5. No demuestra captura/control Winlogon.

Prueba boot: tarea sí se lanzó a12:14:14, LastTaskResult=1, sin JSONL. Usuario
CurrentUser RemoteSigned; MachinePolicy/UserPolicy/Process/LocalMachine Undefined.
No atribuir automáticamente el1 a la política: puede fallar antes de escribir.

Script corregido con RepairBootProbe, validación de propiedad/ACL/reparse points,
lanzador cmd que registra stdout/stderr/exitcode desde antes de PowerShell.
RemoteSigned explícito SOLO para el proceso diagnóstico; sin Set-ExecutionPolicy,
Bypass ni cambios permanentes. Políticas de grupo siguen prevaleciendo.
Se prepara ZIP actualizado con instrucciones de reparar y lanzar sin reiniciar,
para obtener `boot-startup.log` primero. No se ejecuta el diagnóstico en equipo real.

Segunda incidencia: el ACL informado por el usuario contiene SYSTEM,
Administradores y WINDOWS10-VM\yuste, todos Allow FullControl explícitos.
La guardia anterior rechazaba la entrada del creador. RepairBootProbe ahora la
acepta SOLO para el administrador actual con propietario él mismo o grupo
Administradores; rechaza otros usuarios/propietarios. Aplica DACL exacta y owner
Administradores en carpeta y archivos ejecutados por SYSTEM. No toca informes
anteriores ni rutas fuera del diagnóstico. Opción RunNow evita otro comando
pegado por error: repara e inicia solo esa tarea, sin reiniciar.

Pruebas de empaquetado con tareas/ACL simuladas y scripts sintéticos ejecutados
por WindowsPowerShell5.1:14/14. Ninguna tarea/ACL real del equipo actual cambiada.
ZIP actualizado12:46:56, SHA256
5010F648A3D5C61241B0AFD43ADB4C453ABA316E50B1B374D6C1804B45ADFE13.

## Diagnóstico SYSTEM recibido tras la reparación

`boot-startup.log` del usuario termina ExitCode=0. Informe
`C:\Users\AntonioYuste\Desktop\login-probe-20261114-124802-8252.jsonl`:
Windows10Pro19045, SYSTEM/sesión0,60 muestras entre12:48:02 y12:50:00;
OpenInputDesktop no disponible/Error1 en todas; sesión1 Active con token de
usuario disponible en todas. Es coherente con aislamiento de sesión0 y NO
demuestra un fallo de permisos del usuario ni capacidad para controlar Winlogon.
Esta ejecución manual ocurrió con usuario ya iniciado; sigue pendiente el
informe producido por la tarea al arrancar la VM sin iniciar sesión inicialmente.
No repetir RunNow para esa prueba: dejar actuar el disparador AtStartup.

## Informe recibido tras reiniciar la VM

`C:\Users\AntonioYuste\Desktop\login-probe-20261114-125427-5348.jsonl`:
SYSTEM/sesión0,53 muestras entre12:54:45 y12:56:30; DesktopUnavailable/Error1
en todas, sesión1Active/tokenTRUE en todas. La cabecera comenzó12:54:27 y la
primera muestra llegó18s después. El informe no contiene un indicador de estado
de bloqueo ni demuestra que se tomaran muestras antes del login: WTSActive y
tokenTRUE no distinguen por sí solos bloqueo/desbloqueo ni escritorio seguro.

No pedir más repeticiones del mismo diagnóstico: no mide la capacidad de un
worker en la sesión interactiva. Ya está confirmada en código la dependencia
de WTSQueryUserToken/perfil y el aislamiento de sesión0. El siguiente trabajo
es implementación del runtime/broker/worker y revisión de seguridad descritos
arriba, no más cambios de políticas o del script para forzar entrada. El acceso
remoto al login sigue sin estar implementado; no generar ni anunciar un
instalador como si lo incluyera. No se ha actualizado ninguna instalación.
