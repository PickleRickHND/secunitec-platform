# Secunitec Platform

Plataforma de facturación electrónica y auditoría de seguridad para **Secunitec Corp.**
Proyecto final de Arquitectura de Sistemas Informáticos (UNITEC, Q3-2026).

**Estado:** Etapa 1 (cimientos, sin Docker) en curso. El plan completo, las decisiones de arquitectura, los hitos y el reparto por etapas y pistas están en [docs/PLAN.md](docs/PLAN.md).

## Componentes

| Componente | Tecnología | Carpeta |
|---|---|---|
| API Gateway / Ingress Edge | YARP sobre .NET 10, rate limiting L7 con Redis, validación JWT, hardening de cabeceras | `src/Gateway` |
| Identity Provider | .NET 10 + OpenIddict (OAuth 2.0 / OIDC / JWT RS256) + ASP.NET Core Identity | `src/Identity` |
| Core de facturación | .NET 10, Clean Architecture, EF Core + PostgreSQL | `src/Billing` |
| Persistencia multi-modelo | PostgreSQL 17 (transaccional), MongoDB 8 (auditoría), Redis 7 (rate limit y caché) | `infra/` |
| Front-End | React 19 + Vite + TypeScript, SPA servida por nginx | `src/Frontend` |
| Observabilidad (Fase 4) | OpenTelemetry Collector, Prometheus, Tempo, Loki, Grafana, cAdvisor | `infra/`, `docker-compose.observability.yml` |
| Bloques comunes | Contrato del token (roles, `tenant_id`, políticas), cabeceras de seguridad, correlation id, ProblemDetails, hardening de Kestrel | `src/BuildingBlocks` |

## Desarrollo local (Etapa 1, sin Docker)

Requisito: [.NET SDK 10.0.400](https://dotnet.microsoft.com/download/dotnet/10.0) o superior dentro de 10.0.x (`global.json`).

```bash
dotnet build Secunitec.slnx -c Release
dotnet test Secunitec.slnx -c Release
dotnet format Secunitec.slnx --verify-no-changes
```

`dotnet test` corre en modo Microsoft.Testing.Platform (configurado en `global.json`). Para un reporte TRX:
`dotnet test Secunitec.slnx --results-directory TestResults -- --report-xunit-trx`.

Las advertencias del compilador y de los analizadores se tratan como errores; el CI (`.github/workflows/ci.yml`) ejecuta los mismos tres comandos más un escaneo de secretos con gitleaks.

## Ejecución con Docker

> El instructivo completo se publica al cerrar la etapa 2.1. Requisitos previstos: Docker Desktop (Compose v2+).

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
