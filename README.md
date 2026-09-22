# Secunitec Platform

Plataforma de facturación electrónica y auditoría de seguridad para **Secunitec Corp.**
Proyecto final de Arquitectura de Sistemas Informáticos (UNITEC, Q3-2026).

**Estado:** en planificación. El plan completo, las decisiones de arquitectura y los hitos están en [docs/PLAN.md](docs/PLAN.md).

## Componentes

| Componente | Tecnología | Carpeta |
|---|---|---|
| API Gateway / Ingress Edge | YARP sobre .NET 10, rate limiting L7 con Redis, validación JWT, hardening de cabeceras | `src/Gateway` |
| Identity Provider | .NET 10 + OpenIddict (OAuth 2.0 / OIDC / JWT RS256) + ASP.NET Core Identity | `src/Identity` |
| Core de facturación | .NET 10, Clean Architecture, EF Core + PostgreSQL | `src/Billing` |
| Persistencia multi-modelo | PostgreSQL 17 (transaccional), MongoDB 8 (auditoría), Redis 7 (rate limit y caché) | `infra/` |
| Front-End | React 19 + Vite + TypeScript, SPA servida por nginx | `src/Frontend` |
| Observabilidad (Fase 4) | OpenTelemetry Collector, Prometheus, Tempo, Loki, Grafana, cAdvisor | `infra/`, `docker-compose.observability.yml` |

## Ejecución

> El instructivo completo se publica al cerrar el hito H1. Requisitos previstos: Docker Desktop (Compose v2+).

```bash
cp .env.example .env
docker compose up --build
```

- Front-End: http://localhost:3000
- API Gateway: http://localhost:8080
- Grafana (Fase 4): http://localhost:3001

## Documentación

| Documento | Contenido |
|---|---|
| [docs/PLAN.md](docs/PLAN.md) | Requisitos, decisiones, arquitectura, hitos |
| `docs/01-modelado-amenazas-stride.md` | Fase 1: STRIDE por componente |
| `docs/02-securuml/` | Fase 1: diagramas con RBAC/ABAC y trust boundaries |
| `docs/03-owasp-top10-mapeo.md` | Fase 1: mapeo a OWASP Top 10 |
| `docs/04-hardening-verificacion.md` | Fase 2: evidencia de políticas de seguridad |
| `docs/05-pruebas-estres-use.md` | Fase 3: JMeter y Método USE |
| `docs/06-innovacion-opentelemetry.md` | Fase 4: telemetría centralizada |
| `docs/trazabilidad.md` | Requisito → amenaza → OWASP → código → prueba |
