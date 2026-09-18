param(
    [ValidateSet("win-x64", "win-x86")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK no esta instalado o 'dotnet' no esta en PATH."
}

Write-Host "Generando NixToUny.sln..." -ForegroundColor Cyan
if (Test-Path "NixToUny.sln") {
    Remove-Item "NixToUny.sln" -Force
}

dotnet new sln -n NixToUny
if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la solucion." }

$projects = @(
    "src/NixToUny.App/NixToUny.App.csproj",
    "src/NixToUny.Domain/NixToUny.Domain.csproj",
    "src/NixToUny.Migration/NixToUny.Migration.csproj",
    "src/NixToUny.Nixfarma/NixToUny.Nixfarma.csproj",
    "src/NixToUny.UnycopNext/NixToUny.UnycopNext.csproj",
    "src/NixToUny.Infrastructure/NixToUny.Infrastructure.csproj",
    "tests/NixToUny.Tests/NixToUny.Tests.csproj"
)

foreach ($project in $projects) {
    dotnet sln NixToUny.sln add $project
    if ($LASTEXITCODE -ne 0) { throw "No se pudo anadir $project." }
}

Write-Host "Restaurando paquetes..." -ForegroundColor Cyan
dotnet restore NixToUny.sln
if ($LASTEXITCODE -ne 0) { throw "dotnet restore ha fallado." }

Write-Host "Compilando solucion..." -ForegroundColor Cyan
dotnet build NixToUny.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build ha fallado." }

Write-Host "Ejecutando tests..." -ForegroundColor Cyan
dotnet test NixToUny.sln --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "dotnet test ha fallado." }

$dist = Join-Path $root "dist"

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null

Write-Host "Generando ejecutable autocontenido ($Runtime)..." -ForegroundColor Cyan

dotnet publish "src/NixToUny.App/NixToUny.App.csproj" `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $dist `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish ha fallado."
}

$exe = Join-Path $dist "NixToUny.exe"

if (-not (Test-Path $exe)) {
    throw "La publicacion termino pero no se encontro: $exe"
}

Write-Host ""
Write-Host "NixToUny preparado correctamente." -ForegroundColor Green
Write-Host ""
Write-Host "Ejecutable:" -ForegroundColor Green
Write-Host "  $exe" -ForegroundColor White
Write-Host ""
Write-Host "Puedes ejecutarlo directamente sin Visual Studio." -ForegroundColor Green
Write-Host ""
