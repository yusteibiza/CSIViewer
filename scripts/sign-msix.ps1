param(
    [string]$Msix = "",
    [string]$Pfx = "",
    [string]$Password = "",
    [string]$TimestampUrl = "",
    [switch]$GenerateDevCertificate,
    [switch]$Trust
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidate = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($candidate) {
        return $candidate.FullName
    }

    throw "No se encontró signtool.exe. Instala Windows SDK."
}

if (-not $Msix) {
    $Msix = Get-ChildItem (Join-Path $root "src/CSIViewer/AppPackages") -Recurse -Filter "*.msix" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Msix -or -not (Test-Path $Msix)) {
    throw "No se encontró el MSIX. Ejecuta scripts/build-msix.ps1."
}
$Msix = (Resolve-Path $Msix).Path

if ($GenerateDevCertificate -or -not $Pfx) {
    if (-not $Password) {
        $Password = [Guid]::NewGuid().ToString("N")
    }
    $certDirectory = Join-Path $root "artifacts/certs"
    New-Item -ItemType Directory -Force -Path $certDirectory | Out-Null
    $Pfx = Join-Path $certDirectory "CSIViewerDev.pfx"
    $cer = Join-Path $certDirectory "CSIViewerDev.cer"
    $secure = ConvertTo-SecureString -String $Password -AsPlainText -Force
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=CSIViewer" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(2)
    Export-PfxCertificate -Cert $certificate -FilePath $Pfx -Password $secure | Out-Null
    Export-Certificate -Cert $certificate -FilePath $cer -Type CERT | Out-Null
    Write-Host "Certificado de desarrollo: $($certificate.Thumbprint)"
    Write-Host "PFX: $Pfx"
    Write-Host "CER: $cer"
    Write-Host "Contraseña del PFX: $Password"

    if ($Trust) {
        Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
        Write-Host "Certificado de desarrollo importado en CurrentUser\TrustedPeople."
        Write-Host "Para que signtool /pa valide la cadena, importa el .cer manualmente en Entidades de certificación raíz de confianza."
    }
}

if (-not $Pfx -or -not (Test-Path $Pfx)) {
    throw "No se encontró el PFX: $Pfx"
}
if (-not $Password) {
    $Password = Read-Host "Contraseña del PFX" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
}

$signTool = Find-SignTool
$signArgs = @(
    "sign", "/fd", "SHA256", "/f", $Pfx, "/p", $Password
)
if ($TimestampUrl) {
    $signArgs += @("/tr", $TimestampUrl, "/td", "SHA256")
} else {
    $signArgs += "/a"
}
$signArgs += $Msix

& $signTool @signArgs
if ($LASTEXITCODE -ne 0) {
    throw "signtool sign falló con código $LASTEXITCODE."
}

& $signTool verify /pa /v $Msix
if ($LASTEXITCODE -ne 0) {
    Write-Warning "El MSIX está firmado, pero signtool no pudo validar la cadena de confianza. Usa -Trust para confiar en el certificado de desarrollo o importa el .cer en TrustedPeople."
} else {
    Write-Host "Firma verificada."
}

Write-Host "MSIX firmado: $Msix"
if ($Password) {
    Write-Host "Contraseña del PFX: $Password"
}
