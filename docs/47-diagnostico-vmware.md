# CSIViewer — diagnóstico de congelación VMware

> **Compilaciones posteriores a r2:** incluyen ya el lanzador con `--data-dir`,
> el colector que recoge `csiviewer.host-*` y la comparación `Compare-Host.ps1`.
> No requieren sustituir scripts por las correcciones aparte entregadas para r2.
> Al recoger, el escritor reintenta una solicitud temporalmente bloqueada en vez
> de detener todo el diagnóstico. No reinicia captura/encoder/UI.

> **Corrección del lanzador tras la prueba real:** en el paquete r2, el host
> necesitaba `--data-dir` además de la variable de entorno. Usar la copia
> corregida de `scripts/start-pipeline-diagnostic.ps1` (entregada aparte como
> `Start-Diagnostic-Corrected.ps1`) junto al manifiesto original.
> Esta comprueba que el proceso crea su propio `pipeline-<PID>-*.jsonl` y no
> anuncia éxito si sale por colisión de instancia. Los binarios r2 no cambian.
> Si aparece `C:\Program Files\CSIViewer Host` en “Abrir ubicación del archivo”,
> sigue activo el host instalado: no es la ejecución portable.
> Salir manualmente del instalado mediante icono de bandeja → **Salir**;
> cerrar solo la ventana puede dejarlo ejecutándose. No parar servicios/tareas.
> Si vuelve a aparecer automáticamente, detener el procedimiento y comunicarlo.
> Una ejecución aislada nueva tiene otro ID y requiere configurar su servidor
> desde la UI; no copiar settings/identidades del perfil instalado.

**No es una versión final ni un instalador. La congelación NO se declara resuelta.**
Paquete portable Release x64, .NET/Windows App SDK autocontenidos, versión de
archivo 1.0.1.0 y **BuildId único en `build-manifest.json` y en cada log pipeline**.
Usar este mismo paquete en los dos extremos. No mezclar DLL de otras versiones.
El lanzador comprueba todos los archivos del rol contra el manifiesto; SHA-256
detecta cambios respecto a este lote, no sustituye una firma de editor.
Puede comprobarse sin iniciar la aplicación:

```powershell
.\Start-Diagnostic.ps1 -Role Host -VerifyOnly
.\Start-Diagnostic.ps1 -Role Viewer -VerifyOnly
```

## Inicio rápido

En las compilaciones nuevas también puede hacerse doble clic en
**`Start-Host.cmd` dentro de VMware** y **`Start-Viewer.cmd` en el físico**.
En la compilación JPEG-fix usan RunName **vmware04**, ventana de PowerShell visible, mismo script y mismas
guardas: no elevan ni cierran procesos. Se necesita salir manualmente de la
aplicación del mismo rol antes de arrancar otra. Extraer el paquete nuevo en
otra carpeta; no sobrescribir binarios o logs de una sesión en marcha.
Para reproducir el segundo incidente, mantener los ajustes de aquella prueba
(H264/WGC, 60 FPS, calidad95, ancho máximo2560, escritorio1920×1080) y anotarlos.
No subir a90 ni cambiar opciones simultáneamente.

1. Extraer el ZIP en una carpeta local escribible de laboratorio, por ejemplo
   `C:\CSIViewerLab\diagnostico`, tanto en el ordenador físico (visor) como en
   **Windows 10 invitado en VMware Workstation** (host).
2. Cerrar desde su interfaz el visor/host habitual si está abierto. No parar
   servicios/tareas ni desinstalar nada: el lanzador rechaza procesos del mismo
   nombre y **no los mata**. Si un agente recrea el host, usar un snapshot/cuenta
   de laboratorio sin ese agente; no desactivar tareas automáticamente.
3. Abrir PowerShell en la carpeta extraída. En la VM:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Start-Diagnostic.ps1 -Role Host -RunName vmware01
   ```

   En el ordenador físico:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Start-Diagnostic.ps1 -Role Viewer -RunName vmware01
   ```

   El parámetro de ejecución afecta solo a ese proceso, no cambia la política
   de Windows. Si una política administrada impide ejecutar el script, no la
   desactivar: solicitar revisión/firma por el administrador de laboratorio.
4. Configurar desde la UI **el servidor elegido por usted**, en ambos extremos.
   Preferir un servidor de laboratorio. Aquí no se ha conectado al servidor del
   usuario. No se copian configuraciones/identidades/bases de datos instaladas.
   Si usa HTTP, conservar su opt-in explícito habitual; el cifrado de sesión
   sigue activo. No enviar códigos/contraseñas en los registros.
5. Guardar configuración de la carpeta aislada y conectar usando el **nuevo ID
   del host de diagnóstico** y su contraseña. Las preferencias del host deben
   reproducir el caso original: WGC + H.264, resolución y FPS originales
   (1920×1080 / 90 FPS si siguen siendo los del incidente). Anotarlos.

Los datos están **solo en `runs\Host-vmware01` / `runs\Viewer-vmware01` dentro de
la carpeta extraída**; logs en `logs`. Repetir el mismo RunName reutiliza esos
datos de laboratorio; otro nombre crea un laboratorio nuevo. No copiar los datos
instalados. En modo diagnóstico se impiden cambios de HKCU Run/tarea del host y
registro de notificaciones. No se incluye agente, servicio ni provisión Winlogon.

## Secuencia exacta para reproducir

Preparar Bloc de notas con texto sintético y un reloj con segundos visible en
el invitado. Registrar hora UTC, versiones de Windows/Workstation/Tools, GPU,
resolución/DPI/monitores y aceleración 3D **sin cambiarla en la primera prueba**.

1. Mantener 15 s el escritorio sin cambios. Comprobar reloj/animación y texto.
2. Desde el visor, mover Bloc de notas; minimizarlo, maximizarlo y restaurarlo.
   Repetir 10 veces. Anotar la hora y qué acción precede al bloqueo.
3. Repetir minimizando/restaurando **la ventana de CSIViewer Host dentro de la
   VM**, sin finalizar la sesión. Después repetir con el visor físico y, por
   separado, con la ventana de VMware Workstation. No confundir esas ventanas.
4. Si se congela, **no reconectar inmediatamente**. Intentar mover y escribir
   una frase sintética en Bloc de notas desde el visor; comprobar por la consola
   VMware si el Windows invitado responde al ratón/teclado local.
5. Esperar 5, 10 y 30 s y anotar si vuelve. No pulsar acciones de recuperación
   ni cambiar códec durante el primer intento. El watchdog de captura del modo
   diagnóstico **registra pero no reinicia WGC/GDI**. Los fallbacks existentes
   por errores explícitos/backlog se mantienen y sus errores quedan registrados.
6. Recoger **ambos extremos mientras sigue ocurriendo**. En cada máquina:

   ```powershell
   # VM:
   powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Collect-Diagnostic.ps1 -Role Host -RunName vmware01
   # Ordenador físico:
   powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Collect-Diagnostic.ps1 -Role Viewer -RunName vmware01
   ```

   El script solicita un volcado del anillo sin usar el hilo UI, espera 3 s y
   crea ZIP en `runs`. No detiene aplicaciones ni lee settings/identity/DB.
   Si el escritor del diagnóstico o todo el proceso no responde, podría no
   atender la solicitud: conservar igualmente los logs y anotar esa ausencia.
7. Recoger otra vez 30 s después de recuperar/reconectar. Conservar los ZIP y
   el manifiesto, indicar qué prueba y RunName corresponden a cada uno.

Después repetir en ejecuciones separadas WGC+JPEG, GDI+H.264 y GDI+JPEG y, si
procede, 30 FPS. No atribuir la causa a una opción solo porque otro modo funcione.
No hay que desactivar UAC, Defender, VMware Tools ni otras protecciones.

## Qué registran las métricas

- Cada 2 s: contadores acumulados, último progreso monotónico por etapa,
  generaciones de captura, sesiones, transporte y última actividad del peer.
- En memoria: anillo fijo de 8192 eventos numéricos/GUID, sin píxeles, texto,
  teclas, chat, clipboard, contraseñas ni códigos de inscripción. Sin escritura
  ni locks de fichero desde captura/encoder/red/UI.
- Al detectar una sospecha o error: volcado con cooldown de 30 s. Solicitud
  manual fuerza una copia del anillo sin esperar al cooldown. Rotación a unos 16 MiB, máximo 3 segmentos por
  proceso (un volcado puede superar ligeramente el umbral).
- `pipeline-<PID>-<fecha>.jsonl[.1/.2]`: identidad, muestras y flight recorder.
  `csiviewer-*.log*`: logs normales ya existentes, que pueden contener nombres,
  rutas e IDs. **Revisar antes de compartir**. La última línea de una copia en
  vivo puede estar incompleta; no equivale a un fallo del pipeline.

### Relacionar ambos extremos

`Session` es el GUID de la sesión en ambos lados. En host, `CaptureFrame`/`Frame`
es el ID local de captura/llamada al encoder. En eventos `SendBegin/SendDone`,
**A = contador de nonce + 1 del paquete cifrado**, B = tamaño. Ese A coincide
con `Frame` de `VideoReceived` en el visor. Se conserva en la cola de decode y
en `UiSubmitted/CompositionObserved`. No se modifica el protocolo de vídeo.

H.264 puede producir cero, uno o varios frames por llamada: el ID del visor
identifica el **lote de entrada al decoder**, no una garantía de correspondencia
pixel-a-pixel del códec. Se registra también el número de frames de salida.
Los relojes monotónicos son locales: usar `Frequency`, `OriginTicks`, UTC e IDs
de paquete; **no restar timestamps de máquinas distintas como latencia real**.
Ratón: GUID `Trace`, enviado/recibido/aceptado por Windows y posición consultada
en el host. `LocalPointerMove` solo observa la superficie del visor.

| Observación | Interpretación permitida |
|---|---|
| Captura quieta sin demanda visual | Compatible con imagen estática; no declarar captura detenida |
| CaptureBegin sin CaptureDone | Operación de captura/copia GPU pendiente |
| Captura entregada, EncodeBegin sin EncodeDone | Codificador/conversión pendiente |
| SendWaiting / SendBegin sin SendDone | Cola cifrada o envío al transporte pendiente; write completo no es ACK de host |
| SendDone del host sin VideoReceived correlacionado | Tramo de red/relay/recepción por investigar; revisar también heartbeat |
| VideoReceived/DecodeQueued sin salida de decode | Cola/decoder; el buffering legítimo puede dar cero frames |
| DecodeDone sin UiSubmitted | Cola UI/vista oculta o trabajo UI detenido |
| UiProbeQueued sin UiProbeExecuted >5 s | Dispatcher no contesta; también puede estar cerrándose |
| UiSubmitted sin CompositionObserved | Compositor/vista oculta/minimizada por investigar |
| CompositionObserved | Tick WinUI posterior a entregar imagen, **NO ACK físico de presentación en monitor** |
| InputWindowsAccepted | Windows aceptó SendInput, **NO prueba de consumo por la aplicación del host** |
| Cursor local sigue moviéndose | No prueba ni vídeo ni procesamiento remoto |
| Sin heartbeat/datos de peer | Peer/red/host no responde al protocolo; no identifica por sí solo la causa |

Si hay fallo gráfico, buscar `GraphicError` A=HRESULT (o código Win32 en GDI),
incluidos DXGI device removed/reset/hung. Añadir eventos Application/System de
Windows en el intervalo, `dxdiag` y `vmware.log` revisados. No enviar discos de VM
ni dumps sin revisar: un dump puede contener información secreta.

### Diagnóstico ampliado después de la recurrencia real

La prueba buena inicial no cerró el incidente: el usuario reprodujo después la
congelación sin cambiar a 90 FPS. El host seguía capturando mientras faltaba
salida de codificación; el visor había pedido JPEG por backlog H264.
Las nuevas compilaciones observan además:

- `JpegEncodeBegin/Done`, `EncodeFailed`: A=HRESULT, B=fase (1 preparación,
  2 H264, 3 JPEG, 4 construcción/envío). Primer fallo exterior del proceso
  también se registra una sola vez con excepción en el log normal; los
  siguientes quedan numéricos en el anillo, no un log de disco por frame.
- Una sucesión de intentos sin completar ya no renueva indefinidamente el
  umbral de sospecha: se usa el primer inicio pendiente desde la última
  finalización. Los agregados siguen siendo de proceso, no garantía por stream.
- `UiViewState`: A=1 Loaded, 0 Unloaded. `UiFrameOffered`: A es máscara
  visible=1, sesión activa=2, controller=4; B indica render programado.
  `UiRenderQueued.A` indica si DispatcherQueue aceptó el callback;
  `UiRenderBegin/Done` separan una operación de imagen pendiente de un
  dispatcher global que no responde. No cambian el scheduling ni recuperan UI.
- Las muestras incluyen ajustes numéricos/booleanos de vídeo y estado de
  fallback. No registran settings completos, texto, servidor o contraseñas.

Un visor navegando a otra página puede legítimamente dejar de presentar
fotogramas; no confundirlo con bloqueo sin comprobar la visibilidad.

### Corrección JPEG y progreso de contenido muestreado

La compilación JPEG-fix pasa `ImageQuality` como opción de creación del
codificador (Single/float), no como metadato. Corrige el error real
`0x88982F41` observado en la VM al solicitar JPEG. No se afirma con ello que
estén corregidos los episodios GDI/H264 en que todas las etapas avanzaban.

Con BGRA y diagnóstico activo se muestrean como máximo64×36 píxeles RGB por
imagen, en captura entregada y salida H264 decodificada. Solo se registran
booleanos de cambio y número de muestras: **no se escriben píxeles ni hashes**.
`CaptureContentSampled`/`DecodedContentSampled`: A=1 si cambia la muestra
(incluida primera muestra), A=0 si no, B=número de puntos.
`CaptureContentChanged`/`DecodedContentChanged` dan contador/último progreso.
El hash de comparación es privado y transitorio en memoria. RGB ignora alpha.
Una muestra igual NO prueba igualdad de toda la imagen; cambios fuera de la
rejilla pueden no detectarse. JPEG no aporta esos indicadores de BGRA en
estas rutas; ausencia del indicador no equivale a captura detenida.

## Límite de la entrega

La instrumentación localiza **progreso observado y operaciones pendientes**.
No promete causa raíz exacta si el driver, el proceso completo o la VM dejan de
ejecutarse, ni prueba física de display-present. La resolución del incidente
exige ejecutar esta secuencia en la VM afectada. No sustituye los instaladores
actuales ni autoriza Release. El manifiesto y sus SHA-256 identifican este lote.

La lista visual completa se genera en el repositorio con
`scripts/export-visual-checklist.ps1`: CSV en `artifacts/audit/manual-ui`.
Sus filas están marcadas NO EJECUTADA; aplicar los procedimientos de
`docs/45-validacion-manual-release.md` y `docs/46-revision-ui-video.md`.
No confundir inventario XAML/pruebas estáticas con uso real de WinUI.
