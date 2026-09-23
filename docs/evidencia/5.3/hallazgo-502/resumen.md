# 02-ramp-saturation: resumen

Generado por `scripts/stress/analizar.py` a partir de `tests/jmeter/results/20260923-094745-02-ramp-saturation`.

| Métrica | Valor |
|---|---|
| Duración de la carga | 599.8 s |
| Hilos (máximo) | 500 |
| Muestras | 877 348 |
| Throughput total / aceptado | 1462.7 / 10.0 peticiones/s |
| 2xx / 429 / 401-403 / **5xx** / otros | 5997 / 871348 / 0 / **3** / 0 |
| Porcentaje de 429 | 99.3 % |
| Latencia 2xx p50 / p95 / p99 | 4.0 ms / 60.0 ms / 338.0 ms |
| Latencia 429 p50 / p95 / p99 | 1.0 ms / 6.0 ms / 63.0 ms |
| 429 según `secunitec_gateway_rate_limited_total` | 876908 |

Códigos: `200` 4825, `201` 1172, `429` 871348, `502` 3

## Método USE

| Recurso | Contenedor | Utilización (media / máx.) | Saturación | Errores |
|---|---|---|---|---|
| CPU (límite 1) | gateway | 37.0 % / 99.2 % | throttling máx. 17.1 % de los periodos | — |
| CPU (límite 0.5) | billing | 5.2 % / 63.1 % | throttling máx. 29.9 % de los periodos | — |
| CPU (límite 1) | identity | 0.2 % / 19.2 % | throttling máx. 0.0 % de los periodos | — |
| CPU (límite 0.25) | redis | 24.8 % / 68.3 % | throttling máx. 3.3 % de los periodos | — |
| CPU (límite 1) | postgres | 1.6 % / 30.5 % | throttling máx. 1.3 % de los periodos | — |
| Memoria | gateway | 46.3 % / 55.9 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | billing | 57.9 % / 63.5 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | identity | 30.5 % / 31.0 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | redis | 10.9 % / 14.6 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | postgres | 10.1 % / 16.6 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Red | gateway | máx. 20.00 MB/s | — | 0 paquetes con error o descartados |
| Red | billing | máx. 1.50 MB/s | — | 0 paquetes con error o descartados |
| Red | identity | máx. 0.04 MB/s | — | 0 paquetes con error o descartados |
| Red | redis | máx. 2.40 MB/s | — | 0 paquetes con error o descartados |
| Red | postgres | máx. 0.40 MB/s | — | 0 paquetes con error o descartados |
| Conexiones a Postgres (pool de Npgsql) | billing | 14559 consultas, p95 3.3 ms; 34 conexiones nuevas; máx. 0.0 % del pool en uso | máx. s/d peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Conexiones a Postgres (pool de Npgsql) | identity | 20 consultas, p95 4.2 ms; 0 conexiones nuevas; máx. 0.0 % del pool en uso | máx. s/d peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Thread pool de .NET | gateway | máx. 6 hilos | cola máx. 3 | — |
| Thread pool de .NET | billing | máx. 15 hilos | cola máx. 0 | — |
| Thread pool de .NET | identity | máx. 5 hilos | cola máx. 0 | — |

Notas: la utilización de CPU, memoria y red sale de `docker stats` (una muestra por segundo); el throttling, los eventos de memoria y los totales, de Prometheus sobre la ventana de la prueba. El uso del pool y la cola del thread pool son gauges que el SDK exporta cada 5 s: pueden no captar picos de 1 a 2 s.

Gráficas: `respuestas-por-segundo`, `latencia`, `cpu` y `memoria` (.png y .svg) en esta carpeta.
