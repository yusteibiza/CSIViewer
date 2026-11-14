# Contactos: contraseña guardada y color del rótulo — 1.0.1

- Corrección del acceso a remoto desde Contactos en visor/host: recupera
  ProtectedPassword con SecretProtector y la misma entropía que Dispositivos.
  Antes asignaba PendingPassword=null de forma incondicional.
- Precarga ID/contraseña y muestra revisión de permisos igual que Dispositivos;
  no conecta automáticamente ni acepta permisos. No registra contraseñas.
- Al navegar a un equipo sin contraseña recuperable, limpia el PasswordBox para
  no reutilizar por error la contraseña del destino anterior en la página cacheada.
- Aclaración del usuario: el color era para el TEXTO «Equipos vinculados» y su
  fondo, no para los botones. Se aplica una etiqueta verde temática con fondo;
  botones vuelven al estilo secundario normal. Se conserva el espaciado mejorado.
- Recursos HighContrast siguen usando colores del sistema.

Validación Debug/Release limpia: 283/283 por configuración. Contratos de precarga/
limpieza en ambos proyectos, más las pruebas existentes del protector de secretos.
UI nativa Release Light/Dark: 21/21, rótulo verde y botones neutros, separación,
hover y ausencia de franja azul. Captura Dark revisada. Informe
`artifacts/diagnostics/contacts-label-credentials-viewer-release.json`.
La prueba UI visual no valida autenticación desde Contactos contra un host real;
queda pendiente confirmar ese flujo con credenciales guardadas del usuario.
Publicación/empaquetado3 en curso. Manifiesto previsto
`artifacts/diagnostics/contacts-label-credentials-installers-1.0.1.json`.
Los instaladores deben quedar directamente en artifacts/installers/, nunca
entregarse en subcarpetas. No ejecutar instaladores ni tocar servidor instalado.
