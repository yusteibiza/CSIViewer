# Control remoto sin permiso y navegación Dark — 1.0.1

## Diagnóstico

El registro del host instalado muestra que las conexiones recientes de PUESTO3
recibieron ViewScreen, Chat, SendFiles, ReceiveFiles, Audio y Clipboard, pero no
ControlInput. Otra sesión anterior conservaba el control. Esto explica por qué
esas conexiones no podían enviar ratón; no demuestra por sí solo qué decisión
retiró el permiso. No se modifica SendInput ni se eluden UIPI/UAC/escritorio seguro.

## Cambios

- Se propaga la causa de control restringido en los dos caminos de autenticación.
- Si se solicitó control y no se concedió, se avisa explícitamente: ocupado por
  otra sesión (`control_in_use`) o no concedido. Una petición voluntaria de solo
  visualización no genera aviso. No se transfiere ni se promueve el control.
- Los títulos distinguen control, vista sin control y sesión sin pantalla.
- El host registra permisos solicitados/efectivos y restricción, sin contraseñas.
- Fondos NavigationView Dark opacos #202020 en visor/host. Light sigue #F3F3F3;
  Default y HighContrast permanecen intactos.
- Cinco textos nuevos revisados en es/en/fr/ca; 970 claves por idioma.

## Validación

- Compilación inicial Debug/Release sin advertencias ni errores; 279/279 pruebas
  en cada configuración. Incluye clasificación de restricciones, liberación de
  control sin promoción implícita y contratos XAML Light/Dark/HighContrast.
- Prueba con host/dos visores y servidor aislado: 7/7. Primero obtiene control;
  segundo recibe InUse y no puede controlar; cierre no promueve al segundo;
  reconexión nueva recupera control. Informe `artifacts/diagnostics/control-lease-release.json`.
- UI nativa Release: 15/15, incluido hover y filas compactas. Píxel del panel
  Light RGB(243,243,243), Dark RGB(32,32,32), sin tinte. Informe/capturas
  `artifacts/diagnostics/control-theme-viewer-release.json`.
- Reconstrucción final Debug/Release limpia: 279/279 en cada configuración.
- El empaquetado en la carpeta habitual falló con Error 32 al sobrescribir el
  instalador del host, bloqueado por otro proceso. El visor raíz sí se regeneró;
  no considerar los tres archivos raíz como un lote final coherente.
- Reempaquetado de los tres terminado, sin cerrar procesos ajenos. Carpeta final
  `artifacts/installers/control-theme-20261114-112843/`, versión 1.0.1.0 y marca
  común **14/11/2026 11:31:37 +01:00**. Los tres hashes y fechas se verificaron
  nuevamente tras la generación. Manifiesto final
  `artifacts/diagnostics/control-theme-installers-1.0.1.json`.
- Sigue pendiente la confirmación física del usuario al cerrar sesiones previas
  y reconectar solicitando y concediendo «Controlar ratón y teclado».

No se ejecutan instaladores, no se modifican servicios instalados y no se detiene
el servidor del usuario. El ensayo aislado no envía eventos de entrada al escritorio.
