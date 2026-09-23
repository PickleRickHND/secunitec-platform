# TB0 (docs/PLAN.md §2.4): certificado de desarrollo para el TLS del gateway (Windows PowerShell).
# Exporta el certificado HTTPS de desarrollo de .NET a infra/certs, que el compose monta en el gateway.
# Además genera (una sola vez) los certificados con los que Identity firma y cifra los tokens en Development.
# Usa el openssl de Git para Windows; sin él, Identity usa llaves efímeras (docs/problemas-conocidos.md §6).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force -Path 'infra/certs' | Out-Null
dotnet dev-certs https -ep infra/certs/gateway.pem --format PEM --no-password
if ($LASTEXITCODE -ne 0) { throw 'No se pudo exportar el certificado de desarrollo.' }
Write-Host 'Listo: infra/certs/gateway.pem y infra/certs/gateway.key.'

$openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
if (-not $openssl -and (Test-Path 'C:\Program Files\Git\usr\bin\openssl.exe')) {
    $openssl = 'C:\Program Files\Git\usr\bin\openssl.exe'
}

function New-IdentityCert([string] $name, [string] $usage) {
    if ((Test-Path "infra/certs/$name.pem") -and (Test-Path "infra/certs/$name.key")) {
        Write-Host "Sin cambios: infra/certs/$name.pem ya existe."
        return
    }
    & $openssl req -x509 -newkey rsa:2048 -nodes -days 730 -sha256 `
        -subj "/CN=Secunitec Identity $name (desarrollo)" `
        -addext "keyUsage=critical,$usage" `
        -keyout "infra/certs/$name.key" -out "infra/certs/$name.pem" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo generar infra/certs/$name.pem." }
    Write-Host "Listo: infra/certs/$name.pem y infra/certs/$name.key."
}

if ($openssl) {
    New-IdentityCert 'identity-signing' 'digitalSignature'
    New-IdentityCert 'identity-encryption' 'keyEncipherment'
} else {
    Write-Host 'Aviso: no se encontró openssl; Identity usará llaves efímeras (se pierden al reiniciar).'
}

Write-Host 'Para que el navegador confie en https://localhost:8080, ejecuta una vez: dotnet dev-certs https --trust'
