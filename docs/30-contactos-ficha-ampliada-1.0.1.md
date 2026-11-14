# Contactos: ficha ampliada y selector de dispositivos — 1.0.1

## Cambios

- Editar y Eliminar comparten contenedor vertical, ancho estirado y mínimo 96 DIP;
  el ancho puede crecer con las traducciones, pero siempre es igual para ambos.
- Ficha compartida por visor/host con tarjetas temáticas, separación de campos,
  ancho adaptado a la ventana y scroll vertical. Correo/teléfono pasan a una sola
  columna en ventanas estrechas.
- Además de nombre y notas: empresa, correo, teléfono y favorito opcionales.
  Favoritos aparecen primero y con estrella; datos opcionales visibles en la fila.
- Dispositivos vinculados en DropDownButton con aspecto de combo y selección
  múltiple en su desplegable. No se usa ComboBox simple porque perdería la
  asociación de varios equipos que ya soportaba Contactos.
- Búsqueda por alias/ID, resumen de seleccionados y Desvincular todos. Filtrar no
  elimina selecciones ocultas. Vínculos a IDs ya no guardados se conservan como
  opciones explícitas, para que editar otros datos no los borre silenciosamente.
- Guardar valida nombre no vacío y correo si se introduce. Cancelar no altera
  datos ni selección. No se ejecutan enlaces de correo/teléfono.
- La lista se refresca al cargar contactos/dispositivos de forma asíncrona o al
  cambiarlos, con publicaciones UI coalescidas. Evita una lista vacía si se abre
  Contactos antes de que termine la inicialización de la base local.

## Persistencia

Contact conserva los parámetros previos y añade campos opcionales al final.
SQLite añade cuatro columnas con valores por defecto en una transacción,
consultando el esquema para permitir inicializaciones repetidas. No se borran
contactos, dispositivos, contraseñas guardadas ni otros datos. Los datos de
contactos siguen siendo locales; no se envían al servidor.

## Validación en curso

- Primera suite Debug: 269/269 pruebas (dos nuevas de repositorio/migración),
  con una advertencia de estilo de test ya corregida usando indexación directa.
- Roundtrip de todos los campos y varios dispositivos; favorito ordenado primero,
  edición de campos vacíos y otro contacto conservado.
- Esquema anterior simulado en SQLite temporal: actualización repetida conserva
  nombre, notas y dispositivos y asigna valores por defecto a los campos nuevos.
- Contrato XAML de igualdad de anchos en visor/host.
- Debug/Release limpios: 269/269 por configuración. Catálogos es/en/fr/ca: 964 entradas por idioma,
  nuevas traducciones revisadas y claves retiradas podadas.
- UI nativa inicial: 22/22, incluidos guardar/cancelar, búsqueda multiselección,
  validación y persistencia tras reiniciar. Se amplía la prueba a Dark y ventana
  estrecha; el aviso de validación se limpia solo cuando se corrige su campo,
  evitando que un TextChanged tardío oculte un error todavía válido.
- Los chips de dispositivos de cada fila ajustan a varias líneas si no caben.
- Ayuda del visor actualizada para los campos, favorito y selección múltiple.
- UI nativa final: 28/28 en Debug y 28/28 en Release; campos, igualdad de botones,
  selección múltiple/búsqueda, cancelar, validar, guardar y reiniciar. Tema oscuro
  y ventana de 640 px: campos dentro de límites y correo/teléfono en una columna.
- Capturas finales revisadas en `contact-editor-viewer-release-final.json.*.png`.
  El primer flyout parecía transparente por capturarse durante su animación;
  esperar 300 ms en el harness confirmó el fondo y texto correctos, sin cambiar
  el estilo de producción.
- Regresión hover Release: 9/9, Light y Dark, con cambio real de píxeles.
- Compilación/publicación de los tres instaladores terminada. Manifiesto
  final: `artifacts/diagnostics/contact-details-installers-1.0.1.json`.
  Se mantiene versión 1.0.1 y misma fecha/hora de modificación de la tanda.

No se modifican aplicaciones instaladas, el servidor en ejecución ni el ZIP
original 1.0.0. Sin commit ni push.

## Instaladores finales

Versión 1.0.1.0, fecha/hora de modificación común **14/11/2026 10:57:01 +01:00**.
Hashes verificados y payloads visor/host sin bases de datos, settings, logs ni PDB.
No se ejecutaron instaladores.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | 7E07102C594D209ADDEABCEA72FB4C0B082B112E49E0E7361FB97E5DA9DAECCF |
| CSIViewer.Host-Setup-1.0.1.exe | 03681B4A93BBD0CFABB11FB45B222A113E6B57AAD3699082FB28D6930623233B |
| CSIViewerServidor-Setup-1.0.1.exe | C02CCDB361972ACFAC0BC9D50C769D2462BD30B4CF034DC96E1D240CA363CDA7 |
