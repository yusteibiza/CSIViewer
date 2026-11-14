# Instaladores con acceso al inicio de Windows y HTTP opcional — 1.0.1

## Entrega solicitada

El usuario solicita los tres instaladores habituales con la función integrada,
sin más pruebas, y autoriza explícitamente permitir también HTTP remoto.
Se compila y empaqueta; **no se ejecutan pruebas ni instaladores** en esta entrega.
La función sigue siendo candidata, pendiente de validación real de Winlogon.
No presentar las303 pruebas anteriores como validación de esta integración nueva.

## Qué incluye

- El instalador del host incluye `login/CSIViewer.Host.Login.exe` y dependencias
  en instalaciones **para todos los usuarios**, con administración y ruta
  predeterminada `C:\Program Files\CSIViewer Host`.
- Opción desmarcada de configuración del acceso al inicio de Windows. También
  se puede abrir desde el botón **Acceso al inicio de Windows** del host o desde
  el acceso del menú Inicio. La gestión requiere elevación visible por UAC.
- Se mantiene la tarea SYSTEM **CSIViewerHost → CSIViewer.Host.Agent.exe**.
  El agente supervisa el broker opcional solo si existe configuración autorizada.
  No se sustituye por otra tarea ni se elimina la identidad del host de usuario.
- Perfil de máquina separado en `C:\ProgramData\CSIViewer.Host.Login`, privado
  para SYSTEM/Administradores. **ID nuevo**, mostrado como `MachineId`, y clave
  CSIViewer de equipo independiente, guardada solo como verificador PBKDF2.
- Nunca pide ni almacena la contraseña Windows: esta se teclea en el formulario
  de Windows a través de la sesión remota cifrada.
- Se conserva el consentimiento manual del visor/host normales. El permiso
  desatendido de máquina exige `ACTIVAR` en la configuración administrativa.
- Se retira la limitación VMware de esta integración a petición del usuario;
  se recomienda mantener la primera instalación en la VM/snapshot.

## HTTP y HTTPS

- HTTPS remoto sigue siendo la opción predeterminada/recomendada.
- Para HTTP remoto, incluida `http://yusteibiza.ddns.net:5180`, el configurador
  muestra el riesgo y exige escribir exactamente **PERMITIR HTTP**.
- Solo entonces guarda `AllowInsecureServer=true` en el perfil administrativo y
  lo transmite a los clientes de señalización/registro existentes.
- El cifrado propio de sesión no se elimina. HTTP no protege inscripción,
  señalización ni autenticidad del servidor; especialmente riesgoso en Internet.
  No afirmar que el cifrado propio sustituye completamente a TLS frente a ataques
  activos durante el primer contacto. Se recomienda HTTPS o VPN.
- No se cambia el servidor en ejecución, sus puertos ni su configuración.

## Activación por el usuario

1. Instalar **CSIViewer Host para todos los usuarios**, en la ruta predeterminada.
2. Seleccionar la configuración opcional de login durante la instalación, o abrir
   **Acceso al inicio de Windows** desde la ventana del host/menú Inicio.
3. Elegir Configurar, autorizar con `ACTIVAR`, introducir servidor y, si es HTTP
   remoto, confirmar `PERMITIR HTTP`. Introducir código de inscripción y una nueva
   contraseña CSIViewer de equipo (no la contraseña Windows).
4. Consultar **Ver ID/estado** y usar el **MachineId nuevo** con esa clave desde
   el visor. El ID y contraseña normales del host de usuario no son los de login.
5. La gestión permite habilitar/deshabilitar el componente sin desactivar las
   conexiones normales ni modificar el servidor. No se guardan contraseñas en logs.

## Límites y seguridad

- Captura/entrada del escritorio activo mediante worker SYSTEM en sesión de
  consola física; autenticación cifrada antes de conceder ViewScreen/ControlInput.
- No concede archivos, chat, audio ni portapapeles en ese endpoint privilegiado.
- No cambia ACL de escritorios, UAC/UIPI, políticas SAS ni valida contraseñas de
  Windows por su cuenta. **No incluye envío de Ctrl+Alt+Supr** en esta revisión.
- Exige captura reciente del mismo escritorio antes de nueva entrada; solo
  permite liberaciones sin movimiento si no existe captura reciente.
- El agente usa un pipe anónimo privado únicamente para detener su broker; valida
  propietario/ACL/rutas antes de ejecutarlo. El broker fija el proceso agente y
  contiene al worker en un job kill-on-close. Parada/cambio de consola liberan
  sesiones antes del cierre, con terminación del propio hijo como último recurso.
- Indicador local y revocación persistente; cerrar el indicador revoca, no oculta
  una sesión activa. El perfil y aplicaciones existentes se conservan.
- Pendiente de validación real: reinicio/login, captura/entrada del escritorio
  seguro, transición al escritorio normal, layouts/monitores y despliegue privilegiado.

## Empaquetado

Los tres instaladores generados permanecen directamente en `artifacts/installers/`,
versión1.0.1.0 y marca de modificación común **14/11/2026 15:34:11 +01:00**.
Compilación/publicación Release y empaquetado Inno terminados. No se ejecutaron
pruebas, sondas de conexión ni instaladores por instrucción del usuario.
Catálogos completos es/en/fr/ca:974 claves por idioma. Manifiesto final:
`artifacts/diagnostics/windows-login-http-installers-1.0.1.json`.
No se crean subcarpetas de entrega ni se modifican aplicaciones/servidor instalados.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | E975EEADA924DE3FF8C35E29ED3269F9B2B2E536DEAA83925FC1AC672FA79779 |
| CSIViewer.Host-Setup-1.0.1.exe | D310E40C1FF869ADB89D92D104D08BED9BC880BC558AE5DE53D55EC226B246C6 |
| CSIViewerServidor-Setup-1.0.1.exe | 6F2DB65BE2EBE5E3CF51AEBBDB6C01B7B61277CC5FBCF839FC725E9F56AAA99A |
