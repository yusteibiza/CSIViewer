# Host de inicio de Windows — implementación experimental VMware

**Este paquete contiene un host nuevo, no el diagnóstico anterior.** Está
implementado para probar acceso al escritorio de login, pero todavía NO se ha
validado ese acceso en una VM real ni debe considerarse equivalente a TeamViewer.
Solo funciona en una VM VMware configurada explícitamente por un administrador.

## Antes de empezar

- Haz una instantánea de la VM Windows10 (build19041 o posterior).
- Conserva acceso a la consola VMware para recuperar la VM.
- La nueva contraseña CSIViewer permite controlar el equipo con un componente
  SYSTEM: trátala como una credencial de administración y no la compartas.
- No instales este paquete en el equipo principal. El ejecutable también rechaza
  ejecución fuera de VMware y rutas no protegidas.
- Necesitas la URL y el código de inscripción de tu servidor CSIViewer. No hay
  que reinstalar ni modificar el servidor. HTTPS es obligatorio salvo HTTP en
  red privada/local admitido por el cliente actual.
- El visor normal instalado en otro ordenador sirve para la prueba.

## Instalar dentro de la VM

1. Copia y extrae **el nuevo ZIP de Login Lab**, no el ZIP de diagnóstico.
2. Abre PowerShell **como administrador en la VM**, dentro de esa carpeta:

   ```powershell
   .\install-login-lab.ps1 -Mode Install -ConfirmVm
   ```

3. Lee el aviso y escribe `ACTIVAR`. El asistente solicitará:
   - URL del servidor CSIViewer.
   - Código de inscripción del servidor, con entrada oculta.
   - Una **nueva contraseña de acceso remoto CSIViewer**, de12 a64 caracteres,
     y su confirmación. Usa una distinta de la contraseña de Windows.

   **NO pide ni almacena tu contraseña de inicio de sesión de Windows.**

4. Espera unos segundos y consulta el estado:

   ```powershell
   .\install-login-lab.ps1 -Mode Status -ConfirmVm
   ```

   Busca `MachineId` y `Online: true`. Es un **ID nuevo del equipo**, diferente
   del ID del host/visor del usuario. Este ID de máquina se conserva tras reiniciar.
   No conectes al ID antiguo esperando que tenga la función nueva.

5. Desde el visor del otro ordenador conecta a ese `MachineId` con la nueva
   contraseña CSIViewer. Solicita **Ver la pantalla** y **Controlar ratón y teclado**.

## Prueba funcional

Primero comprueba conexión y control con Windows iniciado. Luego bloquea la VM
desde Inicio → usuario → Bloquear y conecta al ID de máquina. Comprueba que ves
una imagen actual y puedes abrir el cuadro de inicio de sesión con clic/Enter.
Solo entonces escribe tu contraseña de Windows **en el cuadro de Windows que
ves en la imagen remota**. No la envíes por chat ni en informes.

Después prueba reiniciar **solo la VM**, sin iniciar sesión localmente: espera
a que el host aparezca en línea, conecta al mismo ID de máquina e intenta el
login desde el visor. Verifica que la imagen y el control continúen al entrar
al escritorio y que puedas finalizar la sesión.

Si la imagen queda negra/congelada o no aparece el campo, no escribas contraseñas
a ciegas: vuelve a la consola VMware y recoge únicamente el estado/error indicados
abajo. El código está preparado para esta ruta, pero la prueba real sigue pendiente.

### Limitación de Ctrl+Alt+Supr

Esta versión no implementa envío de SAS (Ctrl+Alt+Supr). Si Windows lo exige,
atiende ese paso desde la consola VMware e indícalo al evaluar el resultado.
No desactives esa directiva ni UAC para forzar la prueba. PIN/WindowsHello,
SmartCard, BitLocker, RDP/múltiples sesiones y UAC no se dan por validados.

## Detener o revocar

El escritorio normal muestra un indicador de sesión del host de máquina con
**Revocar acceso remoto**. Revocar deja una marca persistente y corta sesiones;
solo un administrador puede volver a habilitar el acceso.

Desde PowerShell como administrador en la carpeta del paquete:

```powershell
.\install-login-lab.ps1 -Mode Stop -ConfirmVm
```

Para volver a habilitar conscientemente el acceso:

```powershell
.\install-login-lab.ps1 -Mode Start -ConfirmVm
```

Para quitar la tarea de arranque del laboratorio sin borrar identidad/informes:

```powershell
.\install-login-lab.ps1 -Mode RemoveTask -ConfirmVm
```

No toca la tarea `CSIViewerHost`, el visor, el host ni el servidor instalados.
La tarea nueva se llama **CSIViewerHostLoginLab**. Binarios en
`C:\Program Files\CSIViewer Host Login Lab`; datos en
`C:\ProgramData\CSIViewer.Host.Login.Lab`, protegidos para SYSTEM/Administradores.
No usa el perfil ni las credenciales guardadas de las aplicaciones existentes.

Si falla, comparte solo `status.json`, `failure.json` o `worker-failure.json` de
esa carpeta, si existen. **No compartas `login.json`, `identity.json` ni bases de
datos**. El estado no incluye contraseñas.

## Implementación

- Nuevo proyecto `CSIViewer.Host.Login`, broker SYSTEM/sesión0 y proceso trabajador
  SYSTEM en la sesión de consola física. Duplica solo su propio token, no tokens
  de Winlogon/otros procesos. El broker solo puede ejecutar su propio binario
  instalado, contenido en un job kill-on-close; no acepta ejecutables/comandos
  arbitrarios del visor ni expone IPC local privilegiado.
- Identidad CSIViewer de máquina independiente, DPAPI CurrentUser de SYSTEM para
  la clave privada; verificador PBKDF2-SHA256600000, sal aleatoria, contraseña
  remota no recuperable. La contraseña Windows nunca se almacena.
- Autenticación y canal cifrado existentes ANTES del consentimiento de máquina.
  La concesión automática es la opción explícita habilitada por el administrador
  del laboratorio; se limita a ViewScreen/ControlInput en la consola física.
- Solo una sesión. Nunca concede archivos, chat, audio o portapapeles desde SYSTEM.
  Cambio de consola, revocación o parada terminan las sesiones. No sustituye el
  consentimiento manual del visor/host normales, que conserva su implementación.
- Hilos MTA dedicados sin ventanas, OpenInputDesktop/SetThreadDesktop con derechos
  que ya tiene SYSTEM; no modifica ACL de escritorios, cambia el escritorio visible,
  desactiva UAC/UIPI ni genera SAS. Se deniega sin fallback si el acceso no existe.
- No admite nuevas pulsaciones, texto o movimiento si no hay una captura local
  reciente (menos de3s) del mismo escritorio. Durante una transición solo permite
  liberar teclas/botones, sin mover el cursor, para no dejarlos pulsados. Esta
  comprobación local no equivale a confirmación de recepción del frame por el visor.
- Captura GDI por frame para no retener DC/bitmaps de un escritorio anterior;
  JPEG10fps/1920 máximo en el experimento. El pipeline optimizado de las aplicaciones
  normales permanece igual. Entrada serializada y monitor actualizado al inyectar.
- Guarda de binarios y configuración: propietario SYSTEM/Administradores,
  rechazo de escritura por otros usuarios y de enlaces/reparsepoints.
- Configuración y binarios creados con ACL/propietario protegidos desde su creación,
  no después de copiar. Verifica SHA256 también sobre el destino antes de ejecutar
  nada. El verificador no se sobrescribe si el perfil ya existe.
- Revocación local visible sin ofrecer diálogos de archivos, terminal, cambios de
  configuración ni superficies para ejecutar comandos con privilegios SYSTEM.

## Estado de validación

- Compilación final Debug/Release limpia y **303/303 pruebas por configuración**.
- Pruebas nuevas: límites de permisos, rechazo de proceso no SYSTEM, compatibilidad
  del verificador sin contraseña recuperable y credencial inválida sin conexión.
- Prueba cifrada de autenticación y política del endpoint: **6/6**. Contraseña
  incorrecta rechazada; verificador correcto autenticado sin contraseña recuperable;
  permisos de archivos/chat/audio/clipboard retirados; control concedido y cierre limpio.
  Se ejecuta sin privilegios en escritorio normal, NO demuestra acceso Winlogon.
- Visor receptor WinUI real: **17/17**, con vídeo/clic/texto/arrastre en fixture
  aislada, consentimiento explícito/restauración, cierre e historia. El emisor es
  el core compartido, no otra ventana WinUI en un segundo equipo.
- Primer rerun del fixture no recibió el clic a tiempo; el harness ahora espera
  a que se estabilice el chat antes de activarlo y verifica ventana bajo puntero.
  Últimas repeticiones17/17; no se parcheó la inyección normal para ocultar el fallo.
- Ejecutable publicado rechaza `--worker` desde un proceso no SYSTEM y no crea
  perfil de máquina. Script comprobado sintácticamente; APIs ACL atómicas
  disponibles en WindowsPowerShell5.1. No se ha ejecutado el instalador privilegiado
  en el equipo de trabajo.
- Publicado self-contained win-x64: **CSIViewer-Login-Lab-Windows10-VMware-1.0.1.zip**,
  directamente en `artifacts/installers/`, con script, README y255 archivos de
  payload verificados por manifiesto SHA256. Registro del paquete:
  `artifacts/diagnostics/login-lab-package-1.0.1.json`.
- **Pendiente**: captura/entrada Winlogon, bloqueo/reinicio/login y transición al
  escritorio mediante esta implementación en la VM Windows10 del usuario.
- Los instaladores oficiales NO se anuncian como soporte login hasta esa validación.
