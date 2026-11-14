# Rechazo incorrecto de TrustedInstaller — 1.0.1

## Causa observada en la instalación del usuario

Inspección de metadatos de solo lectura, sin ejecutar las aplicaciones:

- Host instalado y abierto desde `C:\Program Files\CSIViewer Host`, versión1.0.1.0.
- Agente presente. Carpeta y ejecutables propiedad de Administradores/SYSTEM.
- En directorios de host/agente/login hay una ACE heredada de control total para
  **NT SERVICE\TrustedInstaller**, el servicio instalador de componentes Windows.
- El gestor de arranque solo confiaba en SYSTEM/Administradores y rechazaba esa
  ACE normal de Program Files. El aviso genérico sugería reinstalar y ocultaba
  el motivo real. No era una ruta incorrecta del usuario.
- Se leyeron metadatos de229 elementos del agente y257 del login: no se encontraron
  otros propietarios ni permisos de escritura fuera de los tres principales
  administrativos. No se han leído contraseñas, bases de datos ni perfiles.
- `Get-ScheduledTask` no encuentra `CSIViewerHost`. No se crea/elimina/inicia tarea.
- Servicio `CSIViewerServidor` en ejecución, automático, LocalSystem. No se toca.

Resumen sin secretos: `artifacts/diagnostics/host-startup-installed-metadata-1.0.1.json`.

## Corrección

- Política compartida `src/Shared/ProtectedInstallation.cs`, enlazada en visor,
  host, agente y componente de login, para evitar divergencias.
- Binarios de instalación: acepta SYSTEM, Administradores y TrustedInstaller,
  por SID independiente del idioma. Sigue rechazando propietarios de usuario,
  escritura no administrativa y rutas redirigidas.
- **Datos privados de login siguen limitados a SYSTEM/Administradores**; no se
  amplía su ACL ni se cambian permisos existentes.
- ACE `InheritOnly` no concede acceso al objeto actual y se omite al evaluarlo;
  todos los hijos se verifican individualmente. Se inspecciona cada directorio
  antes de descender, sin seguir junctions/symlinks.
- Revalidación dentro de la operación elevada antes de registrar la tarea.
- Misma corrección en scripts de instalación/gestión y validadores de login.
- Errores del panel distinguen archivo ausente, propietario incorrecto, escritura
  insegura, redirección y permisos inaccesibles; incluyen ruta y motivo.
- Ausencia de tarea COM también reconoce FileNotFoundException con HRESULT
  exacto, no únicamente COMException. Acceso denegado nunca equivale a ausencia.
- Instalador host solicita administración y todos los usuarios por defecto;
  conserva la selección explícita de instalación por usuario para uso no SYSTEM.

## Alcance de la entrega

Se mantienen configuración global de tres botones, conexiones, HTTP explícito,
credenciales e identidades, tarea SYSTEM→agente y servidor independiente.
No se instalan los nuevos binarios, no se modifican permisos del equipo, ni se
detienen procesos/servicios. Compilación/publicación y empaquetado autorizados.
**No se ejecutan pruebas**; activación real y Winlogon quedan pendientes del usuario.

Tres instaladores1.0.1.0 en raíz `artifacts/installers/`, fecha común y hashes en
`artifacts/diagnostics/trustedinstaller-startup-installers-1.0.1.json`.

Entrega final: **14/11/2026 18:27:12 +01:00**. Compilación Release sin errores ni
advertencias, cinco payloads publicados y tres instaladores generados. Catálogos
es/en/fr/ca actualizados. No se ejecutan pruebas ni instaladores.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | C9002844E5ED1637D34603FF9C77F6F7F80911C384F41122B9D15D06E82D8318 |
| CSIViewer.Host-Setup-1.0.1.exe | F65AE0F52B8D4ACA8E8768BF95964456861938326255DC54195BF3E123926C59 |
| CSIViewerServidor-Setup-1.0.1.exe | 96D268EE79DB4C0BA621364D88DED1A88A81CAAE1181E4B157682098E4059C2B |
