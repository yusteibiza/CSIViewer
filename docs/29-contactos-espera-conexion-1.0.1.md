# Contactos y estado de espera de conexión — 1.0.1

## Cambios solicitados

- Contactos de visor/host: fila completa estirada con fondo hover
  `SubtleFillColorSecondaryBrush`, esquina de 4 DIP y retorno a transparente al
  salir. Reutiliza RowHover de Dispositivos, respetando Light/Dark/alto contraste.
  No introduce selección persistente ni conexión al hacer clic en la fila.
- Pantalla de Control remoto: icono de monitor conservado y centrado junto con
  título y explicación. Textos centrados y con ajuste de línea, ancho máximo
  560 DIP y margen interior para ventanas pequeñas.
- En reposo: «Listo para conectar» e instrucciones para ID, permisos y Conectar.
- Durante la solicitud: ProgressRing activo y «Conectando con el equipo remoto…».
- En autenticación/autorización: «Esperando aceptación del equipo remoto…» y
  explicación de que el equipo está validando la conexión/permisos. No afirma
  que haya una persona esperando si la autorización es automática.
- El indicador solo se usa para la conexión iniciada desde esta página, no para
  la conexión general con el servidor. Se detiene al conectar, rechazar, fallar o
  agotar el tiempo de espera. No se modifican tiempos ni políticas de autorización.
- AppState.IsAwaitingPeerAuthorization separa la fase de autorización saliente
  del estado global de conexión al servidor. Se activa al enviar auth.request y
  se restablece en finally; una publicación tardía comprueba el PendingConnect
  correspondiente para no reaparecer tras cancelar.
- Etiquetas dinámicas usan Translate.SetText para mantener la traducción si se
  cambia de idioma durante la espera. Se revisan es/en/fr/ca offline.

## Verificación

Debug y Release limpios: 267/267 tests en cada configuración. Catálogos completos:
946 claves por idioma.

- UI nativa Debug: hover Contactos 9/9 (Light y Dark, fila estirada y cambio de
  píxeles real), espera 19/19 (peer realmente esperando decisión, animación visible,
  centrado a <=2 px, rechazo restaura reposo y aceptación muestra sesión).
- Capturas reales revisadas; sufijo release en connection-wait del primer ensayo
  corresponde al probe Release, aunque el visor de ese ensayo era Debug.
- Los primeros ensayos detectaron que el estado global del servidor no sirve
  para identificar autorización saliente y que la UI del InfoBar puede publicarse
  después del botón. La prueba espera el marcador del peer y el banner de sesión;
  producción usa la fase dedicada descrita arriba, sin modificar reconexión.
- UI Release también aprobada: espera 19/19 y hover 9/9. Empaquetado de los tres
  instaladores terminado. Se conserva la
  misma marca de modificación común en la tanda, igual que en entregas anteriores.
  Manifiesto: `artifacts/diagnostics/contacts-connection-wait-installers-1.0.1.json`.

Se añaden cuatro regresiones XAML (Contactos full-width/tema y placeholder
centrado/spinner inactivo, ambas aplicaciones). FeatureProbe incorpora host
diagnóstico con consentimiento diferido para verificar espera, rechazo y aceptación
sin usar equipos o credenciales reales. El harness externo prueba el color hover
en Light y Dark con datos sintéticos.

La primera suite detectó dos traducciones retiradas que el generador conservaba
si no había lotes pendientes. Se corrige la poda final del generador incluso
cuando todas las traducciones ya existen; no se modifica la lógica de idiomas.

No se instalan aplicaciones ni se modifica el servidor en ejecución, las claves
guardadas, el vídeo optimizado, las sesiones o el ZIP original 1.0.0.

## Instaladores finales

En `artifacts/installers`, versión 1.0.1.0, marca de modificación común
**14/11/2026 09:00:41 +01:00**. Publicaciones/compiladores correctos, hashes
verificados; no se ejecutaron los instaladores.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | AFC339609604325B21424A829B13F648292E1A6A0139291457BCA11079D3AD02 |
| CSIViewer.Host-Setup-1.0.1.exe | 6F0529D114427838C551365F86B80C904638D4F428C6EF0732AAA5A648389EBE |
| CSIViewerServidor-Setup-1.0.1.exe | E059327DD3AB37E8325C781A70F6DD23FDCD598CF2FE05A3EDB6181FD0B60BF3 |
