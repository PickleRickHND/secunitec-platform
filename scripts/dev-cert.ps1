# TB0 (docs/PLAN.md §2.4): certificado de desarrollo para el TLS del gateway (Windows PowerShell).
# Exporta el certificado HTTPS de desarrollo de .NET a infra/certs, que el compose monta en el gateway.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force -Path 'infra/certs' | Out-Null
dotnet dev-certs https -ep infra/certs/gateway.pem --format PEM --no-password
if ($LASTEXITCODE -ne 0) { throw 'No se pudo exportar el certificado de desarrollo.' }
Write-Host 'Listo: infra/certs/gateway.pem y infra/certs/gateway.key.'
Write-Host 'Para que el navegador confie en https://localhost:8080, ejecuta una vez: dotnet dev-certs https --trust'
