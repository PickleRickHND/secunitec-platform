# 5.2 · Verificación de la observabilidad

Generado por `scripts/verify-observability.sh --report`: no editar a mano. El script genera tráfico real a través del gateway y comprueba que llega a Prometheus, Tempo y Loki (criterio de done de 5.2). Los secretos y tokens nunca se imprimen.

| Campo | Valor |
|---|---|
| Fecha | 2026-09-23 15:25 UTC |
| Commit | `efde803` (con cambios sin commitear) |
| Gateway / Grafana | https://localhost:8080 / http://localhost:3001 |
| Resultado | **28 PASS, 0 FAIL** |

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
$ POST /api/billing/facturas (201) y POST .../5d2a4447-1f9d-4413-b7ef-d119c001ce4b/emitir con X-Correlation-Id: verify-otel-1790177125 y traceparent 00-0af7651916cd43dd8448eb211c80319c-...
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
8 -> 9
```

```console
$ PromQL: sum(secunitec_gateway_rate_limited_total{policy="user-by-sub"}) antes y después
152 -> 164
```

```console
$ PromQL: series de red por contenedor (cAdvisor)
container_label_com_docker_compose_service=postgres 10
container_label_com_docker_compose_service=redis 10
container_label_com_docker_compose_service=billing 11
container_label_com_docker_compose_service=gateway 12
```

```console
$ PromQL: db_client_connection_max
service_name=secunitec-billing 100
service_name=secunitec-identity 100
service_name=secunitec-billing 100
service_name=secunitec-identity 100
```

```console
$ PromQL: sum by (client, server) (traces_service_graph_request_total)
client=secunitec-gateway server=secunitec-identity 8
client=secunitec-gateway server=secunitec-billing 60
client=user server=secunitec-gateway 88
client=secunitec-billing server=postgres 16
client=secunitec-billing server=mongo 4
client=secunitec-billing server=secunitec_audit 4
client=secunitec-identity server=postgres 4
client=secunitec-billing server=secunitec-identity 2
client=secunitec-identity server=secunitec_audit 4
client=secunitec-identity server=mongo 4
```

### Trazas (Tempo)

```console
$ Tempo: traza del correlation id verify-otel-1790177125 (91a5108f4b7a23b615108f3c69a47755): servicio | fuente | span | atributos
secunitec-gateway | System.Net.Http | POST | 
secunitec-gateway | Yarp.ReverseProxy | proxy.forwarder | 
secunitec-gateway | Microsoft.AspNetCore | POST /api/billing/{**catch-all} | http.route=/api/billing/{**catch-all}
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | Npgsql | postgresql | db.system.name=postgresql
secunitec-billing | MongoDB.Driver | insert | db.system.name=mongodb
secunitec-billing | MongoDB.Driver | insert secunitec_audit.eventos | db.system.name=mongodb
secunitec-billing | Microsoft.AspNetCore | POST /api/billing/facturas/{id:guid}/emitir | http.route=/api/billing/facturas/{id:guid}/emitir
```

```console
$ Tempo: GET /api/v2/traces/0af7651916cd43dd8448eb211c80319c (el trace id del cliente): lotes de spans
0
```

### Logs (Loki)

```console
$ LogQL: sum by (service_name) (count_over_time({service_name=~"secunitec-.+"}[1h]))
secunitec-billing 17
secunitec-gateway 17
secunitec-identity 11
```
