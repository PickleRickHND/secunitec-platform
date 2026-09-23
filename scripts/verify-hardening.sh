#!/usr/bin/env bash
# Verificación del hardening contra el compose levantado (docs/PLAN.md §5; criterios de done de 3.2 y 4.2).
# Uso, desde la raíz y con el perfil security arriba:
#   scripts/verify-hardening.sh            # imprime PASS/FAIL por control
#   scripts/verify-hardening.sh --report   # además escribe docs/04-hardening-verificacion.md con la salida real
# Si la observabilidad de la etapa 5.2 está levantada (docker-compose.observability.yml), sus contenedores entran solos
# en la verificación y se agregan los controles de TB3.
# Requiere curl, python3 y docker. Lee los secretos de .env (o de ENV_FILE) y confía en infra/certs/gateway.pem
# (o en CA_FILE). Nunca imprime secretos ni tokens: el reporte muestra códigos de estado y cabeceras.
# Consume el cupo de /connect/token (5/min por IP): si lo encuentra agotado, espera el Retry-After y sigue.
set -uo pipefail
cd "$(dirname "$0")/.."

REPORT=""
[ "${1:-}" = "--report" ] && REPORT="docs/04-hardening-verificacion.md"

ENV_FILE=${ENV_FILE:-.env}
CA_FILE=${CA_FILE:-infra/certs/gateway.pem}
env_value() { grep -E "^$1=" "$ENV_FILE" | head -1 | cut -d= -f2-; }
BASE=$(env_value SECUNITEC_PUBLIC_URL); BASE=${BASE:-https://localhost:8080}
SPA=$(env_value SECUNITEC_SPA_URL); SPA=${SPA:-http://localhost:3000}
SECRET=$(env_value IDENTITY_JMETER_CLIENT_SECRET)
HOST=$(python3 -c "import sys, urllib.parse; print(urllib.parse.urlparse(sys.argv[1]).hostname)" "$BASE")

PASS=0
FAIL=0
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
CHECKS="$TMP/checks.md"
EVIDENCE="$TMP/evidence.md"
: > "$CHECKS"
: > "$EVIDENCE"
SECTION=""

section() {
    SECTION=$1
    echo
    echo "== $1"
    printf '\n### %s\n' "$1" >> "$EVIDENCE"
}
ok() { echo "PASS  $1"; PASS=$((PASS + 1)); printf '| %s | %s | PASS |\n' "$SECTION" "$1" >> "$CHECKS"; }
ko() { echo "FAIL  $1"; FAIL=$((FAIL + 1)); printf '| %s | %s | **FAIL** |\n' "$SECTION" "$1" >> "$CHECKS"; }
info() { echo "INFO  $1"; printf '| %s | %s | INFO |\n' "$SECTION" "$1" >> "$CHECKS"; }
check() { if eval "$2"; then ok "$1"; else ko "$1"; fi; }
# Guarda en el reporte un comando (ya sin secretos) y su salida real.
evidence() {
    printf '\n```console\n$ %s\n%s\n```\n' "$1" "$2" >> "$EVIDENCE"
}
http() { curl -s --noproxy '*' --cacert "$CA_FILE" "$@"; }
json() { python3 -c "import json, sys; d = json.load(open(sys.argv[1])); print($2)" "$1" 2>/dev/null; }
header_in() { grep -i "^$2:" "$1" | head -1 | cut -d: -f2- | tr -d '\r' | sed 's/^ //'; }
headers_of() { tr -d '\r' < "$1" | grep -iE "^(HTTP/|server|x-powered-by|x-content-type-options|x-frame-options|content-security-policy|strict-transport-security|x-correlation-id|retry-after|access-control-[a-z-]+|cache-control|referrer-policy|permissions-policy):"; }

# Pide un token de aplicación (jmeter-load). Si /connect/token está limitado, espera el Retry-After y reintenta.
token_request() {
    local status
    for _ in 1 2 3; do
        status=$(http -D "$TMP/th" -o "$TMP/t" -w '%{http_code}' -X POST "$BASE/connect/token" \
            -d grant_type=client_credentials -d client_id=jmeter-load --data-urlencode "client_secret=$SECRET" -d scope=secunitec-billing)
        if [ "$status" != 429 ]; then echo "$status"; return; fi
        local wait_for; wait_for=$(header_in "$TMP/th" Retry-After); wait_for=${wait_for:-60}
        echo "INFO  /connect/token limitado: espero $((wait_for + 1)) s (Retry-After)" >&2
        sleep $((wait_for + 1))
    done
    echo "$status"
}

PROJECT=$(docker compose config --format json 2>/dev/null | python3 -c "import json, sys; print(json.load(sys.stdin)['name'])")
# 5.2: con el overlay de observabilidad corriendo, todos los `docker compose` del script lo incluyen. Sin esto sus
# contenedores quedarían fuera de la verificación sin avisar.
OBSERVABILITY=""
if [ -n "$(docker ps -q --filter "label=com.docker.compose.project=$PROJECT" --filter label=com.docker.compose.service=otel-collector)" ]; then
    OBSERVABILITY=yes
    export COMPOSE_FILE=docker-compose.yml:docker-compose.observability.yml
fi
COMPOSE_FILES=${COMPOSE_FILE:-docker-compose.yml}
echo "Gateway: $BASE   SPA: $SPA   Proyecto: $PROJECT   Compose: $COMPOSE_FILES"

section "TB0 y R06: TLS y cabeceras del gateway"
check "TLS: HTTPS con el certificado del gateway" "http -o /dev/null $BASE/health"
http -D "$TMP/h" -o /dev/null "$BASE/.well-known/openid-configuration"
evidence "curl -sD - -o /dev/null $BASE/.well-known/openid-configuration" "$(headers_of "$TMP/h")"
check "R06: sin cabecera Server" "[ -z \"\$(header_in $TMP/h Server)\" ]"
check "R06: sin cabecera X-Powered-By" "[ -z \"\$(header_in $TMP/h X-Powered-By)\" ]"
check "A05: X-Content-Type-Options nosniff" "[ \"\$(header_in $TMP/h X-Content-Type-Options)\" = nosniff ]"
check "A05: X-Frame-Options DENY" "[ \"\$(header_in $TMP/h X-Frame-Options)\" = DENY ]"
check "A05: Content-Security-Policy presente" "[ -n \"\$(header_in $TMP/h Content-Security-Policy)\" ]"
check "Correlation id en la respuesta" "[ -n \"\$(header_in $TMP/h X-Correlation-Id)\" ]"
if [ "$HOST" = "localhost" ] || [ "$HOST" = "127.0.0.1" ]; then
    info "HSTS: no se envía para $HOST (UseHsts excluye localhost; ver docs/problemas-conocidos.md §4)"
else
    check "HSTS: Strict-Transport-Security presente" "[ -n \"\$(header_in $TMP/h Strict-Transport-Security)\" ]"
fi
http -D "$TMP/hl" -o /dev/null "$BASE/account/login"
check "Identity conserva su CSP propia a través del gateway (estilos 'self')" "header_in $TMP/hl Content-Security-Policy | grep -q \"^default-src 'self'\""

section "R03: discovery"
status=$(http -o "$TMP/d" -w '%{http_code}' "$BASE/.well-known/openid-configuration")
discovery=$(python3 - "$TMP/d" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
print(json.dumps({k: d.get(k) for k in ["issuer", "authorization_endpoint", "token_endpoint", "end_session_endpoint"]}, indent=2))
PY
)
evidence "curl -s $BASE/.well-known/openid-configuration | jq '{issuer, authorization_endpoint, token_endpoint, end_session_endpoint}'" "$discovery"
check "R03: discovery 200" "[ $status = 200 ]"
check "R03: issuer público" "[ \"\$(json $TMP/d \"d['issuer']\")\" = \"$BASE/\" ]"
check "R03: authorization_endpoint público" "json $TMP/d \"d['authorization_endpoint']\" | grep -q \"^$BASE/\""
check "R09: end_session_endpoint para el cierre de sesión del SPA" "json $TMP/d \"d['end_session_endpoint']\" | grep -q \"^$BASE/connect/endsession\""

section "R04 y R01: autenticación y autorización a través del gateway"
status=$(http -o /dev/null -w '%{http_code}' "$BASE/api/billing/facturas")
check "R04: /api/billing sin token → 401" "[ $status = 401 ]"
status=$(token_request)
check "R03: token por client credentials 200" "[ $status = 200 ]"
TOKEN=$(json "$TMP/t" "d['access_token']")
# La partición de jmeter-load (60/min) puede venir gastada por otra herramienta (verify-observability, JMeter): ante un
# 429 se espera el Retry-After una vez, como con /connect/token. El 429 en sí se verifica en la sección R02.
billing_request() {
    local status
    for _ in 1 2; do
        status=$(http -D "$TMP/bh" -o "$TMP/bb" -w '%{http_code}' -H "Authorization: Bearer $TOKEN" "$@")
        if [ "$status" != 429 ]; then echo "$status"; return; fi
        local wait_for; wait_for=$(header_in "$TMP/bh" Retry-After); wait_for=${wait_for:-60}
        echo "INFO  partición de jmeter-load limitada: espero $((wait_for + 1)) s (Retry-After)" >&2
        sleep $((wait_for + 1))
    done
    echo "$status"
}
status_facturas=$(billing_request "$BASE/api/billing/facturas")
check "R01: Billing a través del gateway con token → 200" "[ $status_facturas = 200 ]"
status_obligados=$(billing_request -X POST -H 'Content-Type: application/json' -d '{}' "$BASE/api/billing/obligados")
check "R04: rol Facturador en endpoint de Admin → 403" "[ $status_obligados = 403 ]"
status_auditoria=$(billing_request "$BASE/api/billing/auditoria")
check "R04: rol Facturador en la auditoría → 403" "[ $status_auditoria = 403 ]"
evidence "curl -s -o /dev/null -w '%{http_code}' .../api/billing/{facturas,obligados,auditoria}   # sin token, Facturador y Facturador" \
    "401 (sin token), $status_facturas (facturas), $status_obligados (obligados), $status_auditoria (auditoria)"

section "R09: CORS para el SPA"
http -D "$TMP/pf" -o /dev/null -X OPTIONS "$BASE/api/billing/facturas" -H "Origin: $SPA" \
    -H "Access-Control-Request-Method: GET" -H "Access-Control-Request-Headers: authorization,x-correlation-id"
evidence "curl -sD - -X OPTIONS $BASE/api/billing/facturas -H 'Origin: $SPA' -H 'Access-Control-Request-Method: GET' -H 'Access-Control-Request-Headers: authorization,x-correlation-id'" "$(headers_of "$TMP/pf")"
check "CORS: preflight del SPA → 204 con Access-Control-Allow-Origin" "grep -q ' 204' $TMP/pf && [ \"\$(header_in $TMP/pf Access-Control-Allow-Origin)\" = \"$SPA\" ]"
http -D "$TMP/pe" -o /dev/null -X OPTIONS "$BASE/api/billing/facturas" -H "Origin: https://atacante.example" -H "Access-Control-Request-Method: GET"
check "CORS: un origen ajeno no recibe Access-Control-Allow-Origin" "[ -z \"\$(header_in $TMP/pe Access-Control-Allow-Origin)\" ]"
http -D "$TMP/p401" -o /dev/null -H "Origin: $SPA" "$BASE/api/billing/facturas"
check "CORS: el 401 lleva Access-Control-Allow-Origin (el SPA puede leer el código)" "[ \"\$(header_in $TMP/p401 Access-Control-Allow-Origin)\" = \"$SPA\" ]"

section "Front-End: nginx sin privilegios con CSP"
http -D "$TMP/fe" -o /dev/null "$SPA/"
evidence "curl -sD - -o /dev/null $SPA/" "$(headers_of "$TMP/fe")"
check "Front-End responde 200" "grep -q ' 200' $TMP/fe"
check "Front-End: CSP con connect-src limitado al gateway" "header_in $TMP/fe Content-Security-Policy | grep -q \"connect-src 'self' $BASE;\""
check "Front-End: CSP sin 'unsafe-inline' ni 'unsafe-eval'" "! header_in $TMP/fe Content-Security-Policy | grep -q unsafe-"
check "Front-End: X-Content-Type-Options nosniff" "[ \"\$(header_in $TMP/fe X-Content-Type-Options)\" = nosniff ]"
check "Front-End: X-Frame-Options DENY" "[ \"\$(header_in $TMP/fe X-Frame-Options)\" = DENY ]"
check "R06 (limitación de nginx OSS): Server sin versión" "[ \"\$(header_in $TMP/fe Server)\" = nginx ]"
http -D "$TMP/fe404" -o /dev/null "$SPA/assets/no-existe.js"
check "Front-End: cabeceras de seguridad también en errores" "grep -q ' 404' $TMP/fe404 && [ -n \"\$(header_in $TMP/fe404 Content-Security-Policy)\" ]"

section "4.2: contenedores"
for service in $(docker compose ps --services 2>/dev/null); do
    id=$(docker compose ps -q "$service")
    [ -z "$id" ] && continue
    line=$(docker inspect "$id" --format '{{.Config.User}}|{{.HostConfig.CapDrop}}|{{.HostConfig.ReadonlyRootfs}}|{{.HostConfig.SecurityOpt}}|{{.HostConfig.NanoCpus}}|{{.HostConfig.Memory}}|{{.HostConfig.PidsLimit}}')
    IFS='|' read -r user capdrop readonly secopt cpus memory pids <<< "$line"
    # docker top necesita la columna pid; la segunda es el uid real de cada proceso del contenedor.
    uids=$(docker top "$id" -o pid,uid 2>/dev/null | tail -n +2 | awk '{print $2}' | sort -u | tr '\n' ' ')
    printf '| %s | %s | %s | %s | %s | %s | %s | %s | %s |\n' "$service" "${user:-root}" "$uids" "$capdrop" "$readonly" "$secopt" \
        "$(python3 -c "print($cpus/1e9)")" "$((memory / 1024 / 1024)) MiB" "$pids" >> "$TMP/containers.md"
    check "$service: usuario sin privilegios ($user)" "[ -n \"$user\" ] && [ \"$user\" != root ] && [ \"$user\" != 0 ] && [ \"\${user%%:*}\" != 0 ]"
    check "$service: ningún proceso con uid 0 (docker top: $uids)" "[ -n \"$uids\" ] && ! echo \" $uids\" | grep -qE ' 0( |$)'"
    check "$service: cap_drop ALL" "[ \"$capdrop\" = '[ALL]' ]"
    check "$service: sistema de archivos de solo lectura" "[ \"$readonly\" = true ]"
    check "$service: no-new-privileges" "echo '$secopt' | grep -q 'no-new-privileges:true'"
    check "$service: límites de CPU, memoria y procesos" "[ \"$cpus\" -gt 0 ] && [ \"$memory\" -gt 0 ] && [ \"$pids\" -gt 0 ]"
done
billing_limits=$(docker inspect "$(docker compose ps -q billing)" --format '{{.HostConfig.NanoCpus}} {{.HostConfig.Memory}}' 2>/dev/null)
check "Billing con límites bajos para la etapa 5.3 (0.5 CPU, 256 MiB)" "[ \"$billing_limits\" = '500000000 268435456' ]"
secrets_in_compose=$(grep -nE '(PASSWORD|SECRET|Password=|password=)' docker-compose.yml docker-compose.observability.yml | grep -vE ':[0-9]+:\s*#|\$\{|\$\$\{' || true)
check "Secretos solo por .env: ningún valor literal en los compose" "[ -z \"$secrets_in_compose\" ]"
evidence "grep -nE '(PASSWORD|SECRET|Password=|password=)' docker-compose.yml docker-compose.observability.yml | grep -v '\${'" "${secrets_in_compose:-(sin coincidencias)}"

section "R12 y TB2: redes y puertos"
# {{.Ports}} muestra "127.0.0.1:8080->8443/tcp" para un puerto publicado y "6379/tcp" para uno solo expuesto.
# (Hasta la etapa 5.2 se usaba {{.Publishers}}, cuyo formato nunca contiene "->": estos dos controles no podían fallar.)
ports=$(docker compose ps --format '{{.Service}} {{.Ports}}' 2>/dev/null)
evidence "docker compose ps --format '{{.Service}} {{.Ports}}'" "$ports"
published=$(echo "$ports" | grep -vE '^(gateway|frontend|grafana) ' | grep -c -- '->' || true)
check "R12: solo el gateway, el Front-End y Grafana publican puertos en el host" "[ \"$published\" = 0 ]"
check "R12: los puertos publicados escuchan solo en 127.0.0.1" "! echo \"$ports\" | grep -- '->' | grep -qvE '^[a-z-]+ (127\\.0\\.0\\.1:[0-9]+->[0-9]+/tcp(, )?)+$'"
internal_networks="backend data"
[ -n "$OBSERVABILITY" ] && internal_networks="backend data observability"
for network in $internal_networks; do
    internal=$(docker network inspect "${PROJECT}_${network}" --format '{{.Internal}}' 2>/dev/null)
    check "R12: red $network interna" "[ \"$internal\" = true ]"
done
# Solo cAdvisor (5.2) necesita el socket de Docker y el espacio de PIDs del host; ambos son riesgos aceptados.
host_access=$(for id in $(docker compose ps -q 2>/dev/null); do
    docker inspect "$id" --format '{{index .Config.Labels "com.docker.compose.service"}} pid={{.HostConfig.PidMode}} socket={{range .Mounts}}{{if eq .Source "/var/run/docker.sock"}}{{.Source}}:rw={{.RW}}{{end}}{{end}}'
done | grep -E 'pid=host|socket=/' || true)
evidence "docker inspect <cada contenedor> --format 'pid={{.HostConfig.PidMode}} socket=<montaje de docker.sock>'" "${host_access:-(ninguno)}"
check "Solo cAdvisor monta el socket de Docker, y en solo lectura" "! echo \"$host_access\" | grep 'socket=/' | grep -qvE '^cadvisor .*socket=/var/run/docker.sock:rw=false'"
check "Solo cAdvisor usa el espacio de PIDs del host" "! echo \"$host_access\" | grep 'pid=host' | grep -qv '^cadvisor '"

if [ -n "$OBSERVABILITY" ]; then
    section "TB3: observabilidad (etapa 5.2)"
    networks_of() { docker inspect "$(docker compose ps -q "$1")" --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}' 2>/dev/null; }
    attachments=$(for service in gateway identity billing otel-collector prometheus tempo loki cadvisor grafana; do
        printf '%s: %s\n' "$service" "$(networks_of "$service")"
    done)
    evidence "docker inspect <servicio> --format '{{range \$name, \$_ := .NetworkSettings.Networks}}{{\$name}} {{end}}'" "$attachments"
    check "TB3: identity y billing no están en la red observability" "! echo \"$attachments\" | grep -E '^(identity|billing):' | grep -q '_observability'"
    check "TB3: el collector es el único en backend y observability a la vez" "[ \"\$(echo \"$attachments\" | grep '_backend' | grep '_observability' | cut -d: -f1)\" = otel-collector ]"
    grafana_anon=$(curl -s -o /dev/null -w '%{http_code}' http://localhost:3001/api/search)
    evidence "curl -s -o /dev/null -w '%{http_code}' http://localhost:3001/api/search   # sin credenciales" "$grafana_anon"
    check "TB3: Grafana exige login (401 sin credenciales)" "[ \"$grafana_anon\" = 401 ]"
    check "TB3: la contraseña de Grafana no es el placeholder de .env.example" "[ -n \"$(env_value GRAFANA_ADMIN_PASSWORD)\" ] && ! env_value GRAFANA_ADMIN_PASSWORD | grep -q '^CAMBIAR'"
    check "TB3: Grafana publica solo 127.0.0.1:3001" "echo \"$ports\" | grep -qE '^grafana 127\\.0\\.0\\.1:3001->3000/tcp$'"
fi

section "2.1: Redis y Mongo"
redis_config=$(docker compose exec -T redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli CONFIG GET maxmemory' 2>&1)
redis_flush=$(docker compose exec -T redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli FLUSHALL' 2>&1)
evidence "redis-cli CONFIG GET maxmemory; redis-cli FLUSHALL   # dentro del contenedor, con la contraseña de .env" "$redis_config
$redis_flush"
check "Redis: CONFIG deshabilitado" "echo '$redis_config' | grep -qi 'unknown command'"
check "Redis: FLUSHALL deshabilitado" "echo '$redis_flush' | grep -qi 'unknown command'"
mongo_delete=$(docker compose exec -T mongo sh -c 'mongosh --quiet "mongodb://billing_audit:$0@localhost:27017/secunitec_audit?authSource=secunitec_audit" --eval "db.eventos.deleteMany({})"' \
    "$(env_value MONGO_BILLING_PASSWORD)" 2>&1 | grep -oE 'not authorized[^"]*|deletedCount[^}]*' | head -1)
evidence "mongosh 'mongodb://billing_audit:***@localhost/secunitec_audit' --eval 'db.eventos.deleteMany({})'" "$mongo_delete"
check "Mongo: billing_audit no puede borrar la auditoría" "echo '$mongo_delete' | grep -q 'not authorized'"
# R18 (etapa 5.3): los pools de Npgsql de Billing e Identity caben en las conexiones que Postgres admite; si no, una
# ráfaga devuelve 500 (53300). Sin "Maximum Pool Size" en la cadena, Npgsql usa 100.
pg_limits=$(docker compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d postgres -Atc "SELECT current_setting('"'"'max_connections'"'"')::int - current_setting('"'"'superuser_reserved_connections'"'"')::int"' 2>/dev/null | tr -d '\r')
# Los tamaños salen de los contenedores en ejecución (la configuración real), no de un valor fijo en el script.
pool_sizes=$(for service in billing identity; do
    cadena=$(docker inspect "$(docker compose ps -q "$service")" --format '{{range .Config.Env}}{{println .}}{{end}}' 2>/dev/null | grep '^ConnectionStrings__')
    echo "$service $(echo "$cadena" | grep -oE 'Maximum Pool Size=[0-9]+' | cut -d= -f2 | grep . || echo 100)"
done)
pool_total=$(echo "$pool_sizes" | awk '{s += $2} END {print s + 0}')
evidence "Maximum Pool Size de Billing e Identity (contenedores) contra max_connections - superuser_reserved_connections (Postgres)" "$pool_sizes
total $pool_total / disponibles ${pg_limits:-?}"
check "R18: los pools de Npgsql caben en max_connections de Postgres" "[ -n \"$pg_limits\" ] && [ \"$pool_total\" -le \"$pg_limits\" ]"

section "R02: rate limiting (va al final: agota el cupo de /connect/token)"
rejected=""
for _ in 1 2 3 4 5 6; do
    status=$(http -D "$TMP/r" -o "$TMP/rb" -w '%{http_code}' -X POST -H "Origin: $SPA" "$BASE/connect/token" -d grant_type=client_credentials)
    if [ "$status" = 429 ]; then rejected=yes; break; fi
done
evidence "for i in 1..6: curl -sD - -X POST -H 'Origin: $SPA' $BASE/connect/token -d grant_type=client_credentials" "$(headers_of "$TMP/r")
$(cat "$TMP/rb")"
check "R02: ráfaga en /connect/token → 429" "[ -n \"$rejected\" ]"
check "R02: 429 con Retry-After" "grep -qi '^Retry-After: [0-9]' $TMP/r"
check "R02: 429 con {\"error\":\"rate_limited\"}" "[ \"\$(json $TMP/rb \"d['error']\")\" = rate_limited ]"
check "R10: el 429 lleva CORS y expone Retry-After al SPA" "[ \"\$(header_in $TMP/r Access-Control-Allow-Origin)\" = \"$SPA\" ] && header_in $TMP/r Access-Control-Expose-Headers | grep -qi retry-after"
rl_keys=$(docker compose exec -T redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli --scan --pattern "*secunitec:rl:*"' 2>/dev/null | head -5)
evidence "redis-cli --scan --pattern '*secunitec:rl:*' | head -5" "$rl_keys"
check "R02: los contadores viven en Redis (no en el respaldo en memoria)" "[ -n \"$rl_keys\" ]"
fallback=$(docker compose logs gateway 2>/dev/null | grep -c 'Redis no disponible para el rate limiting' || true)
check "Gateway sin avisos de respaldo en memoria (Redis alcanzable)" "[ \"$fallback\" = 0 ]"

echo
echo "Resultado: $PASS PASS, $FAIL FAIL"

if [ -n "$REPORT" ]; then
    {
        echo "# 4.2 · Verificación del hardening"
        echo
        echo "Generado por \`scripts/verify-hardening.sh --report\`: no editar a mano. Cada control muestra la salida real del comando contra el compose levantado. Los secretos y tokens nunca se imprimen."
        echo
        echo "| Campo | Valor |"
        echo "|---|---|"
        echo "| Fecha | $(date -u '+%Y-%m-%d %H:%M UTC') |"
        echo "| Commit | \`$(git rev-parse --short HEAD 2>/dev/null)\`$(git diff --quiet 2>/dev/null || echo ' (con cambios sin commitear)') |"
        echo "| Docker | $(docker version --format '{{.Server.Version}}' 2>/dev/null), Compose $(docker compose version --short 2>/dev/null) |"
        echo "| Gateway / SPA | $BASE / $SPA |"
        echo "| Archivos de compose | \`$COMPOSE_FILES\` |"
        echo "| Resultado | **$PASS PASS, $FAIL FAIL** |"
        echo
        echo "## Contenedores"
        echo
        echo "| Servicio | Usuario | uid de los procesos | cap_drop | Solo lectura | security_opt | CPU | Memoria | pids |"
        echo "|---|---|---|---|---|---|---|---|---|"
        cat "$TMP/containers.md"
        echo
        echo "## Checklist"
        echo
        echo "| Sección | Control | Resultado |"
        echo "|---|---|---|"
        cat "$CHECKS"
        echo
        echo "## Evidencia"
        cat "$EVIDENCE"
        echo
        echo "## Limitaciones conocidas"
        echo
        echo "- **Server: nginx.** nginx OSS no puede quitar la cabecera \`Server\`; \`server_tokens off\` oculta la versión. R06 se cumple en el gateway, que es la única entrada a los servicios."
        echo "- **HSTS en localhost.** \`UseHsts\` excluye localhost a propósito (docs/problemas-conocidos.md §4); con otro host el control se verifica."
        echo "- **Preflight de CORS sin rate limiting.** Se responde en el gateway antes del limitador y nunca llega a un servicio interno."
        echo "- **cAdvisor (5.2).** Corre sin root, pero monta el socket de Docker (aunque sea \`:ro\`, da acceso a la API de Docker, que equivale a controlar el host) y usa \`pid: host\` para medir la red por contenedor. Es un riesgo aceptado del entorno de desarrollo: está en una red internal, sin puertos, y queda documentado en docs/06 y en STRIDE."
    } > "$REPORT"
    echo "Reporte: $REPORT"
fi

[ "$FAIL" -eq 0 ]
