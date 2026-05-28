# Build & Publish — NFC Access Manager
# Genera un único .exe autocontenido para Windows x64
# Requiere: .NET 8 SDK (https://dotnet.microsoft.com/download)

$ErrorActionPreference = "Stop"

$project = "src\EjecutableNFC.csproj"
$output  = "dist"

Write-Host ""
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host "  NFC Access Manager — Build Script" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Verificar que el SDK está disponible
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: 'dotnet' no encontrado. Instala el .NET 8 SDK desde:" -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

Write-Host "Restaurando paquetes NuGet..." -ForegroundColor Gray
dotnet restore $project

Write-Host ""
Write-Host "Publicando ejecutable autocontenido..." -ForegroundColor Gray
dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=embedded `
    --output $output

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Listo! Ejecutable generado en: $output\EjecutableNFC.exe" -ForegroundColor Green
    Write-Host ""
    # Abrir la carpeta en el Explorador
    Start-Process explorer.exe $output
} else {
    Write-Host ""
    Write-Host "ERROR: La compilación falló con código $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}
