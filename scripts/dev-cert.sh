#!/bin/sh
# TB0 (docs/PLAN.md §2.4): certificado de desarrollo para el TLS del gateway.
# Exporta el certificado HTTPS de desarrollo de .NET a infra/certs, que el compose monta en el gateway.
# Además genera (una sola vez) los certificados con los que Identity firma y cifra los tokens en Development, para
# que las sesiones y los refresh tokens sobrevivan a un reinicio de Identity (docs/problemas-conocidos.md §6).
set -eu
cd "$(dirname "$0")/.."
mkdir -p infra/certs
dotnet dev-certs https -ep infra/certs/gateway.pem --format PEM --no-password
# El contenedor corre con un usuario sin privilegios distinto del tuyo y necesita leer el certificado y la clave.
# Es un certificado solo para localhost y el directorio está en .gitignore.
chmod 644 infra/certs/gateway.pem infra/certs/gateway.key
echo "Listo: infra/certs/gateway.pem y infra/certs/gateway.key."

# OpenIddict exige el uso de clave correcto: firma (digitalSignature) y cifrado (keyEncipherment).
identity_cert() {
    name=$1
    usage=$2
    if [ -f "infra/certs/$name.pem" ] && [ -f "infra/certs/$name.key" ]; then
        echo "Sin cambios: infra/certs/$name.pem ya existe."
        return
    fi
    openssl req -x509 -newkey rsa:2048 -nodes -days 730 -sha256 \
        -subj "/CN=Secunitec Identity $name (desarrollo)" \
        -addext "keyUsage=critical,$usage" \
        -keyout "infra/certs/$name.key" -out "infra/certs/$name.pem" 2>/dev/null
    chmod 644 "infra/certs/$name.pem" "infra/certs/$name.key"
    echo "Listo: infra/certs/$name.pem y infra/certs/$name.key."
}
identity_cert identity-signing digitalSignature
identity_cert identity-encryption keyEncipherment

echo "Para que el navegador confíe en https://localhost:8080, ejecuta una vez: dotnet dev-certs https --trust"
