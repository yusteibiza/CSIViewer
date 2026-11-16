# Defecto JPEG demostrado y límite de la corrección

## Evidencia real del invitado

`Host-vmware03-20261115-104059.zip`, Build `diag-20261115-stage-errors`,
contiene la primera excepción JPEG a09:29:44.863 UTC:

`COMException 0x88982F41`, fase3, en
`JpegEncoder.EncodeCoreAsync → BitmapProperties.SetPropertiesAsync`.

El HRESULT es WINCODEC_ERR_PROPERTYNOTSUPPORTED. ImageQuality se estaba
tratando como metadata después de crear el encoder, con valor double y tipo
declarado Single. Es una opción del códec que debe pasarse al crear el encoder,
con valor real float.

El host mantuvo captura y reintentó JPEG4869 veces sin éxito; el contador
EncodeDone/envío quedó detenido durante ese intervalo WGC. La instrumentación
nueva permitió recuperar la excepción que antes solo se emitía como Debug.

## No confundir dos episodios

Tras cambiar manualmente aGDI y guardar a09:35:37 UTC se recreó captura/encoder
H264. A09:40:56–09:40:58, en la nueva congelación comunicada, GDI/H264 enviaba
fotogramas; no había nuevos errores JPEG. Los4869 errores eran **históricos**,
anteriores a ese reinicio. Ese otro síntoma sigue abierto: el avance de
contadores no demuestra contenido nuevo ni presentación física.

Asimismo, una antigua sospecha JPEG quedaba pendiente después de volver a
codificar H264 con éxito. Se corrige retirando ese estado pendiente cuando
hay EncodeDone, sin borrar contadores ni historial de errores.

## Cambio mínimo del códec

En `WindowsGraphicsCapture.cs` (JpegEncoder) y `GdiScreenCapture.cs`:

- `Math.Clamp(quality / 100f, 0.2f, 0.95f)`, PropertyType.Single.
- `BitmapEncoder.CreateAsync(JpegEncoderId, stream, properties)`.
- Eliminar SetPropertiesAsync(ImageQuality) por la ruta de metadata.

No se cambian preferencias, protocolos, WGC/H264, fallback, reinstalan drivers
ni se reinician componentes automáticamente.

## Pruebas locales y su alcance

- Ocho pruebas nuevas iniciales de roundtrip/scaling/clamping/32 imágenes
  sintéticas seguidas pasaban incluso ANTES en el físico. No se reprodujo aquí
  el HRESULT específico del Windows10 invitado: no inventar un fallo rojo.
- Dos pruebas adicionales reales comprobaron que calidad95 debe producir
  más bytes que calidad20 en ruido determinista128×128. **Ambas fallaron antes**
  (ruta común y GDI) y pasan después. Se estaba ignorando calidad en el físico.
  Evidencia: `jpeg-quality-before.trx`.
- Tras corregir, **10/10 casos JPEG** correctos (`jpeg-options-fixed.trx`).
  Todos operan sobre memoria sintética, sin escritorio/input/red/servicios/DB.
- Se incorpora una prueba del retiro de sospecha JPEG histórica y seis de
  muestreo RGB acotado; la regresión completa se registra en FINAL_AUDIT.

## Observabilidad para el síntoma restante

`FrameContentProbe` compara en memoria una rejilla de hasta2304 píxeles RGB.
Solo se emite cambio/no-cambio/número de muestras, nunca datos ni fingerprint.
Se observa captura entregada BGRA y salida H264 decodificada, con ID correlado.
No se calcula nada si el diagnóstico está desactivado.
Las pruebas cubren primer/repetido/cambio, alpha ignorado, dimensiones inválidas,
datos truncados, overflow, cambio de tamaño y limitación de cambios fuera de
rejilla. No afirmar igualdad de imagen completa por igualdad de muestras.

## Pendiente

Validar JPEG corregido en la VM Windows10 afectada y repetir el caso GDI/H264
con cambios de contenido locales claramente visibles. No declarar resuelta la
congelación ni generar instalador definitivo.

Referencias Microsoft:
- https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.bitmapencoder.createasync
- https://learn.microsoft.com/en-us/windows/win32/wic/-wic-codec-error-codes
