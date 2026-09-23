# 5.2 · Verificación de la observabilidad

Generado por `scripts/verify-observability.sh --report`: no editar a mano. El script genera tráfico real a través del gateway y comprueba que llega a Prometheus, Tempo y Loki (criterio de done de 5.2). Los secretos y tokens nunca se imprimen.

| Campo | Valor |
|---|---|
| Fecha | 2026-09-23 16:55 UTC |
| Commit | `dd38abf` (con cambios sin commitear) |
| Gateway / Grafana | https://localhost:8080 / http://localhost:3001 |
| Resultado | **29 PASS, 0 FAIL** |

## Checklist

| Sección | Control | Resultado |
|---|---|---|
| Stack de observabilidad | Grafana responde | PASS |
| Stack de observabilidad | Prometheus: target otel-collector arriba | PASS |
| Stack de observabilidad | Prometheus: target cadvisor arriba | PASS |
| Stack de observabilidad | Prometheus: target prometheus arriba | PASS |
| Stack de observabilidad | Prometheus: target tempo arriba | PASS |
| Stack de observabilidad | Prometheus: target loki arriba | PASS |
| Stack de observabilidad | Dashboard provisionado: Resiliencia del gateway | PASS |
| Stack de observabilidad | Dashboard provisionado: USE Overview | PASS |
| Stack de observabilidad | Dashboard provisionado: Trazas | PASS |
| Tráfico real a través del gateway | Token de jmeter-load (client credentials) | PASS |
| Tráfico real a través del gateway | Factura creada (201) y emitida (200) | PASS |
| Tráfico real a través del gateway | La ráfaga termina en 429 y sin 5xx | PASS |
| Métricas (Prometheus) | secunitec_billing_invoices_emitted_total sube con la emisión | PASS |
| Métricas (Prometheus) | secunitec_gateway_rate_limited_total sube con los 429 | PASS |
| Métricas (Prometheus) | Histograma secunitec_gateway_retry_after_seconds con muestras | PASS |
| Métricas (Prometheus) | cAdvisor: red por contenedor con el nombre del servicio | PASS |
| Métricas (Prometheus) | Npgsql: métricas del pool de Billing e Identity | PASS |
| Métricas (Prometheus) | Service graph: gateway → billing → postgres | PASS |
| Trazas (Tempo) | Muestreo de trazas al 100 % (si no: recrear gateway, identity y billing sin OTEL_TRACE_SAMPLE_RATIO) | PASS |
| Trazas (Tempo) | Traza encontrada por secunitec.correlation_id | PASS |
| Trazas (Tempo) | La traza cruza gateway → billing | PASS |
| Trazas (Tempo) | La traza llega a Postgres (db.system.name=postgresql) | PASS |
| Trazas (Tempo) | La traza incluye la auditoría en Mongo | PASS |
| Trazas (Tempo) | TB0: la traza no usa el trace id que mandó el cliente | PASS |
| Trazas (Tempo) | TB0: Tempo no tiene ninguna traza con el trace id del cliente | PASS |
| Trazas (Tempo) | TB3: ningún span con query string ni cabeceras HTTP | PASS |
| Logs (Loki) | Loki recibe logs de secunitec-gateway | PASS |
| Logs (Loki) | Loki recibe logs de secunitec-identity | PASS |
| Logs (Loki) | Loki recibe logs de secunitec-billing | PASS |

## Evidencia

### Stack de observabilidad

```console
$ curl http://localhost:3001/api/health
{
  "database": "ok",
  "version": "13.2.2",
  "commit": "1bea008f7e4e858b6824c9e364d608bd4d10b13a"
}
```

```console
$ PromQL: up
job=prometheus 1
job=cadvisor 1
job=loki 1
job=otel-collector 1
job=tempo 1
```

```console
$ GET /api/search?tag=secunitec
Resiliencia del gateway
Trazas
USE Overview
```

### Tráfico real a través del gateway

```console
$ POST /api/billing/facturas (201) y POST .../766369b0-77c0-480d-9e95-9bfa9bf6fb0f/emitir con X-Correlation-Id: verify-otel-1790182475 y traceparent 00-0af7651916cd43dd8448eb211c80319c-...
crear 201, emitir 200
```

```console
$ 70 × GET /api/billing/facturas con el mismo token (partición user-by-sub, 60/min)
  58 200
  12 429
```

### Métricas (Prometheus)

```console
$ PromQL: sum(secunitec_billing_invoices_emitted_total) antes y después
78 -> 79
```

```console
$ PromQL: sum(secunitec_gateway_rate_limited_total{policy="user-by-sub"}) antes y después
3414670 -> 3414682
```

```console
$ PromQL: series de red por contenedor (cAdvisor)
container_label_com_docker_compose_service=postgres 10
container_label_com_docker_compose_service=redis 10
container_label_com_docker_compose_service=gateway 24
container_label_com_docker_compose_service=billing 22
```

```console
$ PromQL: db_client_connection_max
service_name=secunitec-billing 50
service_name=secunitec-identity 20
service_name=secunitec-billing 50
service_name=secunitec-identity 20
```

```console
$ PromQL: sum by (client, server) (traces_service_graph_request_total)
client=secunitec-gateway server=secunitec-identity 3
client=secunitec-gateway server=secunitec-billing 2882
client=user server=secunitec-gateway 432053
client=secunitec-billing server=postgres 6814
client=secunitec-billing server=mongo 557
client=secunitec-billing server=secunitec_audit 557
client=secunitec-identity server=postgres 7
client=secunitec-identity server=secunitec_audit 1
client=secunitec-identity server=mongo 1
```

### Trazas (Tempo)

```console
$ Tempo: traza del correlation id verify-otel-1790182475 (bee9a9211a74366b7a50bb9b0adc76a7): servicio | fuente | span | atributos
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | MongoDB.Driver | insert | db.system.name=mongodb
secunitec-billing | MongoDB.Driver | insert secunitec_audit.eventos | db.system.name=mongodb
secunitec-billing | Microsoft.AspNetCore | POST /api/billing/facturas/{id:guid}/emitir | http.route=/api/billing/facturas/{id:guid}/emitir
secunitec-gateway | System.Net.Http | POST | 
secunitec-gateway | Yarp.ReverseProxy | proxy.forwarder | 
secunitec-gateway | Microsoft.AspNetCore | POST /api/billing/{**catch-all} | http.route=/api/billing/{**catch-all}
```

```console
$ Tempo: GET /api/v2/traces/0af7651916cd43dd8448eb211c80319c (el trace id del cliente): lotes de spans
0
```

### Logs (Loki)

```console
$ LogQL: sum by (service_name) (count_over_time({service_name=~"secunitec-.+"}[1h]))
secunitec-billing 121
secunitec-gateway 21
secunitec-identity 18
```
