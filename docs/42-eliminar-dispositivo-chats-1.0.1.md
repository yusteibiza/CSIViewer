# Eliminar dispositivo con su chat y limpiar selector — 1.0.1

## Peticiones

- Quitar del aviso de borrado de chat la frase sobre el otro equipo.
- Al eliminar un dispositivo guardado, borrar también sus chats y no mostrarlo
  de nuevo en el combo de dispositivos del chat.

## Implementación

- Confirmación de chat: «Se borrará el historial de chat de {0}. ¿Continuar?».
  La ayuda se actualiza y ya no contiene la frase retirada.
- Confirmación de dispositivo avisa expresamente de que también elimina su chat.
- `KnownDeviceRepository.DeleteAsync` elimina mensajes por PeerDeviceId y
  dispositivo en una única transacción SQLite. No borra otros dispositivos,
  contactos, transferencias ni historial de conexiones.
- `RemoteClient.DeleteKnownDeviceAsync` serializa con cargas/guardados de chat,
  invalida versiones antiguas y limpia mensajes y dispositivo en memoria.
- Aviso de eliminación al controlador de chat borra archivo de peers, borrador
  y selección. El combo ya no conserva automáticamente un peer solo por haber
  sido la selección anterior.
- Los dispositivos eliminados se excluyen también de la sesión activa del combo.
  Durante esa ejecución se descarta chat posterior de ese peer hasta volverlo a
  añadir o iniciar una nueva conexión que lo guarde de nuevo. No se finalizan
  sesiones de control ni se envía una orden de borrado al otro equipo.
- Al volver a guardar/conectar se habilita un hilo nuevo sin recuperar los chats
  borrados. Persistencia de conexión antigua encolada comprueba versión para no
  resucitar un dispositivo eliminado durante la operación.
- Al borrar manualmente un chat de un peer no guardado/inactivo, el peer también
  desaparece del selector si ya no tiene mensajes. Un dispositivo que sigue
  guardado permanece disponible para empezar un chat nuevo.
- No se realiza una limpieza masiva de chats antiguos que no tengan dispositivo:
  podrían ser conversaciones válidas del usuario. El borrado está dirigido al
  dispositivo expresamente eliminado.

## Entrega

Se mantienen todos los cambios de informe41: recuperación candidata de captura
tras minimizar el host y acceso al inicio de Windows en configuración antes de
Carpeta de descargas, en tarjeta de ancho completo.

Compilación Release sin errores/avisos y catálogos es/en/fr/ca actualizados.
**Sin pruebas ni instaladores ejecutados** por instrucción del usuario; no se
modifican las bases de datos ni las aplicaciones instaladas durante este trabajo.
Congelación VMware y Winlogon siguen pendientes de validación real.

Tres instaladores1.0.1.0 directamente en `artifacts/installers/`, marca común y
hashes en `artifacts/diagnostics/device-chat-cleanup-installers-1.0.1.json`.

Entrega final: **14/11/2026 20:31:05 +01:00**. Publicados cinco payloads y
generados los tres instaladores; Release sin errores/advertencias. Catálogos
actualizados con974 claves base. No se ejecutaron pruebas ni instaladores.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | B7CD488922927A17A47C5C92BBD34D71846317B993E34CEC3ED49AF01C8A4C8F |
| CSIViewer.Host-Setup-1.0.1.exe | 4C205261FBB8CAB5931EF33E27CA6F9A7A20353D3DF9F34EA5BE857F83D16CF6 |
| CSIViewerServidor-Setup-1.0.1.exe | 00F658B54EBA2B46F15F52CFBF55EF961457D14C8D1C0FCFB4AF31F2B5C24A9B |
