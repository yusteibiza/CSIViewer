# Despliegue del servidor y del cliente

Esta guía cubre la publicación y operación de la build actual. El servidor de desarrollo usa HTTP; producción exige HTTPS.

---

## 1. Publicar el servidor

```powershell
dotnet publish src/CSIViewer.Server/CSIViewer.Server.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/server
```

Salida: `artifacts/server/CSIViewerServidor.exe` y dependencias.

Configuración recomendada mediante la interfaz (también es el modo predeterminado sin argumentos):

```powershell
artifacts\server\CSIViewerServidor.exe
```

La configuración se guarda en `data\serversettings.json`, junto al ejecutable. Si esa carpeta no es escribible, se usa `%ProgramData%\CSIViewerServidor`. En la interfaz, **Iniciar con Windows** registra `CSIViewerServidor.exe --modo=ui` en el inicio de sesión del usuario y evita mostrar una consola. También se puede configurar sin interfaz:

```powershell
artifacts\server\CSIViewerServidor.exe --modo=servidor `
  --port=5080 --all-interfaces `
  --enrollment-code=CAMBIA-ESTE-CODIGO `
  --db=D:\CSIViewer\data\server.db
```

La base de datos predeterminada es `data\server.db` junto al ejecutable. También se puede cambiar con la variable `CSIVIEWER_SERVER_DB`.

Certificado HTTPS:

```powershell
.\scripts\new-server-certificate.ps1 -Machine
```

Guarda la huella y configura el servidor:

```powershell
CSIViewerServidor.exe --modo=servidor --https --cert-thumbprint=HUELLA
```

El certificado público exportado debe importarse en los clientes en Entidades de certificación raíz de confianza.

Servidor STUN opcional:

```powershell
CSIViewerServidor.exe --modo=servidor --stun --stun-port=3478
```

Abre UDP 3478 en el firewall y en el router. El descubrimiento STUN ya está integrado; el hole punching ICE completo queda pendiente.

---

## 2. HTTPS con Kestrel

Genera o instala un certificado válido para el nombre del servidor y configura Kestrel:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://0.0.0.0:443",
        "Certificate": {
          "Path": "C:\\Certificates\\csiviewer.pfx",
          "Password": "CAMBIA-ESTA-CONTRASENA"
        }
      }
    }
  }
}
```

Alternativas:

- Certificado del almacén de Windows (`CertificateStore`).
- Proxy inverso IIS/nginx con TLS y soporte WebSocket.
- Certificado Let's Encrypt renovado automáticamente.

En el cliente, configura la URL como `https://servidor:443`. El cliente construye `wss://servidor:443/ws` automáticamente.

**No desactives la validación TLS en producción.** Para un servidor autohospedado con CA privada, instala la CA en el equipo cliente.

---

## 3. Servicio de Windows

```powershell
.\scripts\install-server-service.ps1 -AllInterfaces
```

O manualmente:

```powershell
sc.exe create CSIViewerServidor `
  binPath= "C:\CSIViewer\server\CSIViewerServidor.exe --modo=servicio" `
  start= auto `
  DisplayName= "CSIViewer Servidor"

sc.exe description CSIViewerServidor "Servidor de señalización y relay de CSIViewer"
sc.exe start CSIViewerServidor
```

Detener/eliminar:

```powershell
sc.exe stop CSIViewerServidor
sc.exe delete CSIViewerServidor
```

La cuenta del servicio debe tener permiso de lectura/escritura sobre el directorio de datos y el certificado. Para producción se recomienda una cuenta de servicio dedicada, no una cuenta de administrador interactiva.

---

## 4. Firewall y red

| Puerto | Protocolo | Uso |
| --- | --- | --- |
| 443 | TCP | HTTPS y WebSocket/WSS |
| 5080 | TCP | Servidor HTTP/HTTPS y WebSocket |
| 3478 | UDP | Servidor STUN opcional |
| 3478 | UDP | STUN reservado para la fase P2P |
| 3479 | UDP | Relay UDP reservado para la fase P2P |

En la build actual, dos equipos en la misma LAN pueden usar TCP directo. A través de Internet, el tráfico de sesión pasa por el relay WSS del servidor. Abrir 443/TCP es suficiente para funcionar a través de Internet.

Si se publica detrás de un proxy, debe permitir:

- Conexiones WebSocket de larga duración.
- Timeout de inactividad superior a 60 s.
- Tamaño de mensaje suficiente para chunks de 1 MiB (holgura recomendada: 4 MiB).

---

## 5. Cliente

Publicación:

```powershell
dotnet publish src/CSIViewer/CSIViewer.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/client
```

En **Configuración > Servidor** introduce la URL HTTPS y el código de inscripción. El cliente guarda la identidad en `data\identity.json` junto al ejecutable, protegida con DPAPI.

### MSIX

El proyecto genera un MSIX de un solo proyecto:

```powershell
.\scripts\build-msix.ps1
```

Salida aproximada:

```text
src/CSIViewer/AppPackages/CSIViewer_0.1.0.0_x64_Test/CSIViewer_0.1.0.0_x64.msix
```

El paquete actual no está firmado (`AppxPackageSigningEnabled=false`). Para instalarlo fuera de modo desarrollador:

1. Crear un certificado de firma de confianza.
2. Firmar el MSIX con `signtool`.
3. Instalar el certificado en `Trusted People` del equipo de prueba.

Para distribución profesional falta:

- Certificado de firma EV/Authenticode y firma del MSIX.
- Actualizador firmado.
- Política de privacidad y aviso de consentimiento.
- Pruebas en máquinas limpias Windows 10/11.

---

## 6. Copias de seguridad

- Detén el servicio o usa una copia consistente de SQLite.
- Copia `server.db` y `server.db-wal`/`server.db-shm` si existen.
- Conserva el certificado y su contraseña en un almacén seguro.
- Prueba la restauración en un equipo distinto antes de confiar en ella.

---

## 7. Limitaciones actuales del despliegue

- Un solo nodo; no hay alta disponibilidad.
- La LAN usa TCP directo con fallback al relay; no hay P2P/TURN para Internet. La consulta STUN de diagnóstico está implementada.
- El relay consume ancho de banda del servidor; dimensiona la máquina y aplica cuotas.
- No hay panel de administración web; la inscripción se controla con el código de `appsettings.json` y la base de datos.
- No hay rotación automática de certificados.
- No hay métricas Prometheus/OpenTelemetry; los logs son JSONL/consola.
