# Pantalla completa: borde y controles de ventana — 13/11/2026

## Cambios

Aplicados por igual al visor y al host, sin modificar protocolo ni servidor:

- SessionCard pierde borde y esquinas redondeadas en pantalla completa. Al salir,
  ClearValue recupera los valores del estilo/tema.
- NavigationView elimina su borde y radio redundantes mediante recursos locales
  del control (también en modo ventana; las tarjetas conservan su estilo propio).
- Se desactiva IsTitleBarAutoPaddingEnabled mientras está en pantalla completa.
- La barra muestra tres botones locales, con etiquetas accesibles y tooltips:
  minimizar, maximizar y restaurar.
- Minimizar libera entrada lógica y capturas del puntero; usa ShowWindow(SW_MINIMIZE)
  sobre el mismo HWND porque FullScreenPresenter no ofrece Minimize. Al restaurar
  desde Windows mantiene pantalla completa.
- Maximizar sale de pantalla completa y maximiza un OverlappedPresenter.
- Restaurar sale a ventana normal con IsResizable/IsMaximizable/IsMinimizable
  habilitados. Permite moverla y ajustar su tamaño con los mecanismos de Windows.
- Se conserva ocultación automática, reaparición superior, chincheta y cierre remoto.

## Verificación

- Builds Debug/Release: cero errores y advertencias.
- Tests: 168/168 en cada configuración.
- UIAutomation sobre visor WinUI real: 15/15 casos en Debug y Release publicado.
  Incluye los ocho casos previos más minimizar, recuperar fullscreen, maximizar,
  salir de fullscreen al maximizar, restaurar, capacidad de resize y resize real
  a 1100×800 mediante TransformPattern. Cierre desde anfitrión de diagnóstico
  mediante RemoteClient y servidor real; no omite autenticación o permisos.
- Evidencia: `artifacts/diagnostics/ui-fullscreen-window-debug.json` y
  `artifacts/diagnostics/ui-fullscreen-window-release.json`.
- La captura `.json.png` de Release es una prueba local con captura recursiva del
  mismo monitor: puede contener la barra/imagen previamente capturada. No constituye
  por sí sola una validación visual concluyente del borde en dos equipos distintos.
  La corrección del borde se basa en los valores explícitos de CardStyle y de la
  plantilla NavigationView del SDK. Confirmación visual del usuario pendiente.
- Páginas de visor/host idénticas salvo namespace; DLL Services/Media de payloads
  idénticas. La prueba UI se ejecutó sobre el visor, no sobre la ventana del host.
- No se detuvo TeamViewer ni se actualizaron silenciosamente aplicaciones instaladas.

## Instaladores

En `artifacts/installers/`:

| Archivo | Fecha local | SHA-256 |
|---|---|---|
| CSIViewer-Setup-1.0.0.exe | 13/11/2026 18:00:00 | E60AD519AD330BD9C98E3E2CF26A46080AB4273E788DCD06B01944376A05A7E2 |
| CSIViewer.Host-Setup-1.0.0.exe | 13/11/2026 18:01:13 | D143567C9CAB0C7D1023AC11A62BDF7673A7186E6830D14EB6080E0052152A75 |

Servidor sin cambios. Pendiente prueba visual PC-YUSTE → PUESTO2 y otros DPI/monitores.
