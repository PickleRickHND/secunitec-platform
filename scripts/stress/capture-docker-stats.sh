#!/usr/bin/env bash
# R17 (etapa 5.3): docker stats de los contenedores del compose a CSV, una muestra por contenedor cada segundo.
#   scripts/stress/capture-docker-stats.sh <salida.csv>      # corre hasta recibir SIGTERM o Ctrl+C
# Usa el modo streaming de docker stats: con --no-stream cada lectura tarda unos 2 s y no llega a una por segundo.
# Los valores quedan como los imprime docker (p. ej. "101.6MiB"); scripts/stress/analizar.py los convierte.
set -uo pipefail
cd "$(dirname "$0")/../.."

OUT=${1:?Uso: capture-docker-stats.sh <salida.csv>}
PROJECT=$(docker compose config --format json 2>/dev/null | python3 -c "import json, sys; print(json.load(sys.stdin)['name'])")
ids=$(docker ps -q --filter "label=com.docker.compose.project=$PROJECT")
[ -n "$ids" ] || { echo "No hay contenedores del proyecto $PROJECT corriendo." >&2; exit 1; }

# Un solo proceso convierte el streaming: quita las secuencias ANSI con que docker redibuja la pantalla, agrega la
# hora de lectura y separa "uso / límite" en dos columnas. Lanzar un proceso por línea cargaría al host durante la prueba.
# shellcheck disable=SC2016
CONVERTIR='
import re, sys, time
ansi = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")
proyecto = sys.argv[1] + "-"
salida = open(sys.argv[2], "w", buffering=1)
salida.write("timestamp,contenedor,cpu_pct,mem_uso,mem_limite,mem_pct,red_rx,red_tx,disco_lectura,disco_escritura,pids\n")
ultimo = {}
for linea in sys.stdin:
    campos = ansi.sub("", linea).strip().split("|")
    if len(campos) != 7 or campos[0] in ("", "NAME"):
        continue
    nombre, cpu, mem, mem_pct, red, disco, pids = campos
    # docker redibuja cada 0,5 s pero el daemon mide cada 1 s: se descartan las repeticiones exactas.
    if ultimo.get(nombre) == campos:
        continue
    ultimo[nombre] = campos
    partes = [f"{time.time():.3f}", nombre.removeprefix(proyecto), cpu.rstrip("%"), *mem.split(" / "), mem_pct.rstrip("%"),
              *red.split(" / "), *disco.split(" / "), pids]
    salida.write(",".join(partes) + "\n")
'
# Al terminar (SIGTERM de run-jmeter.sh o Ctrl+C) se detienen también docker stats y el conversor.
trap 'pkill -P $$ 2>/dev/null; exit 0' TERM INT
# shellcheck disable=SC2086
docker stats --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}|{{.NetIO}}|{{.BlockIO}}|{{.PIDs}}' $ids |
    python3 -u -c "$CONVERTIR" "$PROJECT" "$OUT" &
wait
