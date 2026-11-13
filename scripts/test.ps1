$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root "CSIViewer.slnx") -c Debug
dotnet test (Join-Path $root "tests/CSIViewer.UnitTests/CSIViewer.UnitTests.csproj") -c Debug --no-build
