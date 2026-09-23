#!/usr/bin/env bash
# Verificación del hardening contra el compose levantado (docs/PLAN.md §5; criterio de done de 3.2).
# Uso, desde la raíz y con el perfil security arriba:  scripts/verify-hardening.sh
# Requiere curl, python3 y docker. Lee los secretos de .env (o de ENV_FILE) y confía en infra/certs/gateway.pem
# (o en CA_FILE). Consume el cupo de /connect/token (5/min por IP): para repetirlo, esperar un minuto.
set -uo pipefail
cd "$(dirname "$0")/.."

ENV_FILE=${ENV_FILE:-.env}
CA_FILE=${CA_FILE:-infra/certs/gateway.pem}
env_value() { grep -E "^$1=" "$ENV_FILE" | head -1 | cut -d= -f2-; }
BASE=$(env_value SECUNITEC_PUBLIC_URL); BASE=${BASE:-https://localhost:8080}
SECRET=$(env_value IDENTITY_JMETER_CLIENT_SECRET)
HOST=$(python3 -c "import sys, urllib.parse; print(urllib.parse.urlparse(sys.argv[1]).hostname)" "$BASE")

PASS=0
FAIL=0
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

ok() { echo "PASS  $1"; PASS=$((PASS + 1)); }
ko() { echo "FAIL  $1"; FAIL=$((FAIL + 1)); }
check() { if eval "$2"; then ok "$1"; else ko "$1"; fi; }
http() { curl -s --noproxy '*' --cacert "$CA_FILE" "$@"; }
json() { python3 -c "import json, sys; d = json.load(open(sys.argv[1])); print($2)" "$1" 2>/dev/null; }

echo "Gateway: $BASE"

# TB0 y R06: TLS y cabeceras en las respuestas del gateway.
check "TLS: HTTPS con el certificado del gateway" "http -o /dev/null $BASE/health"
http -D "$TMP/h" -o /dev/null "$BASE/.well-known/openid-configuration"
header() { grep -i "^$1:" "$TMP/h" | head -1 | cut -d: -f2- | tr -d '\r' | sed 's/^ //'; }
check "R06: sin cabecera Server" "[ -z \"\$(header Server)\" ]"
check "R06: sin cabecera X-Powered-By" "[ -z \"\$(header X-Powered-By)\" ]"
check "A05: X-Content-Type-Options nosniff" "[ \"\$(header X-Content-Type-Options)\" = nosniff ]"
check "A05: X-Frame-Options DENY" "[ \"\$(header X-Frame-Options)\" = DENY ]"
check "A05: Content-Security-Policy presente" "[ -n \"\$(header Content-Security-Policy)\" ]"
check "Correlation id en la respuesta" "[ -n \"\$(header X-Correlation-Id)\" ]"
if [ "$HOST" = "localhost" ] || [ "$HOST" = "127.0.0.1" ]; then
    echo "INFO  HSTS: no se envía para $HOST (UseHsts excluye localhost; ver docs/problemas-conocidos.md)"
else
    check "HSTS: Strict-Transport-Security presente" "[ -n \"\$(header Strict-Transport-Security)\" ]"
fi

# R03: discovery con el issuer y los endpoints públicos.
status=$(http -o "$TMP/d" -w '%{http_code}' "$BASE/.well-known/openid-configuration")
check "R03: discovery 200" "[ $status = 200 ]"
check "R03: issuer público" "[ \"\$(json $TMP/d \"d['issuer']\")\" = \"$BASE/\" ]"
check "R03: authorization_endpoint público" "json $TMP/d \"d['authorization_endpoint']\" | grep -q \"^$BASE/\""

# R04: sin token no se llega a Billing.
status=$(http -o /dev/null -w '%{http_code}' "$BASE/api/billing/facturas")
check "R04: /api/billing sin token → 401" "[ $status = 401 ]"

# Token por client credentials (jmeter-load) y acceso a Billing a través del gateway.
status=$(http -o "$TMP/t" -w '%{http_code}' -X POST "$BASE/connect/token" \
    -d grant_type=client_credentials -d client_id=jmeter-load --data-urlencode "client_secret=$SECRET" -d scope=secunitec-billing)
check "R03: token por client credentials 200" "[ $status = 200 ]"
TOKEN=$(json "$TMP/t" "d['access_token']")
status=$(http -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" "$BASE/api/billing/facturas")
check "R01: Billing a través del gateway con token → 200" "[ $status = 200 ]"
status=$(http -o /dev/null -w '%{http_code}' -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    -d '{}' "$BASE/api/billing/obligados")
check "R04: rol Facturador en endpoint de Admin → 403" "[ $status = 403 ]"

# R02: ráfaga contra /connect/token (5/min por IP) → 429 con Retry-After y JSON, sin llegar a Identity.
rejected=""
for _ in 1 2 3 4 5 6; do
    status=$(http -D "$TMP/r" -o "$TMP/rb" -w '%{http_code}' -X POST "$BASE/connect/token" -d grant_type=client_credentials)
    if [ "$status" = 429 ]; then rejected=yes; break; fi
done
check "R02: ráfaga en /connect/token → 429" "[ -n \"$rejected\" ]"
check "R02: 429 con Retry-After" "grep -qi '^Retry-After: [0-9]' $TMP/r"
check "R02: 429 con {\"error\":\"rate_limited\"}" "[ \"\$(json $TMP/rb \"d['error']\")\" = rate_limited ]"

# R12 y TB2: solo el gateway publica puertos; backend y data son redes internas.
PROJECT=$(docker compose config --format json 2>/dev/null | python3 -c "import json, sys; print(json.load(sys.stdin)['name'])")
published=$(docker compose ps --format '{{.Service}} {{.Publishers}}' 2>/dev/null | grep -v '^gateway ' | grep -cE '[0-9]+->' || true)
check "R12: solo el gateway publica puertos en el host" "[ \"$published\" = 0 ]"
for network in backend data; do
    internal=$(docker network inspect "${PROJECT}_${network}" --format '{{.Internal}}' 2>/dev/null)
    check "R12: red $network interna" "[ \"$internal\" = true ]"
done

echo
echo "Resultado: $PASS PASS, $FAIL FAIL"
[ "$FAIL" -eq 0 ]
