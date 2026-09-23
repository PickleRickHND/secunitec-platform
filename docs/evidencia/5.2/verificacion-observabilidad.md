# 5.2 · Verificación de la observabilidad

Generado por `scripts/verify-observability.sh --report`: no editar a mano. El script genera tráfico real a través del gateway y comprueba que llega a Prometheus, Tempo y Loki (criterio de done de 5.2). Los secretos y tokens nunca se imprimen.

| Campo | Valor |
|---|---|
| Fecha | 2026-09-23 19:46 UTC |
| Commit | `e6fa4a8` |
| Gateway / Grafana | https://localhost:8080 / http://localhost:3001 |
| Resultado | **32 PASS, 0 FAIL** |

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
| Logs (Loki) | Loki: el evento de auditoría de la emisión llega con su correlation id | PASS |
| Logs (Loki) | Loki: el log lleva el trace id de su traza en Tempo | PASS |
| Logs (Loki) | TB3: el log de auditoría no lleva IP, actor ni detalle | PASS |

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
$ POST /api/billing/facturas (201) y POST .../a575fd60-c63a-443b-a2fa-440031b90361/emitir con X-Correlation-Id: verify-otel-1790192763 y traceparent 00-0af7651916cd43dd8448eb211c80319c-...
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
1 -> 2
```

```console
$ PromQL: sum(secunitec_gateway_rate_limited_total{policy="user-by-sub"}) antes y después
13 -> 25
```

```console
$ PromQL: series de red por contenedor (cAdvisor)
container_label_com_docker_compose_service=postgres 10
container_label_com_docker_compose_service=redis 10
container_label_com_docker_compose_service=gateway 12
container_label_com_docker_compose_service=billing 11
```

```console
$ PromQL: db_client_connection_max
service_name=secunitec-identity 20
service_name=secunitec-billing 50
```

```console
$ PromQL: sum by (client, server) (traces_service_graph_request_total)
client=secunitec-gateway server=secunitec-identity 12
client=secunitec-gateway server=secunitec-billing 63
client=user server=secunitec-gateway 92
client=secunitec-billing server=postgres 23
client=secunitec-billing server=mongo 5
client=secunitec-billing server=secunitec_audit 5
client=secunitec-identity server=postgres 7
client=secunitec-billing server=secunitec-identity 2
```

### Trazas (Tempo)

```console
$ Tempo: traza del correlation id verify-otel-1790192763 (5569e242b70c4b1fca69a2f8791e196f): servicio | fuente | span | atributos
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
$ LogQL: sum by (service_name) (count_over_time({service_name=~"secunitec-.+"}[1192s])) (desde el arranque)
secunitec-billing 24
secunitec-gateway 8
secunitec-identity 11
```

```console
$ LogQL: {service_name="secunitec-billing"} | CorrelationId="verify-otel-1790192763" | Accion="factura.emitida"
trace_id=5569e242b70c4b1fca69a2f8791e196f | Auditoría: factura.emitida (exito) sobre a575fd60-c63a-443b-a2fa-440031b90361.
```
