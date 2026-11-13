param(
    [switch]$Machine,
    [string[]]$DnsNames = @("localhost", $env:COMPUTERNAME),
    [string]$OutputCer = ""
)

$ErrorActionPreference = "Stop"

if ($Machine) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Para instalar el certificado en LocalMachine ejecuta como administrador."
    }
    $store = "Cert:\LocalMachine\My"
    $defaultCer = Join-Path $env:ProgramData "CSIViewerServidor\certs\server.cer"
} else {
    $store = "Cert:\CurrentUser\My"
    $defaultCer = Join-Path $env:LOCALAPPDATA "CSIViewerServidor\certs\server.cer"
}

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=CSIViewer Servidor" `
    -DnsName $DnsNames `
    -CertStoreLocation $store `
    -KeyExportPolicy Exportable `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
    -NotAfter (Get-Date).AddYears(2)

if (-not $OutputCer) {
    $OutputCer = $defaultCer
}
$directory = Split-Path -Parent $OutputCer
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Export-Certificate -Cert $certificate -FilePath $OutputCer -Type CERT | Out-Null

Write-Host "Certificado creado."
Write-Host "Huella: $($certificate.Thumbprint)"
Write-Host "Almacén: $store"
Write-Host "Certificado público: $OutputCer"
Write-Host ""
Write-Host "Para que los clientes confíen en él, importa el .cer en 'Entidades de certificación raíz de confianza'."
