#!/bin/sh
# TB0 (docs/PLAN.md §2.4): certificado de desarrollo para el TLS del gateway.
# Exporta el certificado HTTPS de desarrollo de .NET a infra/certs, que el compose monta en el gateway.
set -eu
cd "$(dirname "$0")/.."
mkdir -p infra/certs
dotnet dev-certs https -ep infra/certs/gateway.pem --format PEM --no-password
# El contenedor corre con un usuario sin privilegios distinto del tuyo y necesita leer la clave.
# Es un certificado solo para localhost y el directorio está en .gitignore.
chmod 644 infra/certs/gateway.key
echo "Listo: infra/certs/gateway.pem y infra/certs/gateway.key."
echo "Para que el navegador confíe en https://localhost:8080, ejecuta una vez: dotnet dev-certs https --trust"
