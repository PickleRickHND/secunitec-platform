#!/usr/bin/env bash
# Verificación de la Fase 4 (etapa 5.2, R19) contra el compose levantado con la observabilidad:
#   docker compose -f docker-compose.yml -f docker-compose.observability.yml --profile security --profile observability up -d --build
#   scripts/verify-observability.sh            # imprime PASS/FAIL por control
#   scripts/verify-observability.sh --report   # además escribe docs/evidencia/5.2/verificacion-observabilidad.md
# Genera su propio tráfico (una factura creada y emitida y una ráfaga que termina en 429) y comprueba que llega a
# Prometheus, Tempo y Loki. Prometheus, Tempo y Loki no publican puertos: se consultan a través del proxy de
# datasources de Grafana, con el usuario admin y la contraseña de .env. Nunca imprime secretos ni tokens.
# Consume 1 canje de /connect/token (5/min por IP) y unas 70 peticiones de la partición de jmeter-load.
set -uo pipefail
cd "$(dirname "$0")/.."

REPORT=""
[ "${1:-}" = "--report" ] && REPORT="docs/evidencia/5.2/verificacion-observabilidad.md"

ENV_FILE=${ENV_FILE:-.env}
CA_FILE=${CA_FILE:-infra/certs/gateway.pem}
GRAFANA=${GRAFANA_URL:-http://localhost:3001}
env_value() { grep -E "^$1=" "$ENV_FILE" | head -1 | cut -d= -f2-; }
BASE=$(env_value SECUNITEC_PUBLIC_URL); BASE=${BASE:-https://localhost:8080}
SECRET=$(env_value IDENTITY_JMETER_CLIENT_SECRET)
CLIENTE=$(env_value SEED_CLIENTE_ID); CLIENTE=${CLIENTE:-00000000-0000-0000-0000-0000000000c1}
GRAFANA_AUTH="admin:$(env_value GRAFANA_ADMIN_PASSWORD)"

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
check() { if eval "$2"; then ok "$1"; else ko "$1"; fi; }
evidence() { printf '\n```console\n$ %s\n%s\n```\n' "$1" "$2" >> "$EVIDENCE"; }
http() { curl -s --noproxy '*' --cacert "$CA_FILE" "$@"; }
grafana() { curl -s --noproxy '*' -u "$GRAFANA_AUTH" "$@"; }
# Consulta instantánea a Prometheus; imprime "etiquetas valor" por serie.
promql() {
    grafana --data-urlencode "query=$1" "$GRAFANA/api/datasources/proxy/uid/prometheus/api/v1/query" | python3 -c '
import json, sys
data = json.load(sys.stdin).get("data", {}).get("result", [])
for serie in data:
    labels = {k: v for k, v in serie["metric"].items() if k in sys.argv[1:]}
    print(" ".join(f"{k}={v}" for k, v in sorted(labels.items())), serie["value"][1])
' "${@:2}"
}
# Espera hasta 60 s a que una consulta de Prometheus devuelva un valor mayor que el umbral.
wait_prom() {
    local query=$1 threshold=$2 value
    for _ in $(seq 1 12); do
        value=$(promql "$query" | awk '{print $NF}' | head -1)
        if [ -n "$value" ] && python3 -c "import sys; sys.exit(0 if float(sys.argv[1]) > float(sys.argv[2]) else 1)" "$value" "$threshold"; then
            echo "$value"; return
        fi
        sleep 5
    done
    echo "${value:-0}"
}

echo "Gateway: $BASE   Grafana: $GRAFANA"

section "Stack de observabilidad"
health=$(curl -s --noproxy '*' "$GRAFANA/api/health")
evidence "curl $GRAFANA/api/health" "$health"
check "Grafana responde" "echo '$health' | grep -q '\"database\": *\"ok\"'"
targets=$(promql 'up' job)
evidence "PromQL: up" "$targets"
for job in otel-collector cadvisor prometheus tempo loki; do
    check "Prometheus: target $job arriba" "echo \"$targets\" | grep -qE '^job=$job 1$'"
done
dashboards=$(grafana "$GRAFANA/api/search?tag=secunitec" | python3 -c "import json, sys; print('\n'.join(sorted(d['title'] for d in json.load(sys.stdin))))")
evidence "GET /api/search?tag=secunitec" "$dashboards"
for title in "Resiliencia del gateway" "USE Overview" "Trazas"; do
    check "Dashboard provisionado: $title" "echo \"$dashboards\" | grep -qx '$title'"
done

section "Tráfico real a través del gateway"
emitted_before=$(promql 'sum(secunitec_billing_invoices_emitted_total)' | awk '{print $NF}'); emitted_before=${emitted_before:-0}
limited_before=$(promql 'sum(secunitec_gateway_rate_limited_total{policy="user-by-sub"})' | awk '{print $NF}'); limited_before=${limited_before:-0}
status=""
for _ in 1 2 3; do
    status=$(http -D "$TMP/th" -o "$TMP/t" -w '%{http_code}' -X POST "$BASE/connect/token" -d grant_type=client_credentials \
        -d client_id=jmeter-load --data-urlencode "client_secret=$SECRET" -d scope=secunitec-billing)
    [ "$status" != 429 ] && break
    wait_for=$(grep -i '^Retry-After:' "$TMP/th" | tr -dc '0-9'); echo "INFO  /connect/token limitado: espero $((${wait_for:-60} + 1)) s" >&2
    sleep $((${wait_for:-60} + 1))
done
TOKEN=$(python3 -c "import json, sys; print(json.load(open(sys.argv[1]))['access_token'])" "$TMP/t" 2>/dev/null)
check "Token de jmeter-load (client credentials)" "[ '$status' = 200 ] && [ -n '$TOKEN' ]"
CORRELATION="verify-otel-$(date +%s)"
TRACE_EXTERNO="0af7651916cd43dd8448eb211c80319c"
created=$(http -o "$TMP/f" -w '%{http_code}' -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
    -H "X-Correlation-Id: $CORRELATION-crear" \
    -d "{\"clienteId\":\"$CLIENTE\",\"lineas\":[{\"descripcion\":\"Verificación OTel\",\"cantidad\":1,\"precioUnitario\":100,\"exento\":false}]}" \
    "$BASE/api/billing/facturas")
FACTURA=$(python3 -c "import json, sys; print(json.load(open(sys.argv[1]))['id'])" "$TMP/f" 2>/dev/null)
# TB0: el cliente manda su propio traceparent; el gateway debe ignorarlo y abrir una traza nueva.
emitted=$(http -o /dev/null -w '%{http_code}' -X POST -H "Authorization: Bearer $TOKEN" -H "X-Correlation-Id: $CORRELATION" \
    -H "traceparent: 00-$TRACE_EXTERNO-b7ad6b7169203331-01" "$BASE/api/billing/facturas/$FACTURA/emitir")
evidence "POST /api/billing/facturas (201) y POST .../$FACTURA/emitir con X-Correlation-Id: $CORRELATION y traceparent 00-$TRACE_EXTERNO-..." "crear $created, emitir $emitted"
check "Factura creada (201) y emitida (200)" "[ '$created' = 201 ] && [ '$emitted' = 200 ]"
for _ in $(seq 1 70); do
    http -o /dev/null -w '%{http_code}\n' -H "Authorization: Bearer $TOKEN" "$BASE/api/billing/facturas?pagina=1"
done | sort | uniq -c > "$TMP/burst"
evidence "70 × GET /api/billing/facturas con el mismo token (partición user-by-sub, 60/min)" "$(cat "$TMP/burst")"
check "La ráfaga termina en 429 y sin 5xx" "grep -qE ' 429$' $TMP/burst && ! grep -qE ' 5[0-9]{2}$' $TMP/burst"

section "Métricas (Prometheus)"
emitted_after=$(wait_prom 'sum(secunitec_billing_invoices_emitted_total)' "$emitted_before")
limited_after=$(wait_prom 'sum(secunitec_gateway_rate_limited_total{policy="user-by-sub"})' "$limited_before")
evidence "PromQL: sum(secunitec_billing_invoices_emitted_total) antes y después" "$emitted_before -> $emitted_after"
evidence "PromQL: sum(secunitec_gateway_rate_limited_total{policy=\"user-by-sub\"}) antes y después" "$limited_before -> $limited_after"
check "secunitec_billing_invoices_emitted_total sube con la emisión" "python3 -c 'import sys; sys.exit(0 if float(\"$emitted_after\") > float(\"$emitted_before\") else 1)'"
check "secunitec_gateway_rate_limited_total sube con los 429" "python3 -c 'import sys; sys.exit(0 if float(\"$limited_after\") > float(\"$limited_before\") else 1)'"
retry=$(promql 'sum(secunitec_gateway_retry_after_seconds_count)' | awk '{print $NF}')
check "Histograma secunitec_gateway_retry_after_seconds con muestras" "[ -n '$retry' ] && python3 -c 'import sys; sys.exit(0 if float(\"$retry\") > 0 else 1)'"
use=$(promql 'count by (container_label_com_docker_compose_service) (container_network_receive_bytes_total{container_label_com_docker_compose_service=~"gateway|billing|redis|postgres"})' container_label_com_docker_compose_service)
evidence "PromQL: series de red por contenedor (cAdvisor)" "$use"
check "cAdvisor: red por contenedor con el nombre del servicio" "[ \"\$(echo \"$use\" | wc -l | tr -d ' ')\" = 4 ]"
pool=$(promql 'db_client_connection_max' service_name)
evidence "PromQL: db_client_connection_max" "$pool"
check "Npgsql: métricas del pool de Billing e Identity" "echo \"$pool\" | grep -q secunitec-billing && echo \"$pool\" | grep -q secunitec-identity"
graph=$(promql 'sum by (client, server) (traces_service_graph_request_total)' client server)
evidence "PromQL: sum by (client, server) (traces_service_graph_request_total)" "$graph"
check "Service graph: gateway → billing → postgres" "echo \"$graph\" | grep -q 'client=secunitec-gateway server=secunitec-billing' && echo \"$graph\" | grep -q 'client=secunitec-billing server=postgres'"

section "Trazas (Tempo)"
trace_id=""
for _ in $(seq 1 12); do
    trace_id=$(grafana -G --data-urlencode "q={span.secunitec.correlation_id=\"$CORRELATION\"}" \
        "$GRAFANA/api/datasources/proxy/uid/tempo/api/search" | python3 -c "import json, sys; t = json.load(sys.stdin).get('traces', []); print(t[0]['traceID'] if t else '')" 2>/dev/null)
    [ -n "$trace_id" ] && break
    sleep 5
done
grafana -H 'Accept: application/json' "$GRAFANA/api/datasources/proxy/uid/tempo/api/v2/traces/$trace_id" > "$TMP/trace.json"
spans=$(python3 - "$TMP/trace.json" <<'EOF'
import json, sys
data = json.load(open(sys.argv[1]))
trace = data.get("trace", data)
for batch in trace.get("resourceSpans", []):
    service = next(a["value"]["stringValue"] for a in batch["resource"]["attributes"] if a["key"] == "service.name")
    for scope in batch.get("scopeSpans", []):
        for span in scope["spans"]:
            attrs = {a["key"]: next(iter(a["value"].values())) for a in span.get("attributes", [])}
            extra = " ".join(f"{k}={attrs[k]}" for k in ("db.system.name", "http.route", "url.query") if k in attrs)
            sensitive = [k for k in attrs if k.startswith("http.request.header.") or k in ("url.query", "url.full")]
            print(f"{service} | {scope['scope']['name']} | {span['name']} | {extra}{' | SENSIBLE ' + ','.join(sensitive) if sensitive else ''}")
EOF
)
evidence "Tempo: traza del correlation id $CORRELATION ($trace_id): servicio | fuente | span | atributos" "$spans"
check "Traza encontrada por secunitec.correlation_id" "[ -n '$trace_id' ]"
check "La traza cruza gateway → billing" "echo \"$spans\" | grep -q '^secunitec-gateway | Yarp.ReverseProxy' && echo \"$spans\" | grep -q '^secunitec-billing | Microsoft.AspNetCore'"
check "La traza llega a Postgres (db.system.name=postgresql)" "echo \"$spans\" | grep -q '^secunitec-billing | Npgsql .*db.system.name=postgresql'"
check "La traza incluye la auditoría en Mongo" "echo \"$spans\" | grep -q '^secunitec-billing | MongoDB.Driver'"
check "TB0: la traza no usa el trace id que mandó el cliente" "[ -n '$trace_id' ] && [ '$trace_id' != '$TRACE_EXTERNO' ]"
# Tempo 3 responde 200 con {"trace":{}} cuando el trace id no existe.
externo=$(grafana -H 'Accept: application/json' "$GRAFANA/api/datasources/proxy/uid/tempo/api/v2/traces/$TRACE_EXTERNO" |
    python3 -c "import json, sys; print(len(json.load(sys.stdin).get('trace', {}).get('resourceSpans', [])))")
evidence "Tempo: GET /api/v2/traces/$TRACE_EXTERNO (el trace id del cliente): lotes de spans" "$externo"
check "TB0: Tempo no tiene ninguna traza con el trace id del cliente" "[ '$externo' = 0 ]"
check "TB3: ningún span con query string ni cabeceras HTTP" "! echo \"$spans\" | grep -q SENSIBLE"

section "Logs (Loki)"
logs=$(grafana -G --data-urlencode 'query=sum by (service_name) (count_over_time({service_name=~"secunitec-.+"}[1h]))' \
    "$GRAFANA/api/datasources/proxy/uid/loki/loki/api/v1/query" | python3 -c "
import json, sys
for s in json.load(sys.stdin)['data']['result']:
    print(s['metric']['service_name'], s['value'][1])")
evidence "LogQL: sum by (service_name) (count_over_time({service_name=~\"secunitec-.+\"}[1h]))" "$logs"
for service in gateway identity billing; do
    check "Loki recibe logs de secunitec-$service" "echo \"$logs\" | grep -qE '^secunitec-$service [1-9]'"
done

echo
echo "Resultado: $PASS PASS, $FAIL FAIL"

if [ -n "$REPORT" ]; then
    mkdir -p "$(dirname "$REPORT")"
    {
        echo "# 5.2 · Verificación de la observabilidad"
        echo
        echo "Generado por \`scripts/verify-observability.sh --report\`: no editar a mano. El script genera tráfico real a través del gateway y comprueba que llega a Prometheus, Tempo y Loki (criterio de done de 5.2). Los secretos y tokens nunca se imprimen."
        echo
        echo "| Campo | Valor |"
        echo "|---|---|"
        echo "| Fecha | $(date -u '+%Y-%m-%d %H:%M UTC') |"
        echo "| Commit | \`$(git rev-parse --short HEAD 2>/dev/null)\`$(git diff --quiet 2>/dev/null || echo ' (con cambios sin commitear)') |"
        echo "| Gateway / Grafana | $BASE / $GRAFANA |"
        echo "| Resultado | **$PASS PASS, $FAIL FAIL** |"
        echo
        echo "## Checklist"
        echo
        echo "| Sección | Control | Resultado |"
        echo "|---|---|---|"
        cat "$CHECKS"
        echo
        echo "## Evidencia"
        cat "$EVIDENCE"
    } > "$REPORT"
    echo "Reporte: $REPORT"
fi

[ "$FAIL" -eq 0 ]
