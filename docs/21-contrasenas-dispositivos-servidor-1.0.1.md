# Contraseñas de dispositivos y paquetes 1.0.1 — 13/11/2026

## Defecto reproducido

`RemoteClient.PersistKnownDeviceAsync`, usado tanto al conectar como al aceptar
sesiones, construía un KnownDevice sin el último argumento ProtectedPassword.
Después llamaba a UpsertAsync, que asignaba ese null a la columna de SQLite.
La contraseña se borraba realmente del registro, no era solo un PasswordBox vacío.
Si faltaba el dispositivo en la caché UI también podía sustituir alias y otros
detalles del registro existente por valores predeterminados.

Además, el editor de dispositivos del host construía el registro sin conservar
ProtectedPassword, aunque ese editor no ofrece modificar contraseñas.

La corrección anterior del diálogo del visor (campo vacío conserva contraseña,
checkbox explícito para quitarla) no cubría esta otra ruta automática de sesión.

## Corrección

- Nuevo `IKnownDeviceRepository.RecordConnectionAsync`: lee el registro real y
  modifica exclusivamente LastSeenUtc y una clave pública no vacía. EF no escribe
  campos de usuario ni la contraseña durante esta operación. No hace depender
  la conservación del secreto de que exista una copia en AppState.
- Al crear un dispositivo por primera conexión no guarda automáticamente la
  contraseña utilizada para autenticarse; se mantiene la elección explícita.
- Actualización de AppState en el dispatcher: busca por ID y mezcla únicamente
  los datos de conexión, sin reemplazar ediciones de usuario por una copia vieja.
- El editor del host conserva el ProtectedPassword existente.
- UpsertAsync sigue permitiendo borrar la contraseña cuando el usuario lo pide
  explícitamente en el editor del visor. No se aplica un coalesce que impida quitarla.
- Presencia y eliminación de listas no borran el secreto; actualizan otros campos.
- DPAPI sigue vinculado al usuario de Windows. Copiar una base a otro usuario puede
  impedir descifrarla, pero no se ha cambiado ni debilitado ese mecanismo.

No se leyeron ni imprimieron contraseñas reales, ni se modificaron bases de datos
de las instalaciones del usuario. Los tests usan SQLite y secretos ficticios en
carpetas temporales aisladas. Las contraseñas ya borradas no se recuperan por esta
corrección: tras instalarla deberán guardarse de nuevo si faltan.

## Evidencia

- Dos regresiones, con y sin dispositivo en la caché UI, **fallan antes** de la
  corrección con ProtectedPassword=null y **pasan después**.
- `artifacts/diagnostics/device-password-before-fix.log`
- `artifacts/diagnostics/device-password-after-fix.log`
- Otros dos tests comprueban quitar expresamente la contraseña, mantener fecha/
  clave pública ante actualizaciones vacías/antiguas y crear un dispositivo nuevo
  sin guardar una contraseña de autenticación.
- Solución Debug/Release: cero errores/advertencias y **187/187 tests** en ambos.
- **22/22 comprobaciones de sesión real** con las DLL compartidas publicadas:
  conservación de un secreto DPAPI ficticio en SQLite y caché tras conectar,
  más los 21 casos de transferencia de la tarea anterior.
  `artifacts/diagnostics/device-password-live-transfer-regression.json`.
- UI WinUI publicada: archivos **7/7** en
  `ui-password-fix-files-release.json`.
- Regresión fullscreen: un primer intento falló en el check de ocultación tras
  una espera fija de 2,3 s (`ui-password-fix-fullscreen-release.json`). Repetido
  **sin cambiar código ni harness**, pasó **15/15**
  (`ui-password-fix-fullscreen-release-retry.json`). No se ha demostrado la causa
  de esa variación; no se presenta el primer intento como correcto.

## Servidor 1.0.1

Publicado también `artifacts/installers/payload/server/CSIViewerServidor.exe` con
versión 1.0.1.0 y generado su instalador. Smoke test del EXE publicado: arranque en
un puerto loopback libre con SQLite temporal y respuesta `status=ok` de
`/api/health`. UPnP/STUN desactivados durante la prueba. Se cerró solo el proceso
de diagnóstico; no se paró/reconfiguró el servidor ni servicio instalado.

Evidencia: `artifacts/diagnostics/server-101-smoke.json`. No equivale a instalar
o validar de nuevo todos los escenarios de servicio/UI del servidor.

## Instaladores definitivos de esta revisión

En `artifacts/installers/`, todos con versión Win32 1.0.1.0:

| Archivo | Fecha local 13/11/2026 | SHA-256 |
|---|---|---|
| `CSIViewer-Setup-1.0.1.exe` | 19:26:42 | `F5B7CF3EE4698C6A42777DD17F320E2DFF6DC84DE8E02BEE1152ACC0BAC25562` |
| `CSIViewer.Host-Setup-1.0.1.exe` | 19:27:55 | `E56269B2B5A8EDF1D07CE09A957E2219827690B616F01A3EF01A2FDE8B2BD073` |
| `CSIViewerServidor-Setup-1.0.1.exe` | 19:29:00 | `7EDF918F6C4FCF4A3BCDF4936BD6B669EB1B062B2F22B94C7323FD93595BAB60` |

Tras comprobar que existían los nuevos paquetes y sus versiones se borraron,
**a petición explícita del usuario**, solo estos tres archivos:

- `artifacts/installers/CSIViewer-Setup-1.0.0.exe`
- `artifacts/installers/CSIViewer.Host-Setup-1.0.0.exe`
- `artifacts/installers/CSIViewerServidor-Setup-1.0.0.exe`

Se conserva `artifacts/backups/CSIViewer-1.0.0-20261113-183951.zip`, comprobado con
su SHA-256 original. Contiene también los instaladores anteriores. No se borraron
fuentes, payloads, documentación ni el archivo de seguridad. Los informes previos
que indican conservar instaladores sueltos reflejan el estado anterior a esta petición.

No se instalaron automáticamente los paquetes. Sigue pendiente la confirmación
del usuario con sus dispositivos reales. TeamViewer no se detuvo.
