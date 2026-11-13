# Idiomas e indicadores nativos — trabajo 1.0.1

## Implementación

- Catálogos JSON es/en/fr/ca integrados en Core, sin traducción ni red en ejecución.
- Selector persistente por aplicación junto a tema y énfasis, también en ajustes compactos.
- Propiedades XAML adjuntas y traducción de textos C# sin recrear páginas o sesiones.
- La etiqueta seleccionada del ComboBox usa un TextBlock actualizable y nombre accesible explícito; no se cambia la selección para refrescarla.
- Correcciones contextuales del administrador de archivos y de «ID —».
- Navegación de ajustes del visor con `NavSettings`, independiente del idioma.
- Tarjetas compactas del host en una columna bajo 560 DIP, tanto en visor como en host independiente.

## Énfasis e indicadores

`AccentAppearance.SetBrush` no debe modificar `SolidColorBrush.Color` sobre el
resultado de una búsqueda de recursos. Los aliases de WinUI pueden compartir
instancias: cambiar el texto de los botones alteraba también indicadores nativos.
Se crean pinceles propios; no se sobrescriben TextOnAccentFillColorPrimaryBrush
ni TextOnAccentFillColorSecondaryBrush. Los aliases de fondo/acento de controles
comunes se personalizan explícitamente para no perder el color elegido.

Inspección de capturas reales del host: círculo activado blanco en Light y negro
en Dark, conforme a generic.xaml de WinUI 2.6.5. Mantener también las reglas de
alto contraste, sin imponer blanco universalmente ni cambiar Windows.

## Evidencia actual

- Compilaciones sin avisos/errores; 221/221 tests Debug y Release, incluidas cuatro regresiones de ID.
- `artifacts/diagnostics/languages-{viewer,host,server}-debug.json`: 18 + 18 + 17 comprobaciones superadas.
- `artifacts/diagnostics/accent-native-{viewer,host,server}-debug.json`: 12 + 12 + 9 comprobaciones superadas.
- Capturas reales PrintWindow junto a cada JSON; revisadas visor francés a 1000 px y host francés a 640/780 px.
- `artifacts/diagnostics/languages-{viewer,host,server}-release.json`: 18 + 18 + 17 comprobaciones superadas.
- Último retoque francés («Tester», «Actualiser l’état», etc.) posterior a esas capturas: Release recompilado y 221/221 tests repetidos, sin afirmar cobertura visual exhaustiva.

El servidor de pruebas usa instancia/directorio aislados. No se ha detenido ni
modificado el servidor instalado, sus servicios o aplicaciones instaladas.

## Pendientes antes de publicar

- Revisión contextual adicional de traducciones y textos dinámicos/cacheados.
- Diálogos, ayuda, administrador de archivos e idioma durante una sesión activa.
- Repetir regresiones de sesiones/archivos/fullscreen; más DPI y alto contraste.
- Audio del sistema remoto al visor y portapapeles de texto bidireccional: todavía
  no implementados, requieren permisos, habilitación explícita y limpieza por sesión.
- Publicación secuencial y nuevos instaladores cuando estén terminadas las funciones.

Los instaladores existentes 1.0.1 siguen siendo los de ayuda/énfasis: no incluyen
este trabajo. El ZIP completo 1.0.0 permanece intacto.
