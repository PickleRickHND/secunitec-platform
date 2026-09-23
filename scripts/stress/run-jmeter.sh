#!/usr/bin/env bash
# R16 y R17 (etapa 5.3): corre un plan de tests/jmeter contra el gateway y guarda la evidencia de la prueba:
#   scripts/stress/run-jmeter.sh 01-baseline|02-ramp-saturation|03-spike [-Jpropiedad=valor ...]
# Salida en tests/jmeter/results/<fecha>-<plan>/ (ignorada por git):
#   resultados.jtl   muestras de JMeter (CSV)          html/            reporte HTML de JMeter
#   docker-stats.csv docker stats cada segundo         rango.env        inicio y fin para Grafana (epoch en ms)
#   jmeter.log       log de JMeter
# Después: scripts/stress/analizar.py <carpeta> (gráficas, tabla USE) y, para las capturas de Grafana,
#   (set -a; . <carpeta>/rango.env; cd tests/e2e && GRAFANA_CAPTURAS=<carpeta>/grafana pnpm exec playwright test specs/grafana.spec.ts)
# Requisitos: el compose con la observabilidad arriba, IDENTITY_JMETER_LOAD_CLIENTS > 0 en .env y JMeter 5.6 (Homebrew).
# El secreto de los clientes viaja en la variable de entorno JMETER_CLIENT_SECRET, nunca como -J: JMeter registra las
# propiedades de la línea de comandos en su log.
set -euo pipefail
cd "$(dirname "$0")/../.."

PLAN=${1:-}
[ -n "$PLAN" ] && [ -f "tests/jmeter/$PLAN.jmx" ] || { echo "Uso: $0 01-baseline|02-ramp-saturation|03-spike [-Jprop=valor ...]" >&2; exit 2; }
shift

ENV_FILE=${ENV_FILE:-.env}
env_value() { grep -E "^$1=" "$ENV_FILE" | head -1 | cut -d= -f2-; }
CLIENTES=$(env_value IDENTITY_JMETER_LOAD_CLIENTS)
[ "${CLIENTES:-0}" -gt 0 ] || { echo "Defina IDENTITY_JMETER_LOAD_CLIENTS (> 0) en $ENV_FILE y recree identity y billing." >&2; exit 2; }
CLIENTE_ID=$(env_value SEED_LOAD_CLIENTE_ID); CLIENTE_ID=${CLIENTE_ID:-00000000-0000-0000-0000-0000000000c2}
BASE=$(env_value SECUNITEC_PUBLIC_URL); BASE=${BASE:-https://localhost:8080}
HOST=$(python3 -c "import sys, urllib.parse; print(urllib.parse.urlparse(sys.argv[1]).hostname)" "$BASE")
PORT=$(python3 -c "import sys, urllib.parse; print(urllib.parse.urlparse(sys.argv[1]).port or 443)" "$BASE")
CA_FILE=${CA_FILE:-$PWD/infra/certs/gateway.pem}
command -v jmeter >/dev/null || { echo "Falta JMeter (brew install jmeter)." >&2; exit 2; }
curl -sf --cacert "$CA_FILE" -o /dev/null "$BASE/health" || { echo "El gateway no responde en $BASE." >&2; exit 2; }

OUT="tests/jmeter/results/$(date +%Y%m%d-%H%M%S)-$PLAN"
mkdir -p "$OUT"
JMETER_CLIENT_SECRET=$(env_value IDENTITY_JMETER_CLIENT_SECRET)
export JMETER_CLIENT_SECRET

scripts/stress/capture-docker-stats.sh "$OUT/docker-stats.csv" &
CAPTURA=$!
trap 'kill $CAPTURA 2>/dev/null || true' EXIT

INICIO=$(python3 -c 'import time; print(int(time.time() * 1000))')
echo "Plan $PLAN con $CLIENTES clientes de carga → $OUT"
# 1000 hilos necesitan más heap que el de fábrica (1 GB).
HEAP="-Xms1g -Xmx3g" jmeter -n -t "tests/jmeter/$PLAN.jmx" -q tests/jmeter/secunitec.properties \
    -Jhost="$HOST" -Jport="$PORT" -Jca="$CA_FILE" -Jclientes="$CLIENTES" -Jcliente_id="$CLIENTE_ID" "$@" \
    -l "$OUT/resultados.jtl" -j "$OUT/jmeter.log" -e -o "$OUT/html"
FIN=$(python3 -c 'import time; print(int(time.time() * 1000))')

kill "$CAPTURA" 2>/dev/null || true
wait "$CAPTURA" 2>/dev/null || true
printf 'GRAFANA_DESDE=%s\nGRAFANA_HASTA=%s\nPLAN=%s\n' "$INICIO" "$FIN" "$PLAN" > "$OUT/rango.env"

# Resumen rápido: códigos de respuesta de la carga (sin el setUp de los tokens).
python3 - "$OUT/resultados.jtl" <<'PY'
import collections, csv, sys
codigos = collections.Counter(r["responseCode"] for r in csv.DictReader(open(sys.argv[1])) if not r["label"].startswith("setUp"))
total = sum(codigos.values())
cinco = sum(n for c, n in codigos.items() if c.startswith("5"))
print(f"Muestras: {total}  " + "  ".join(f"{c}: {n}" for c, n in sorted(codigos.items())))
print(f"5xx: {cinco}")
PY
echo "Resultados: $OUT"
