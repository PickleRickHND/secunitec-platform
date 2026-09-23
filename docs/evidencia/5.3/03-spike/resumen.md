# 03-spike: resumen

Generado por `scripts/stress/analizar.py` a partir de `tests/jmeter/results/20260923-103831-03-spike`.

| Métrica | Valor |
|---|---|
| Duración de la carga | 189.9 s |
| Hilos (máximo) | 1000 |
| Muestras | 854 338 |
| Throughput total / aceptado | 4498.1 / 12.64 peticiones/s |
| 2xx / 429 / 401-403 / **5xx** / otros | 2400 / 851938 / 0 / **0** / 0 |
| Porcentaje de 429 | 99.7 % |
| Latencia 2xx p50 / p95 / p99 | 1289.5 ms / 1898.1 ms / 1948.0 ms |
| Latencia 429 p50 / p95 / p99 | 2.0 ms / 59.0 ms / 141.0 ms |
| 429 según `secunitec_gateway_rate_limited_total` | 882001 |

Códigos: `200` 1947, `201` 453, `429` 851938

## Método USE

| Recurso | Contenedor | Utilización (media / máx.) | Saturación | Errores |
|---|---|---|---|---|
| CPU (límite 1) | gateway | 83.0 % / 104.5 % | throttling máx. 39.4 % de los periodos | — |
| CPU (límite 0.5) | billing | 5.2 % / 103.9 % | throttling máx. 29.1 % de los periodos | — |
| CPU (límite 1) | identity | 0.4 % / 38.1 % | throttling máx. 0.0 % de los periodos | — |
| CPU (límite 0.25) | redis | 63.2 % / 86.5 % | throttling máx. 6.8 % de los periodos | — |
| CPU (límite 1) | postgres | 2.9 % / 67.0 % | throttling máx. 16.7 % de los periodos | — |
| Memoria | gateway | 88.7 % / 94.7 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | billing | 82.5 % / 90.6 % | 162 veces en el límite (memory.events max) | 0 OOM |
| Memoria | identity | 37.3 % / 37.6 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | redis | 5.3 % / 9.3 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | postgres | 21.9 % / 26.6 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Red | gateway | máx. 20.08 MB/s | — | 0 paquetes con error o descartados |
| Red | billing | máx. 9.21 MB/s | — | 0 paquetes con error o descartados |
| Red | identity | máx. 0.02 MB/s | — | 0 paquetes con error o descartados |
| Red | redis | máx. 11.03 MB/s | — | 0 paquetes con error o descartados |
| Red | postgres | máx. 3.01 MB/s | — | 0 paquetes con error o descartados |
| Conexiones a Postgres (pool de Npgsql) | billing | 6373 consultas, p95 93.8 ms; 52 conexiones nuevas; máx. 0.0 % del pool en uso | máx. 99 peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Conexiones a Postgres (pool de Npgsql) | identity | 21 consultas, p95 5.0 ms; 1 conexiones nuevas; máx. 0.0 % del pool en uso | máx. s/d peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Thread pool de .NET | gateway | máx. 5 hilos | cola máx. 335 | — |
| Thread pool de .NET | billing | máx. 10 hilos | cola máx. 496 | — |
| Thread pool de .NET | identity | máx. 5 hilos | cola máx. 0 | — |

Notas: la utilización de CPU, memoria y red sale de `docker stats` (una muestra por segundo); el throttling, los eventos de memoria y los totales, de Prometheus sobre la ventana de la prueba. El uso del pool y la cola del thread pool son gauges que el SDK exporta cada 5 s: pueden no captar picos de 1 a 2 s.

Gráficas: `respuestas-por-segundo`, `latencia`, `cpu` y `memoria` (.png y .svg) en esta carpeta.
