param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts\installer"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root ".publish\$Runtime"
$outDir = Join-Path $root $Output
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
New-Item -ItemType Directory -Path $publish -Force | Out-Null
dotnet publish (Join-Path $root "AtlasSoftPlc.Web.csproj") -c $Configuration -r $Runtime --self-contained false -o $publish
$readme = Join-Path $publish "INSTALAR.txt"
@"
AtlasPLC Workbench

Ejecutar AtlasSoftPlc.Web.exe y abrir http://127.0.0.1:5193
Los datos se guardan en %LocalAppData%\AtlasSoftPlc.
Cambie Auth:SeedPassword y AllowedHosts antes de producción.
"@ | Set-Content -LiteralPath $readme -Encoding UTF8
$zip = Join-Path $outDir "AtlasPLC-Workbench-$Runtime.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
Write-Host "Paquete creado: $zip"
