# Prueba de inicio de sesión — VMware / Windows 10

**Este paquete es solo diagnóstico. Todavía no permite controlar el login.**
No instala ni actualiza CSIViewer. No guarda contraseñas, capturas ni teclas.

1. Copia el ZIP dentro de la VM Windows 10 y extráelo.
2. Abre PowerShell en esa carpeta, **dentro de la VM**, y ejecuta:

   ```powershell
   .\test-login-vmware.ps1 -ConfirmVm -Minutes 3
   ```

3. Durante esos tres minutos, bloquea Windows con Win+L desde la consola VMware,
   espera unos segundos y desbloquéalo **localmente** en esa consola.
4. Al terminar, recoge los `.jsonl` de:
   `%LOCALAPPDATA%\CSIViewerLoginDiagnostic\`.

Si la política de la VM impide ejecutar el script, envía el error: no desactives
UAC, antivirus, escritorio seguro ni directivas para forzar la prueba.

## Arranque sin usuario (opcional)

Haz antes una instantánea de la VM. Desde PowerShell **como administrador en la
VM**, prepara el diagnóstico de arranque:

```powershell
.\test-login-vmware.ps1 -Mode InstallBootProbe -ConfirmVm -Minutes 5
```

Crea solo la tarea temporal `CSIViewerLoginDiagnostic`, no modifica la tarea
`CSIViewerHost`. No reinicia automáticamente. Reinicia tú la VM desde su consola,
espera 30 segundos en la pantalla de login y después inicia sesión localmente.

Recoge los informes `.jsonl` de `%ProgramData%\CSIViewerLoginDiagnostic\` como
administrador. No incluyas logs o archivos de credenciales de las instalaciones
de CSIViewer, solo los informes de este diagnóstico.

Quita la tarea temporal al terminar:

```powershell
.\test-login-vmware.ps1 -Mode RemoveBootProbe -ConfirmVm
```

Los informes permanecen disponibles para revisar. El script no reinicia,
desbloquea, inyecta entrada ni cambia permisos de escritorios Windows.

## Si la tarea anterior terminó con resultado 1 y no creó informes

Resultado1 es un error genérico; no identifica la causa. La política
CurrentUser=RemoteSigned del usuario no se aplica a SYSTEM. La revisión del
paquete añade un lanzador que guarda también los errores de arranque de PowerShell
en `boot-startup.log` y selecciona RemoteSigned solo para ese proceso, sin cambiar
políticas persistentes ni eludir MachinePolicy/UserPolicy. No usa Bypass.

Vuelve a copiar y extraer el ZIP actualizado dentro de la VM, reemplazando la
copia anterior. En PowerShell como administrador, desde esa carpeta:

```powershell
.\test-login-vmware.ps1 -Mode RepairBootProbe -ConfirmVm -Minutes 2 -RunNow
```

No hace falta reiniciar para esta comprobación del diagnóstico. Espera dos
minutos y envía `C:\ProgramData\CSIViewerLoginDiagnostic\boot-startup.log` y los
`.jsonl` que aparezcan en esa carpeta. Si falla antes, el log debe indicar por qué.
La reparación solo acepta la tarea propia y la carpeta con permisos protegidos;
no elimina informes, no modifica CSIViewerHost y no detiene tareas en ejecución.

La revisión actual reconoce también la entrada explícita del creador que dejó
la primera instalación, pero solo si coincide con el usuario administrador
actual y la carpeta pertenece a ese usuario o a Administradores. Rechaza otras
cuentas o propietarios desconocidos. Al reparar fija el
propietario en Administradores y protege carpeta/script/lanzador con acceso
SYSTEM/Administradores; no modifica permisos fuera del diagnóstico.
