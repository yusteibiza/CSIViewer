# Captura tras minimizar el host y ubicación del acceso al login — 1.0.1

## Incidencia comunicada

El usuario conecta desde otro ordenador a CSIViewer Host dentro de Windows10
en VMware. La sesión funciona, pero al minimizar **la ventana del propio host
en el Windows invitado**, la imagen del visor queda congelada; el puntero local
se mueve pero no confirma recepción de entrada remota.

No se ha reproducido la incidencia ni se han obtenido logs de esa VM. La
revisión estática no encuentra una parada intencionada de captura al minimizar.
Sí encuentra dos huecos reales de recuperación:

- WGC solo pasa a GDI al recibir una excepción, no si deja de producir imágenes.
- Su StopAsync anterior ejecutaba Dispose nativo de forma síncrona antes de
  devolver Task; el timeout del llamador no podía limitar ese bloqueo.

## Cambios de captura

- Seguimiento de fotogramas terminados, generación y cambios visuales esperados.
- Minimizar/ocultar host y entrada remota válida inyectada (excepto mero movimiento
  del cursor) señalan actividad visual. No se pausa ni finaliza la sesión.
- El watcher de monitores comprueba progreso cada2s: sin primer fotograma8s,
  o sin fotograma tras cambio visual4s, programa respaldo GDI deduplicado.
- Un escritorio inactivo que ya ha entregado imagen no provoca respaldo solo
  por estar quieto. La entrada continua no reinicia indefinidamente el timeout.
- Se conserva monitor, calidad, FPS, permisos y sesiones; H.264 puede seguir
  activo sobre la fuente GDI. No se cambia la preferencia persistida del usuario.
- Cada ejecución WGC tiene su propio semáforo/callbacks/configuración. Una copia
  GPU antigua bloqueada no retiene el semáforo de la siguiente ejecución.
- Callbacks de ejecuciones retiradas no publican datos ni errores nuevos; el
  servicio filtra también por generación. Recuperación comprueba generación al
  adquirir el gate para no reiniciar una sesión posterior.
- Limpieza nativa en tarea separada, espera acotada del llamador y al salir.
  Si la limpieza anterior no termina se declina WGC y se usa GDI, sin crear
  más ejecuciones WGC sobre recursos que siguen bloqueados.
- Login SYSTEM conserva su captura ligada al escritorio: no se activa WGC ni
  se relaja la condición de captura reciente para permitir entrada privilegiada.

Esto añade recuperación para un fallo compatible con los síntomas, **no prueba
la causa exacta ni que resuelva todos los bloqueos de vídeo**. Un bloqueo nativo
del encoder/red/decoder es distinto de la ausencia de fotogramas en captura.
Si persiste, pedir logs de host VM y visor alrededor del momento del fallo.
Mensajes útiles: `Switching Windows.Graphics.Capture to GDI`, `Using GDI capture
fallback`, `Capture recovery failed`, `encoder is still busy`.

## Interfaz solicitada después

Se mueve **Acceso al inicio de Windows** desde HostPage a HostSettingsPage,
justo antes de Carpeta de descargas, fuera de la cuadrícula de dos columnas.
Usa una tarjeta CardStyle de ancho completo, botón alineado a la izquierda y
explicación con ajuste de línea, igual al bloque Privacidad y mantenimiento.
La gestión sigue siendo una acción independiente, no se guarda con las opciones.
Se conserva el configurador administrativo y su autorización HTTP explícita.

## Entrega

Solo compilación/publicación/empaquetado Release y traducciones. **Sin pruebas,
sin instalar, sin ejecutar las aplicaciones, sin modificar host/servidor reales.**
La reproducción de minimizar en VMware queda pendiente del usuario; Winlogon
también sigue pendiente de validación real.

Tres instaladores1.0.1.0 en `artifacts/installers/`, fecha común y hashes en
`artifacts/diagnostics/capture-recovery-login-settings-installers-1.0.1.json`.

Paquete intermedio generado **14/11/2026 20:19:49 +01:00**, Release limpio,
cinco payloads publicados y tres instaladores compilados. Después el usuario
añadió limpieza de chat al eliminar dispositivo (informe42), que requiere una
nueva entrega y sustituye los instaladores de la raíz. No hubo pruebas reales.

Dato posterior del usuario: conexión al equipo físico funciona; conexión al
Windows10 invitado VMware falla. Es una diferencia de entorno confirmada por el
usuario, no una demostración de qué componente del vídeo se bloquea. Se necesitan
logs de ambos extremos para continuar sin atribuirlo automáticamente a WGC.
