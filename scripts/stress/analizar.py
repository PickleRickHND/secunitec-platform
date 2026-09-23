#!/usr/bin/env python3
"""Análisis de una prueba de estrés (etapa 5.3, R17 y R18).

    .venv-stress/bin/python scripts/stress/analizar.py tests/jmeter/results/<fecha>-<plan> [--evidencia docs/evidencia/5.3/<plan>]

Lee resultados.jtl (JMeter), docker-stats.csv (scripts/stress/capture-docker-stats.sh) y rango.env, y consulta
Prometheus a través de Grafana para las métricas de saturación que docker stats no ve: throttling de CPU, cola del
thread pool, pool de Npgsql y eventos de memoria. Escribe en la carpeta de evidencia:

- resumen.json y resumen.md: totales por código, latencias, throughput y la tabla USE por recurso;
- respuestas-por-segundo.{png,svg}: 2xx, 429 y 5xx por segundo, con los hilos activos debajo (la gráfica "429 vs 500");
- latencia.{png,svg}: p50 y p95 por segundo de las respuestas aceptadas y de las bloqueadas;
- cpu.{png,svg} y memoria.{png,svg}: utilización por contenedor contra su límite.

Paleta y marcas: las del skill dataviz (paleta categórica de referencia validada en modo claro, líneas de 2 px, un solo
eje por gráfica, leyenda siempre presente). Requiere matplotlib (scripts/stress/requirements.txt).
"""
from __future__ import annotations

import argparse
import base64
import csv
import json
import math
import os
import re
import statistics
import subprocess
import sys
import urllib.parse
import urllib.request
from collections import Counter, defaultdict
from pathlib import Path

RAIZ = Path(__file__).resolve().parents[2]

# Paleta de referencia (skill dataviz, modo claro). Clases de respuesta con los mismos colores que el SPA y Grafana.
COLOR_CLASE = {"2xx": "#2a78d6", "429": "#eb6834", "401/403": "#1baf7a", "5xx": "#e87ba4", "otros": "#4a3aa7"}
COLOR_SERVICIO = {"gateway": "#2a78d6", "billing": "#eb6834", "identity": "#1baf7a", "redis": "#eda100",
                  "postgres": "#e87ba4", "mongo": "#008300", "frontend": "#4a3aa7", "otel-collector": "#e34948"}
TINTA = "#0b0b0b"
TINTA_SECUNDARIA = "#52514e"
REJILLA = "#e4e3df"
FONDO = "#fcfcfb"
SERVICIOS_USE = ["gateway", "billing", "identity", "redis", "postgres"]


# ------------------------------------------------------------------------------------------------ lectura de datos
def leer_rango(carpeta: Path) -> dict[str, str]:
    return dict(linea.split("=", 1) for linea in (carpeta / "rango.env").read_text().split() if "=" in linea)


def leer_jtl(carpeta: Path) -> list[dict]:
    with open(carpeta / "resultados.jtl", newline="") as f:
        return [r for r in csv.DictReader(f) if not r["label"].startswith("setUp")]


def clase(codigo: str) -> str:
    if codigo.startswith("2"):
        return "2xx"
    if codigo == "429":
        return "429"
    if codigo in ("401", "403"):
        return "401/403"
    if codigo.startswith("5"):
        return "5xx"
    return "otros"  # fallas de conexión o timeouts de JMeter ("Non HTTP response code: ...")


def a_bytes(valor: str) -> float:
    m = re.fullmatch(r"([0-9.]+)\s*([kKMGT]?i?B)", valor.strip())
    if not m:
        return 0.0
    numero, unidad = float(m.group(1)), m.group(2)
    base = 1024 if "i" in unidad else 1000
    exponente = {"B": 0, "kB": 1, "KB": 1, "KiB": 1, "MB": 2, "MiB": 2, "GB": 3, "GiB": 3, "TB": 4, "TiB": 4}[unidad]
    return numero * base ** exponente


def leer_stats(carpeta: Path) -> dict[str, list[dict]]:
    por_contenedor: dict[str, list[dict]] = defaultdict(list)
    with open(carpeta / "docker-stats.csv", newline="") as f:
        for r in csv.DictReader(f):
            servicio = re.sub(r"-\d+$", "", r["contenedor"])
            por_contenedor[servicio].append({
                "t": float(r["timestamp"]),
                "cpu": float(r["cpu_pct"] or 0),
                "mem_pct": float(r["mem_pct"] or 0),
                "mem": a_bytes(r["mem_uso"]),
                "rx": a_bytes(r["red_rx"]),
                "tx": a_bytes(r["red_tx"]),
                "pids": int(r["pids"] or 0),
            })
    return por_contenedor


def limites_cpu() -> dict[str, float]:
    """CPU de deploy.resources.limits por servicio, del compose efectivo."""
    salida = subprocess.run(
        ["docker", "compose", "-f", "docker-compose.yml", "-f", "docker-compose.observability.yml",
         "--profile", "security", "--profile", "observability", "config", "--format", "json"],
        cwd=RAIZ, capture_output=True, text=True, check=True).stdout
    servicios = json.loads(salida)["services"]
    return {nombre: float(s.get("deploy", {}).get("resources", {}).get("limits", {}).get("cpus", 0) or 0)
            for nombre, s in servicios.items()}


# ------------------------------------------------------------------------------------------------ Prometheus
def env_value(clave: str) -> str | None:
    for linea in (RAIZ / os.environ.get("ENV_FILE", ".env")).read_text().splitlines():
        if linea.startswith(clave + "="):
            return linea.split("=", 1)[1]
    return None


def prometheus(query: str, desde: float, hasta: float, paso: int = 5) -> dict[str, list[tuple[float, float]]]:
    """query_range a través del proxy de datasources de Grafana (Prometheus no publica puertos)."""
    grafana = os.environ.get("GRAFANA_URL", "http://localhost:3001")
    parametros = urllib.parse.urlencode({"query": query, "start": desde, "end": hasta, "step": paso})
    peticion = urllib.request.Request(f"{grafana}/api/datasources/proxy/uid/prometheus/api/v1/query_range?{parametros}")
    credencial = base64.b64encode(f"admin:{env_value('GRAFANA_ADMIN_PASSWORD')}".encode()).decode()
    peticion.add_header("Authorization", f"Basic {credencial}")
    try:
        with urllib.request.urlopen(peticion, timeout=30) as respuesta:
            datos = json.load(respuesta)["data"]["result"]
    except Exception as error:  # noqa: BLE001 - la evidencia de Prometheus es complementaria
        print(f"Aviso: Prometheus no respondió ({error}); la tabla USE queda sin esa columna.", file=sys.stderr)
        return {}
    series = {}
    for serie in datos:
        etiqueta = serie["metric"].get("servicio") or serie["metric"].get("service_name") or "total"
        series[etiqueta] = [(float(t), float(v)) for t, v in serie["values"] if v not in ("NaN", "+Inf", "-Inf")]
    return series


def instante(query: str, momento: float) -> dict[str, float]:
    """Consulta instantánea (para totales: increase sobre la ventana completa de la prueba)."""
    grafana = os.environ.get("GRAFANA_URL", "http://localhost:3001")
    parametros = urllib.parse.urlencode({"query": query, "time": momento})
    peticion = urllib.request.Request(f"{grafana}/api/datasources/proxy/uid/prometheus/api/v1/query?{parametros}")
    credencial = base64.b64encode(f"admin:{env_value('GRAFANA_ADMIN_PASSWORD')}".encode()).decode()
    peticion.add_header("Authorization", f"Basic {credencial}")
    try:
        with urllib.request.urlopen(peticion, timeout=30) as respuesta:
            datos = json.load(respuesta)["data"]["result"]
    except Exception as error:  # noqa: BLE001
        print(f"Aviso: Prometheus no respondió ({error}).", file=sys.stderr)
        return {}
    return {(r["metric"].get("servicio") or r["metric"].get("service_name") or "total"): float(r["value"][1])
            for r in datos if r["value"][1] not in ("NaN", "+Inf", "-Inf")}


def maximo(series: dict[str, list[tuple[float, float]]], etiqueta: str) -> float | None:
    valores = [v for _, v in series.get(etiqueta, [])]
    return max(valores) if valores else None


# ------------------------------------------------------------------------------------------------ cálculo
def percentil(valores: list[float], p: float) -> float:
    if not valores:
        return float("nan")
    ordenados = sorted(valores)
    k = (len(ordenados) - 1) * p
    f, c = math.floor(k), math.ceil(k)
    return ordenados[f] if f == c else ordenados[f] + (ordenados[c] - ordenados[f]) * (k - f)


def analizar(carpeta: Path) -> dict:
    rango = leer_rango(carpeta)
    muestras = leer_jtl(carpeta)
    stats = leer_stats(carpeta)
    limites = limites_cpu()
    t0 = min(int(r["timeStamp"]) for r in muestras) / 1000
    t1 = max(int(r["timeStamp"]) + int(r["elapsed"]) for r in muestras) / 1000

    codigos = Counter(r["responseCode"] for r in muestras)
    clases = Counter(clase(r["responseCode"]) for r in muestras)
    por_segundo: dict[int, Counter] = defaultdict(Counter)
    hilos: dict[int, int] = {}
    latencias: dict[str, dict[int, list[float]]] = {"2xx": defaultdict(list), "429": defaultdict(list)}
    for r in muestras:
        segundo = int(int(r["timeStamp"]) / 1000 - t0)
        c = clase(r["responseCode"])
        por_segundo[segundo][c] += 1
        hilos[segundo] = max(hilos.get(segundo, 0), int(r["allThreads"] or 0))
        if c in latencias:
            latencias[c][segundo].append(float(r["elapsed"]))
    elapsed = {c: [float(r["elapsed"]) for r in muestras if clase(r["responseCode"]) == c] for c in ("2xx", "429")}
    duracion = max(t1 - t0, 1)

    # docker stats: CPU relativa al límite (docker la da relativa a 1 CPU) y memoria relativa al límite.
    use_docker = {}
    for servicio in SERVICIOS_USE:
        filas = [s for s in stats.get(servicio, []) if t0 <= s["t"] <= t1]
        if not filas:
            continue
        limite = limites.get(servicio) or 1.0
        cpu = [s["cpu"] / limite for s in filas]
        red = [(b["rx"] + b["tx"] - a["rx"] - a["tx"]) / max(b["t"] - a["t"], 1e-6) for a, b in zip(filas, filas[1:])]
        use_docker[servicio] = {
            "limite_cpu": limite,
            "cpu_media": statistics.fmean(cpu), "cpu_max": max(cpu),
            "mem_media": statistics.fmean(s["mem_pct"] for s in filas), "mem_max": max(s["mem_pct"] for s in filas),
            "red_max_bps": max(red) if red else 0.0,
            "pids_max": max(s["pids"] for s in filas),
        }

    # Prometheus (saturación y errores que docker stats no ve).
    desde, hasta = int(rango["GRAFANA_DESDE"]) / 1000, int(rango["GRAFANA_HASTA"]) / 1000
    svc = "container_label_com_docker_compose_service"
    vivo = 'and on(id) (time() - container_last_seen < 30)'
    def por_servicio(expr: str) -> str:
        return f'label_replace(({expr}) {vivo}, "servicio", "$1", "{svc}", "(.*)")'
    throttling = prometheus(
        f'sum by (servicio) ({por_servicio(f"rate(container_cpu_cfs_throttled_periods_total[30s])")}) / '
        f'sum by (servicio) ({por_servicio(f"rate(container_cpu_cfs_periods_total[30s])")}) * 100', desde, hasta)
    # Totales: increase sobre la ventana completa en una consulta instantánea al final (sumar ventanas solapadas
    # de un query_range contaría cada evento varias veces).
    ventana = f"{max(int(math.ceil(hasta - desde)), 60)}s"
    eventos_memoria = instante(
        f'sum by (servicio) ({por_servicio(f"increase(container_memory_events_max_total[{ventana}]) + increase(container_oom_events_total[{ventana}])")})', hasta)
    oom = instante(f'sum by (servicio) ({por_servicio(f"increase(container_oom_events_total[{ventana}])")})', hasta)
    errores_red = instante(
        f'sum by (servicio) ({por_servicio(f"increase(container_network_receive_errors_total[{ventana}]) + increase(container_network_transmit_errors_total[{ventana}]) + increase(container_network_receive_packets_dropped_total[{ventana}]) + increase(container_network_transmit_packets_dropped_total[{ventana}])")})', hasta)
    cinco_por_servicio = instante(f'sum by (service_name) (increase(http_server_request_duration_seconds_count{{http_response_status_code=~"5..",service_name=~"secunitec-.+"}}[{ventana}]))', hasta)
    rechazos = instante(f'sum(increase(secunitec_gateway_rate_limited_total[{ventana}]))', hasta)
    conexiones_creadas = instante(f'sum by (service_name) (increase(db_client_connection_npgsql_create_time_seconds_count[{ventana}]))', hasta)
    operaciones_pg = instante(f'sum by (service_name) (increase(db_client_operation_duration_seconds_count[{ventana}]))', hasta)
    p95_pg = instante(f'histogram_quantile(0.95, sum by (le, service_name) (increase(db_client_operation_duration_seconds_bucket[{ventana}])))', hasta)
    # Series muestreadas cada 5 s (valor en el instante de exportación): pueden perder ráfagas de 1 a 2 s.
    cola_hilos = prometheus('max by (service_name) (dotnet_thread_pool_queue_length_total{service_name=~"secunitec-.+"})', desde, hasta)
    hilos_pool = prometheus('max by (service_name) (dotnet_thread_pool_thread_count_total{service_name=~"secunitec-.+"})', desde, hasta)
    pool_pg = prometheus('sum by (service_name) (db_client_connection_count{db_client_connection_state="used"}) / '
                         'sum by (service_name) (db_client_connection_max) * 100', desde, hasta)
    pendientes_pg = prometheus('sum by (service_name) (db_client_connection_npgsql_pending_requests)', desde, hasta)

    use_prom = {
        "throttling_max": {s: maximo(throttling, s) for s in SERVICIOS_USE},
        "eventos_memoria": {s: eventos_memoria.get(s, 0.0) for s in SERVICIOS_USE},
        "oom": {s: oom.get(s, 0.0) for s in SERVICIOS_USE},
        "errores_red": {s: errores_red.get(s, 0.0) for s in SERVICIOS_USE},
        "cola_thread_pool_max": {s: maximo(cola_hilos, f"secunitec-{s}") for s in ("gateway", "billing", "identity")},
        "hilos_thread_pool_max": {s: maximo(hilos_pool, f"secunitec-{s}") for s in ("gateway", "billing", "identity")},
        "pool_postgres_max_pct": {s: maximo(pool_pg, f"secunitec-{s}") for s in ("billing", "identity")},
        "pool_postgres_pendientes_max": {s: maximo(pendientes_pg, f"secunitec-{s}") for s in ("billing", "identity")},
        "conexiones_postgres_creadas": {s: conexiones_creadas.get(f"secunitec-{s}", 0.0) for s in ("billing", "identity")},
        "operaciones_postgres": {s: operaciones_pg.get(f"secunitec-{s}", 0.0) for s in ("billing", "identity")},
        "operaciones_postgres_p95_ms": {s: (p95_pg.get(f"secunitec-{s}") or float("nan")) * 1000 for s in ("billing", "identity")},
        "respuestas_5xx": {s: cinco_por_servicio.get(f"secunitec-{s}", 0.0) for s in ("gateway", "billing", "identity")},
        "rechazos_429_segun_metrica_propia": rechazos.get("total", 0.0),
    }

    return {
        "plan": rango.get("PLAN", carpeta.name),
        "carpeta": str(carpeta.relative_to(RAIZ)) if carpeta.is_relative_to(RAIZ) else str(carpeta),
        "duracion_s": round(duracion, 1),
        "hilos_max": max(hilos.values()) if hilos else 0,
        "muestras": len(muestras),
        "throughput_rps": round(len(muestras) / duracion, 1),
        "throughput_aceptado_rps": round(clases["2xx"] / duracion, 2),
        "codigos": dict(sorted(codigos.items())),
        "clases": {c: clases.get(c, 0) for c in COLOR_CLASE},
        "porcentaje_429": round(100 * clases["429"] / max(len(muestras), 1), 1),
        "latencia_ms": {c: {"p50": round(percentil(v, .5), 1), "p95": round(percentil(v, .95), 1),
                            "p99": round(percentil(v, .99), 1), "max": round(max(v), 1) if v else None}
                        for c, v in elapsed.items()},
        "use_docker": use_docker,
        "use_prometheus": use_prom,
        "_series": {"por_segundo": {s: dict(c) for s, c in por_segundo.items()}, "hilos": hilos,
                    "latencias": {c: {s: v for s, v in d.items()} for c, d in latencias.items()}},
        "_stats": {s: [(f["t"] - t0, f["cpu"] / (limites.get(s) or 1.0), f["mem_pct"]) for f in stats.get(s, [])
                       if t0 <= f["t"] <= t1] for s in SERVICIOS_USE},
    }


# ------------------------------------------------------------------------------------------------ gráficas
def estilo(plt) -> None:
    plt.rcParams.update({
        "figure.facecolor": FONDO, "axes.facecolor": FONDO, "savefig.facecolor": FONDO,
        "axes.edgecolor": REJILLA, "axes.labelcolor": TINTA_SECUNDARIA, "axes.titlecolor": TINTA,
        "axes.grid": True, "grid.color": REJILLA, "grid.linewidth": 0.8, "axes.spines.top": False,
        "axes.spines.right": False, "xtick.color": TINTA_SECUNDARIA, "ytick.color": TINTA_SECUNDARIA,
        "font.size": 10, "axes.titlesize": 12, "axes.titleweight": "bold", "legend.frameon": False,
        "lines.linewidth": 2, "lines.solid_capstyle": "round",
    })


def guardar(fig, destino: Path, nombre: str) -> None:
    for extension in ("png", "svg"):
        fig.savefig(destino / f"{nombre}.{extension}", dpi=160, bbox_inches="tight")


def graficas(r: dict, destino: Path) -> None:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    estilo(plt)
    plan = r["plan"]
    segundos = sorted(int(s) for s in r["_series"]["por_segundo"])
    if not segundos:
        return
    eje = list(range(segundos[0], segundos[-1] + 1))

    # 1) 429 contra 5xx por segundo, con los hilos activos en un panel propio (nunca dos escalas en un eje).
    fig, (arriba, abajo) = plt.subplots(2, 1, figsize=(10, 6), sharex=True, gridspec_kw={"height_ratios": [3, 1]})
    for c in ("2xx", "429", "401/403", "5xx", "otros"):
        valores = [r["_series"]["por_segundo"].get(s, {}).get(c, 0) for s in eje]
        if c in ("2xx", "429", "5xx") or any(valores):
            total = r["clases"].get(c, 0)
            arriba.plot(eje, valores, color=COLOR_CLASE[c], label=f"{c}  (total {total:,})".replace(",", " "))
    arriba.set_ylabel("respuestas por segundo")
    arriba.set_title(f"{plan}: respuestas del gateway por segundo (429 controlados contra 5xx)", loc="left")
    arriba.legend(loc="upper left", ncols=3)
    # Aire arriba para la leyenda: nunca tapa las series.
    arriba.set_ylim(0, max(arriba.get_ylim()[1], 1) * 1.25)
    abajo.plot(eje, [r["_series"]["hilos"].get(s, 0) for s in eje], color=TINTA_SECUNDARIA, linewidth=1.5)
    abajo.set_ylabel("hilos activos")
    abajo.set_xlabel("segundos desde el inicio de la carga")
    abajo.set_ylim(bottom=0)
    guardar(fig, destino, "respuestas-por-segundo")
    plt.close(fig)

    # 2) Latencia por segundo: aceptadas contra bloqueadas.
    fig, ax = plt.subplots(figsize=(10, 4))
    for c, color in (("2xx", COLOR_CLASE["2xx"]), ("429", COLOR_CLASE["429"])):
        serie = r["_series"]["latencias"][c]
        xs = [s for s in eje if serie.get(s)]
        if xs:
            ax.plot(xs, [percentil(serie[s], .95) for s in xs], color=color, label=f"{c} p95")
            ax.plot(xs, [percentil(serie[s], .5) for s in xs], color=color, linewidth=1, linestyle=(0, (4, 3)), label=f"{c} p50")
    ax.set_title(f"{plan}: latencia por segundo (ms)", loc="left")
    ax.set_ylabel("milisegundos")
    ax.set_xlabel("segundos desde el inicio de la carga")
    ax.set_ylim(0, max(ax.get_ylim()[1], 1) * 1.25)
    ax.legend(loc="upper left", ncols=4)
    guardar(fig, destino, "latencia")
    plt.close(fig)

    # 3) y 4) Utilización de CPU y memoria contra el límite de cada contenedor.
    for indice, nombre, titulo in ((1, "cpu", "CPU usada (% del límite del contenedor)"),
                                   (2, "memoria", "memoria usada (% del límite del contenedor)")):
        fig, ax = plt.subplots(figsize=(10, 4))
        for servicio in SERVICIOS_USE:
            filas = r["_stats"].get(servicio, [])
            if filas:
                ax.plot([f[0] for f in filas], [f[indice] for f in filas], color=COLOR_SERVICIO[servicio], label=servicio)
        ax.axhline(100, color=TINTA_SECUNDARIA, linewidth=1, linestyle=(0, (2, 2)))
        fin = max((f[0] for fs in r["_stats"].values() for f in fs), default=0)
        ax.text(fin, 101, "límite", color=TINTA_SECUNDARIA, fontsize=9, va="bottom", ha="right")
        ax.set_title(f"{plan}: {titulo}", loc="left")
        ax.set_ylabel("% del límite")
        ax.set_xlabel("segundos desde el inicio de la carga")
        ax.set_ylim(0, max(110, max((f[indice] for fs in r["_stats"].values() for f in fs), default=0) * 1.05))
        ax.legend(loc="upper center", bbox_to_anchor=(0.5, -0.18), ncols=5)
        guardar(fig, destino, nombre)
        plt.close(fig)


# ------------------------------------------------------------------------------------------------ reporte
def fmt(valor, sufijo="", decimales=1) -> str:
    if valor is None or (isinstance(valor, float) and math.isnan(valor)):
        return "s/d"
    return f"{valor:.{decimales}f}{sufijo}"


def markdown(r: dict) -> str:
    d, p = r["use_docker"], r["use_prometheus"]
    lineas = [
        f"# {r['plan']}: resumen",
        "",
        f"Generado por `scripts/stress/analizar.py` a partir de `{r['carpeta']}`.",
        "",
        "| Métrica | Valor |",
        "|---|---|",
        f"| Duración de la carga | {r['duracion_s']} s |",
        f"| Hilos (máximo) | {r['hilos_max']} |",
        f"| Muestras | {r['muestras']:,} |".replace(",", " "),
        f"| Throughput total / aceptado | {r['throughput_rps']} / {r['throughput_aceptado_rps']} peticiones/s |",
        f"| 2xx / 429 / 401-403 / **5xx** / otros | {r['clases']['2xx']} / {r['clases']['429']} / {r['clases']['401/403']} / **{r['clases']['5xx']}** / {r['clases']['otros']} |",
        f"| Porcentaje de 429 | {r['porcentaje_429']} % |",
        f"| Latencia 2xx p50 / p95 / p99 | {fmt(r['latencia_ms']['2xx']['p50'], ' ms')} / {fmt(r['latencia_ms']['2xx']['p95'], ' ms')} / {fmt(r['latencia_ms']['2xx']['p99'], ' ms')} |",
        f"| Latencia 429 p50 / p95 / p99 | {fmt(r['latencia_ms']['429']['p50'], ' ms')} / {fmt(r['latencia_ms']['429']['p95'], ' ms')} / {fmt(r['latencia_ms']['429']['p99'], ' ms')} |",
        f"| 429 según `secunitec_gateway_rate_limited_total` | {fmt(p['rechazos_429_segun_metrica_propia'], '', 0)} |",
        "",
        "Códigos: " + ", ".join(f"`{c}` {n}" for c, n in r["codigos"].items()),
        "",
        "## Método USE",
        "",
        "| Recurso | Contenedor | Utilización (media / máx.) | Saturación | Errores |",
        "|---|---|---|---|---|",
    ]
    for s, u in d.items():
        lineas.append(f"| CPU (límite {u['limite_cpu']:g}) | {s} | {fmt(u['cpu_media'], ' %')} / {fmt(u['cpu_max'], ' %')} | "
                      f"throttling máx. {fmt(p['throttling_max'].get(s), ' %')} de los periodos | — |")
    for s, u in d.items():
        lineas.append(f"| Memoria | {s} | {fmt(u['mem_media'], ' %')} / {fmt(u['mem_max'], ' %')} | "
                      f"{fmt(p['eventos_memoria'].get(s), '', 0)} veces en el límite (memory.events max) | "
                      f"{fmt(p['oom'].get(s), '', 0)} OOM |")
    for s, u in d.items():
        lineas.append(f"| Red | {s} | máx. {fmt(u['red_max_bps'] / 1e6, ' MB/s', 2)} | — | "
                      f"{fmt(p['errores_red'].get(s), '', 0)} paquetes con error o descartados |")
    for s in ("billing", "identity"):
        lineas.append(f"| Conexiones a Postgres (pool de Npgsql) | {s} | {fmt(p['operaciones_postgres'].get(s), '', 0)} consultas, p95 "
                      f"{fmt(p['operaciones_postgres_p95_ms'].get(s), ' ms')}; {fmt(p['conexiones_postgres_creadas'].get(s), '', 0)} conexiones "
                      f"nuevas; máx. {fmt(p['pool_postgres_max_pct'].get(s), ' %')} del pool en uso | "
                      f"máx. {fmt(p['pool_postgres_pendientes_max'].get(s), '', 0)} peticiones esperando conexión | "
                      f"{fmt(p['respuestas_5xx'].get(s), '', 0)} respuestas 5xx del servicio |")
    for s in ("gateway", "billing", "identity"):
        lineas.append(f"| Thread pool de .NET | {s} | máx. {fmt(p['hilos_thread_pool_max'].get(s), ' hilos', 0)} | "
                      f"cola máx. {fmt(p['cola_thread_pool_max'].get(s), '', 0)} | — |")
    lineas += ["",
               "Notas: la utilización de CPU, memoria y red sale de `docker stats` (una muestra por segundo); el throttling, los "
               "eventos de memoria y los totales, de Prometheus sobre la ventana de la prueba. El uso del pool y la cola del thread "
               "pool son gauges que el SDK exporta cada 5 s: pueden no captar picos de 1 a 2 s.",
               "",
               "Gráficas: `respuestas-por-segundo`, `latencia`, `cpu` y `memoria` (.png y .svg) en esta carpeta.", ""]
    return "\n".join(lineas)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("carpeta", type=Path)
    parser.add_argument("--evidencia", type=Path, help="carpeta de destino (por defecto, la misma de los resultados)")
    args = parser.parse_args()
    carpeta = args.carpeta.resolve()
    destino = (args.evidencia or carpeta).resolve()
    destino.mkdir(parents=True, exist_ok=True)

    r = analizar(carpeta)
    graficas(r, destino)
    publico = {k: v for k, v in r.items() if not k.startswith("_")}
    (destino / "resumen.json").write_text(json.dumps(publico, ensure_ascii=False, indent=2) + "\n")
    (destino / "resumen.md").write_text(markdown(r))
    print(markdown(r))


if __name__ == "__main__":
    main()
