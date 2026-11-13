param(
    [string]$DataDirectory = "$env:ProgramData\CSIViewerServidor"
)

$ErrorActionPreference = "Stop"

# The Windows service runs as SYSTEM and may create server.db, server.db-wal
# and server.db-shm with an ACL that only lets SYSTEM/Administrators write.
# The administration UI runs as the interactive user and then fails with
# "attempt to write a readonly database". Grant local Users modify rights on
# this dedicated data folder so both modes share the same database.
New-Item -ItemType Directory -Force -Path $DataDirectory | Out-Null
& icacls.exe $DataDirectory /grant "*S-1-5-32-545:(OI)(CI)M" /T /C | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "No se pudieron ajustar todos los permisos de $DataDirectory (icacls $LASTEXITCODE)."
}
