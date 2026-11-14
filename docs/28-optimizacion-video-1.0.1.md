# Optimización de vídeo — 1.0.1

El usuario informa de ~20,7 FPS a 2560×1440 con objetivo 60, dec 47,5 ms y UI
3,7 ms. Solicita revisar y mejorar el rendimiento, sin tocar instalaciones.
TeamViewer permanece abierto por ahora: la comparación WAN posterior debería
hacerse sin su carga/captura simultánea, manteniendo una vía de acceso segura.

## Cambios

- Conversión NV12 → BGRA en bloques de ocho píxeles AVX2/SSSE3; alternativa
  escalar para procesadores sin esas instrucciones. Aritmética fija BT.601,
  chroma compartido entre dos filas y bloques de 32 filas con máximo cuatro
  trabajadores, reduciendo la granularidad por fila y limitando competencia con UI.
- Se conservan strides y altura de plano codificada (p. ej. 1088 para 1080),
  alfa opaco y validación de tamaños antes de accesos vectoriales. Diferencia
  máxima prevista de un nivel por canal frente a la fórmula flotante anterior.
- Array BGRA sin inicialización redundante, con escritura de todos sus bytes.
- El decodificador reutiliza buffer/muestra de salida nativos en lugar de crear
  buffers de más de 7 MiB en cada salida y cada NEED_MORE_INPUT. Se invalidan al
  renegociar el formato. La disposición NV12 se consulta una vez por formato.
- Cada frame publicado conserva su propio array: reutilizar la muestra nativa
  no puede sobrescribir una imagen que todavía esté mostrando WinUI.
- Instrumentación de transformación/copias y conversión por separado en el
  benchmark. No se cambian red, cifrado, protocolos, calidad, resolución, permisos,
  audio, entrada, ni política de descartar cuadros para evitar acumulación.

## Mediciones locales iniciales (mediana, Release)

| Operación | Resolución | Antes | Optimizado |
| --- | --- | --- | --- |
| NV12 → BGRA | 1920×1080 | 4,89 ms | 1,19 ms |
| NV12 → BGRA | 2560×1440 | 7,66 ms | 2,04 ms |
| H.264 completo + color | 1920×1080 | 6,67 ms | 4,20 ms |
| H.264 completo + color | 2560×1440 | 11,27 ms | 7,44 ms |

Informes: `video-baseline-1.0.1.json`, `video-simd-1.0.1.json` y
`video-optimized-1.0.1.json` en artifacts/diagnostics. Son pruebas sintéticas
locales y muestras cortas, no FPS de la sesión casa/oficina ni garantía de 60 FPS.
El benchmark conserva la fórmula antigua únicamente como referencia diagnóstica
para comparar A/B dentro del mismo proceso.

## Verificación

- Debug/Release: 263/263 tests en cada configuración, sin avisos/errores.
- 15/15 regresiones de píxel/códec también con DOTNET_EnableAVX2=0.
- Comparación de conversión vectorial/escalar byte a byte, referencia flotante
  con tolerancia máxima de un nivel, dimensiones pequeñas/colas vectoriales,
  stride y padding, rechazo de buffers truncados y overflow.
- Roundtrip H.264 incluye 2560×1440; comprueba que no hay banda verde, que no se
  crean buffers de salida por cuadro y que los arrays retenidos no se alteran.
- A/B final en el mismo proceso (`video-optimized-ab-1.0.1.json`): conversión
  1080p 4,81→0,87 ms y 1440p 7,73→1,61 ms. Decode completo optimizado: 3,74 ms
  en 1080p y 5,72 ms en 1440p. La referencia de decode completo inicial fue de
  otra ejecución; solo la comparación de conversión es A/B en el mismo proceso.
- Sesión local real cifrada (`video-session-release.json`): cuatro comprobaciones
  aprobadas, vídeo BGRA válido 2560×1440, mediana decode 6,38 ms y p95 12,57 ms.
  Se observaron 15 cuadros con escritorio prácticamente estático; no demuestra
  60 FPS sostenidos ni incluye renderizado WinUI o latencia WAN.
- Suite de sesión real de audio/clipboard/chat: 31/31 comprobaciones aprobadas.
- No se han actualizado aplicaciones instaladas. La nueva tanda de instaladores
  (tres con misma fecha) está terminada; manifiesto:
  `artifacts/diagnostics/video-optimized-installers-1.0.1.json`.

## Entrega final

Los tres instaladores 1.0.1 se regeneraron con fecha/hora de modificación común
**14/11/2026 01:07:01 +01:00**. Publicaciones y compiladores sin advertencias.
Hashes verificados; las DLL Media de los payloads visor/host coinciden con la
compilación Release optimizada y no incluyen datos, logs, settings ni PDB.

| Instalador | SHA-256 |
| --- | --- |
| CSIViewer-Setup-1.0.1.exe | D2EBC8938FF1E4B8AEDBF1178D1AF7F05AD1BD387D99C7B36CC1A7BE00AE86D4 |
| CSIViewer.Host-Setup-1.0.1.exe | 38C7D3411656D7BC49DAE0E96291F37EB4F06D30D75C272E9AC8952EC2E5897C |
| CSIViewerServidor-Setup-1.0.1.exe | D875333B35EA433B85C4161302FBB7250B9125A0E01EE06D2E5FCDAAC37B3183 |

No se ejecutó ningún instalador ni se modificó el servidor instalado. El ZIP
1.0.0 mantiene su hash original. No hay commit ni push.
