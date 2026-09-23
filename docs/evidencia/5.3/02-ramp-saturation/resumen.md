# 02-ramp-saturation: resumen

Generado por `scripts/stress/analizar.py` a partir de `tests/jmeter/results/20260923-102717-02-ramp-saturation`.

| Métrica | Valor |
|---|---|
| Duración de la carga | 599.8 s |
| Hilos (máximo) | 500 |
| Muestras | 875 903 |
| Throughput total / aceptado | 1460.3 / 10.0 peticiones/s |
| 2xx / 429 / 401-403 / **5xx** / otros | 6000 / 869903 / 0 / **0** / 0 |
| Porcentaje de 429 | 99.3 % |
| Latencia 2xx p50 / p95 / p99 | 4.0 ms / 56.1 ms / 148.0 ms |
| Latencia 429 p50 / p95 / p99 | 1.0 ms / 6.0 ms / 25.0 ms |
| 429 según `secunitec_gateway_rate_limited_total` | 873795 |

Códigos: `200` 4859, `201` 1141, `429` 869903

## Método USE

| Recurso | Contenedor | Utilización (media / máx.) | Saturación | Errores |
|---|---|---|---|---|
| CPU (límite 1) | gateway | 31.0 % / 80.1 % | throttling máx. 4.3 % de los periodos | — |
| CPU (límite 0.5) | billing | 4.7 % / 50.6 % | throttling máx. 24.4 % de los periodos | — |
| CPU (límite 1) | identity | 0.2 % / 26.0 % | throttling máx. 0.0 % de los periodos | — |
| CPU (límite 0.25) | redis | 23.0 % / 59.4 % | throttling máx. 5.7 % de los periodos | — |
| CPU (límite 1) | postgres | 1.6 % / 19.7 % | throttling máx. 0.0 % de los periodos | — |
| Memoria | gateway | 76.3 % / 87.5 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | billing | 58.5 % / 69.4 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | identity | 29.2 % / 31.7 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | redis | 6.2 % / 9.1 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Memoria | postgres | 12.0 % / 16.6 % | 0 veces en el límite (memory.events max) | 0 OOM |
| Red | gateway | máx. 20.04 MB/s | — | 0 paquetes con error o descartados |
| Red | billing | máx. 2.19 MB/s | — | 0 paquetes con error o descartados |
| Red | identity | máx. 0.02 MB/s | — | 0 paquetes con error o descartados |
| Red | redis | máx. 11.02 MB/s | — | 0 paquetes con error o descartados |
| Red | postgres | máx. 0.70 MB/s | — | 0 paquetes con error o descartados |
| Conexiones a Postgres (pool de Npgsql) | billing | 14322 consultas, p95 3.5 ms; 32 conexiones nuevas; máx. 2.0 % del pool en uso | máx. s/d peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Conexiones a Postgres (pool de Npgsql) | identity | 10 consultas, p95 4.0 ms; 0 conexiones nuevas; máx. 0.0 % del pool en uso | máx. s/d peticiones esperando conexión | 0 respuestas 5xx del servicio |
| Thread pool de .NET | gateway | máx. 5 hilos | cola máx. 4 | — |
| Thread pool de .NET | billing | máx. 15 hilos | cola máx. 0 | — |
| Thread pool de .NET | identity | máx. 6 hilos | cola máx. 0 | — |

Notas: la utilización de CPU, memoria y red sale de `docker stats` (una muestra por segundo); el throttling, los eventos de memoria y los totales, de Prometheus sobre la ventana de la prueba. El uso del pool y la cola del thread pool son gauges que el SDK exporta cada 5 s: pueden no captar picos de 1 a 2 s.

Gráficas: `respuestas-por-segundo`, `latencia`, `cpu` y `memoria` (.png y .svg) en esta carpeta.
