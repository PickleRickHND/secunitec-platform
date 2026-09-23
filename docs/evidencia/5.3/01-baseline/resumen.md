# 01-baseline: resumen

Generado por `scripts/stress/analizar.py` a partir de `tests/jmeter/results/20260923-102110-01-baseline`.

| Métrica | Valor |
|---|---|
| Duración de la carga | 299.9 s |
| Hilos (máximo) | 50 |
| Muestras | 2 392 |
| Throughput total / aceptado | 8.0 / 7.98 peticiones/s |
| 2xx / 429 / 401-403 / **5xx** / otros | 2392 / 0 / 0 / **0** / 0 |
| Porcentaje de 429 | 0.0 % |
| Latencia 2xx p50 / p95 / p99 | 11.0 ms / 25.0 ms / 69.3 ms |
| Latencia 429 p50 / p95 / p99 | s/d / s/d / s/d |
| 429 según `secunitec_gateway_rate_limited_total` | 1 |

Códigos: `200` 1968, `201` 424

## Método USE

| Recurso | Contenedor | Utilización (media / máx.) | Saturación | Errores |
|---|---|---|---|---|
| CPU (límite 1) | gateway | 3.0 % / 35.3 % | throttling máx. 1.9 % de los periodos | — |
| CPU (límite 0.5) | billing | 14.2 % / 104.6 % | throttling máx. 39.0 % de los periodos | — |
| CPU (límite 1) | identity | 0.9 % / 50.0 % | throttling máx. 5.1 % de los periodos | — |
| CPU (límite 0.25) | redis | 4.7 % / 38.8 % | throttling máx. 5.5 % de los periodos | — |
| CPU (límite 1) | postgres | 1.9 % / 8.1 % | throttling máx. 0.0 % de los periodos | — |
| Memoria | gateway | 86.6 % / 89.2 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | billing | 51.5 % / 55.5 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | identity | 25.9 % / 26.2 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | redis | 6.9 % / 10.1 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | postgres | 9.0 % / 10.5 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Red | gateway | máx. 10.01 MB/s | — | 0 paquetes con error o descartados |
| Red | billing | máx. 1.15 MB/s | — | 0 paquetes con error o descartados |
| Red | identity | máx. 0.02 MB/s | — | 0 paquetes con error o descartados |
| Red | redis | máx. 9.84 MB/s | — | 0 paquetes con error o descartados |
| Red | postgres | máx. 0.20 MB/s | — | 0 paquetes con error o descartados |
| Conexiones a Postgres (pool de Npgsql) | billing | 5804 consultas, p95 4.5 ms; 2 conexiones nuevas; máx. 2.0 % del pool en uso | máx. 0 peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Conexiones a Postgres (pool de Npgsql) | identity | 10 consultas, p95 4.6 ms; 0 conexiones nuevas; máx. 0.0 % del pool en uso | máx. s/d peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Thread pool de .NET | gateway | máx. 7 hilos | cola máx. 0 | — |
| Thread pool de .NET | billing | máx. 8 hilos | cola máx. 0 | — |
| Thread pool de .NET | identity | máx. 5 hilos | cola máx. 1 | — |

Notas: la utilización de CPU, memoria y red sale de `docker stats` (una muestra por segundo); el throttling, los eventos de memoria y los totales, de Prometheus sobre la ventana de la prueba. El uso del pool y la cola del thread pool son gauges que el SDK exporta cada 5 s: pueden no captar picos de 1 a 2 s.

Gráficas: `respuestas-por-segundo`, `latencia`, `cpu` y `memoria` (.png y .svg) en esta carpeta.
