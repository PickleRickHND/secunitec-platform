# 03-spike: resumen

Generado por `scripts/stress/analizar.py` a partir de `tests/jmeter/results/20260923-101404-03-spike`.

| Métrica | Valor |
|---|---|
| Duración de la carga | 190.5 s |
| Hilos (máximo) | 1000 |
| Muestras | 827 417 |
| Throughput total / aceptado | 4343.8 / 12.43 peticiones/s |
| 2xx / 429 / 401-403 / **5xx** / otros | 2367 / 825017 / 0 / **33** / 0 |
| Porcentaje de 429 | 99.7 % |
| Latencia 2xx p50 / p95 / p99 | 1716.0 ms / 7111.9 ms / 7819.7 ms |
| Latencia 429 p50 / p95 / p99 | 1.0 ms / 73.0 ms / 268.0 ms |
| 429 según `secunitec_gateway_rate_limited_total` | 840871 |

Códigos: `200` 1888, `201` 479, `429` 825017, `500` 33

## Método USE

| Recurso | Contenedor | Utilización (media / máx.) | Saturación | Errores |
|---|---|---|---|---|
| CPU (límite 1) | gateway | 83.2 % / 113.7 % | throttling máx. 57.2 % de los periodos | — |
| CPU (límite 0.5) | billing | 9.0 % / 100.5 % | throttling máx. 36.7 % de los periodos | — |
| CPU (límite 1) | identity | 0.4 % / 22.2 % | throttling máx. 0.0 % de los periodos | — |
| CPU (límite 0.25) | redis | 62.9 % / 83.6 % | throttling máx. 7.6 % de los periodos | — |
| CPU (límite 1) | postgres | 9.2 % / 111.8 % | throttling máx. 68.4 % de los periodos | — |
| Memoria | gateway | 72.2 % / 85.0 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | billing | 78.5 % / 89.4 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | identity | 28.5 % / 31.0 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | redis | 10.7 % / 11.4 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | postgres | 21.3 % / 41.0 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Red | gateway | máx. 20.02 MB/s | — | 0 paquetes con error o descartados |
| Red | billing | máx. 5.70 MB/s | — | 0 paquetes con error o descartados |
| Red | identity | máx. 0.02 MB/s | — | 0 paquetes con error o descartados |
| Red | redis | máx. 11.01 MB/s | — | 0 paquetes con error o descartados |
| Red | postgres | máx. 1.60 MB/s | — | 0 paquetes con error o descartados |
| Conexiones a Postgres (pool de Npgsql) | billing | 5761 consultas, p95 474.9 ms; 1286 conexiones nuevas; máx. 100.0 % del pool en uso | máx. 461 peticiones esperando conexión | 25 respuestas 5xx del servicio |
| Conexiones a Postgres (pool de Npgsql) | identity | 20 consultas, p95 4.0 ms; 0 conexiones nuevas; máx. 0.0 % del pool en uso | máx. s/d peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Thread pool de .NET | gateway | máx. 3 hilos | cola máx. 448 | — |
| Thread pool de .NET | billing | máx. 10 hilos | cola máx. 0 | — |
| Thread pool de .NET | identity | máx. 5 hilos | cola máx. 0 | — |

Notas: la utilización de CPU, memoria y red sale de `docker stats` (una muestra por segundo); el throttling, los eventos de memoria y los totales, de Prometheus sobre la ventana de la prueba. El uso del pool y la cola del thread pool son gauges que el SDK exporta cada 5 s: pueden no captar picos de 1 a 2 s.

Gráficas: `respuestas-por-segundo`, `latencia`, `cpu` y `memoria` (.png y .svg) en esta carpeta.
