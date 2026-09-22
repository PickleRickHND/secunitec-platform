# Secunitec Platform

Plataforma de facturación electrónica y auditoría de seguridad para **Secunitec Corp.**
Proyecto final de Arquitectura de Sistemas Informáticos (UNITEC, Q3-2026).

Este README es el tablero del proyecto: el plan por etapas, qué está hecho y qué falta. El detalle de arquitectura, requisitos (`R01`-`R22`), decisiones y controles de seguridad vive en [docs/PLAN.md](docs/PLAN.md).

## Estado general

| Etapa | Punto | Pista | Estado |
|---|---|---|---|
| 1 · Cimientos (sin Docker) | 1.1 Proyecto base, reglas comunes y contrato del token | A | **Completado** ([PR #1](https://github.com/PickleRickHND/secunitec-platform/pull/1)) |
| | 1.2 Reglas de facturación (`Billing.Domain`) | B | Pendiente |
| | 1.3 Acciones de facturación (`Billing.Application`) | B | Pendiente |
| 2 · Bases de datos (se enciende Docker) | 2.1 Postgres, Mongo y Redis en contenedores | B | Pendiente |
| | 2.2 Facturación conectada y expuesta (`Infrastructure` + `Api`) | B | Pendiente |
| 3 · Seguridad | 3.1 Identity (OAuth 2.0 / OIDC / JWT) | A | Pendiente |
| | 3.2 Gateway (única entrada, 429, cabeceras) | A | Pendiente |
| 4 · Fachada | 4.1 Frontend (login, facturas, panel 429) | C | Pendiente |
| | 4.2 Endurecer y verificar | A | Pendiente |
| 5 · Evidencia e informe | 5.1 Amenazas (STRIDE, SecurUML, OWASP) | Todos | Pendiente |
| | 5.2 Telemetría (OpenTelemetry + Grafana) | C | Pendiente |
| | 5.3 Pruebas de estrés (JMeter, USE) | C | Pendiente |
| | 5.4 Informe técnico y presentación | Todos | Pendiente |

Pistas: **A** = Identity, Gateway, hardening · **B** = Billing y bases de datos · **C** = Frontend, telemetría, estrés. Las tres pistas avanzan en paralelo.

## Componentes

| Componente | Tecnología | Carpeta | Estado |
|---|---|---|---|
| Bloques comunes | Contrato del token (roles, `tenant_id`, políticas), cabeceras de seguridad, correlation id, ProblemDetails, hardening de Kestrel | `src/BuildingBlocks` | Completado |
| Core de facturación | .NET 10, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core + PostgreSQL | `src/Billing` | Pendiente (1.2, 1.3, 2.2) |
| Persistencia multi-modelo | PostgreSQL 17 (transaccional), MongoDB 8 (auditoría), Redis 7 (rate limit y caché) | `infra/`, `docker-compose.yml` | Pendiente (2.1) |
| Identity Provider | .NET 10 + OpenIddict (OAuth 2.0 / OIDC / JWT RS256) + ASP.NET Core Identity | `src/Identity` | Pendiente (3.1) |
| API Gateway / Ingress Edge | YARP sobre .NET 10, rate limiting L7 con Redis, validación JWT, hardening de cabeceras | `src/Gateway` | Pendiente (3.2) |
| Front-End | React 19 + Vite + TypeScript, SPA servida por nginx | `src/Frontend` | Pendiente (4.1) |
| Observabilidad (Fase 4) | OpenTelemetry Collector, Prometheus, Tempo, Loki, Grafana, cAdvisor | `infra/`, `docker-compose.observability.yml` | Pendiente (5.2) |
