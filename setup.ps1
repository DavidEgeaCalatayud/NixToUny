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

Write-Host "Compilando..." -ForegroundColor Cyan
dotnet build NixToUny.sln --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build ha fallado." }

Write-Host ""
Write-Host "Hito 1 preparado correctamente." -ForegroundColor Green
Write-Host "Abre: $root\NixToUny.sln" -ForegroundColor Green
