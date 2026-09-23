# Secunitec Platform

Plataforma de facturación electrónica y auditoría de seguridad para **Secunitec Corp.**
Proyecto final de Arquitectura de Sistemas Informáticos (UNITEC, Q3-2026).

Este README es el tablero del proyecto: el plan por etapas, qué está hecho y qué falta. El detalle de arquitectura, requisitos (`R01`-`R22`), decisiones y controles de seguridad vive en [docs/PLAN.md](docs/PLAN.md).

## Estado general

| Etapa | Punto | Pista | Estado |
|---|---|---|---|
| 1 · Cimientos (sin Docker) | 1.1 Proyecto base, reglas comunes y contrato del token | A | **Completado** ([PR #1](https://github.com/PickleRickHND/secunitec-platform/pull/1)) |
| | 1.2 Reglas de facturación (`Billing.Domain`) | B | **Completado** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2) y [PR #4](https://github.com/PickleRickHND/secunitec-platform/pull/4)) |
| | 1.3 Acciones de facturación (`Billing.Application`) | B | **Completado** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2) y [PR #4](https://github.com/PickleRickHND/secunitec-platform/pull/4)) |
| 2 · Bases de datos (se enciende Docker) | 2.1 Postgres, Mongo y Redis en contenedores | B | **Completado** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2) y [PR #4](https://github.com/PickleRickHND/secunitec-platform/pull/4)) |
| | 2.2 Facturación conectada y expuesta (`Infrastructure` + `Api`) | B | **Completado** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2) y [PR #4](https://github.com/PickleRickHND/secunitec-platform/pull/4)) |
| 3 · Seguridad | 3.1 Identity (OAuth 2.0 / OIDC / JWT) | A | **Completado** ([PR #3](https://github.com/PickleRickHND/secunitec-platform/pull/3)); ver [pendientes](#pendientes) |
| | 3.2 Gateway (única entrada, 429, cabeceras) | A | **Completado** ([PR #3](https://github.com/PickleRickHND/secunitec-platform/pull/3)); ver [pendientes](#pendientes) |
| 4 · Fachada | 4.1 Frontend (login, facturas, panel 429) | C | **Completado** ([PR #4](https://github.com/PickleRickHND/secunitec-platform/pull/4)) |
| | 4.2 Endurecer y verificar | A | **Completado** ([PR #4](https://github.com/PickleRickHND/secunitec-platform/pull/4)) |
| 5 · Evidencia e informe | 5.1 Amenazas (STRIDE, SecurUML, OWASP) | Todos | Pendiente |
| | 5.2 Telemetría (OpenTelemetry + Grafana) | C | **Completado** (rama `feature/5-evidencia`); ver [docs/06](docs/06-innovacion-opentelemetry.md) |
| | 5.3 Pruebas de estrés (JMeter, USE) | C | Pendiente |
| | 5.4 Informe técnico y presentación | Todos | Pendiente |

Pistas: **A** = Identity, Gateway, hardening · **B** = Billing y bases de datos · **C** = Frontend, telemetría, estrés. Las tres pistas avanzan en paralelo.

La etapa 4 cerró también los pendientes de 1.2 a 2.2: el SPA los necesitaba. El detalle está en [`docs/ETAPA_4_1_FRONTEND.md`](docs/ETAPA_4_1_FRONTEND.md) y [`docs/ETAPA_4_2_HARDENING.md`](docs/ETAPA_4_2_HARDENING.md), y la evidencia del hardening en [`docs/04-hardening-verificacion.md`](docs/04-hardening-verificacion.md).

## Componentes

| Componente | Tecnología | Carpeta | Estado |
|---|---|---|---|
| Bloques comunes | Contrato del token (roles, `tenant_id`, políticas), cabeceras de seguridad, correlation id, ProblemDetails, hardening de Kestrel | `src/BuildingBlocks` | Completado |
| Core de facturación | .NET 10, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core + PostgreSQL, FluentValidation | `src/Billing` | Completado (1.2 a 2.2) |
| Persistencia multi-modelo | PostgreSQL 17 (transaccional), MongoDB 8 (auditoría solo de inserción), Redis 7 (contadores del rate limiting y caché) | `infra/`, `docker-compose.yml` | Completado (2.1) |
| Identity Provider | .NET 10 + OpenIddict (OAuth 2.0 / OIDC / JWT RS256) + ASP.NET Core Identity | `src/Identity` | Completado (3.1) |
| API Gateway / Ingress Edge | YARP sobre .NET 10, TLS, rate limiting L7 con Redis, validación JWT, CORS del SPA, hardening de cabeceras | `src/Gateway` | Completado (3.2) |
| Front-End | React 19 + Vite + TypeScript, SPA servida por nginx sin privilegios con CSP estricta | `src/Frontend` | Completado (4.1) |
| Hardening | Contenedores sin root, sin capabilities, de solo lectura y con límites; verificación reproducible | `docker-compose.yml`, `scripts/verify-hardening.sh` | Completado (4.2) |
| Observabilidad (Fase 4) | OpenTelemetry (trazas, métricas y logs por OTLP), Collector, Prometheus, Tempo, Loki, cAdvisor y Grafana con tres dashboards | `docker-compose.observability.yml`, `infra/{otel,prometheus,tempo,loki,grafana}` | Completado (5.2) |

## Pendientes

Quedan fuera del criterio de done de sus etapas:

| Punto | Pendiente |
|---|---|
| 3.1 | Pantalla de administración para asignar rol y `cliente_id` a los usuarios autoregistrados (hoy quedan sin rol) |
| 3.2 | Probar el rate limiting con varias instancias del gateway (hoy corre una) |
| 2.2 | Outbox transaccional para la auditoría: si Mongo falla justo después de confirmar en Postgres, puede quedar una operación sin evento (queda un warning en el log) |
| 4.1 | Correr los E2E en CI: hoy corren en local contra el compose (necesitan certificados y secretos) |

## Ejecutar el sistema

Requisitos: .NET SDK 10.0.400, Docker con Compose, Python 3 y, para los E2E, Node 22 con pnpm 11. Solo el gateway (`https://localhost:8080`), el Front-End (`http://localhost:3000`) y, con la observabilidad, Grafana (`http://localhost:3001`) publican puertos, y solo en 127.0.0.1.

```bash
dotnet test Secunitec.slnx -c Release       # los tests de Identity y Billing levantan Postgres, Mongo y Redis con Testcontainers
cp .env.example .env                         # reemplazar cada valor CAMBIAR_* y definir IDENTITY_SEED_DEMO_PASSWORD
scripts/dev-cert.sh                          # Windows: powershell -File scripts/dev-cert.ps1
dotnet dev-certs https --trust               # una vez, para que el navegador confíe en el gateway
docker compose --profile security up -d --build
docker compose --profile security ps         # bases y frontend en healthy; mongo-setup termina con código 0
scripts/verify-hardening.sh --report         # controles de 3.2 y 4.2: debe terminar con 0 FAIL
```

Con la observabilidad de la Fase 4 (etapa 5.2; en `.env`, `GRAFANA_ADMIN_PASSWORD`):

```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml --profile security --profile observability up -d --build
scripts/verify-observability.sh --report     # métricas propias, traza gateway → billing → Postgres y logs
scripts/verify-hardening.sh --report         # incluye los contenedores nuevos y los controles de TB3
```

Grafana queda en `http://localhost:3001` (usuario `admin`), con los dashboards "Resiliencia del gateway", "USE Overview" y "Trazas". Detalle en [docs/06](docs/06-innovacion-opentelemetry.md).

Con eso:

- **SPA:** `http://localhost:3000`. Usar `localhost` y no `127.0.0.1` ([problemas conocidos §7](docs/problemas-conocidos.md)).
- **Usuarios:** el admin de `.env` y, con `IDENTITY_SEED_DEMO_PASSWORD`, `facturador@`, `auditor@` y `cliente@secunitec.local` (el cliente ve solo las facturas del cliente de demostración).
- **Panel de resiliencia:** en "Resiliencia", 120 peticiones en 10 segundos muestran el límite de 60 por minuto con 429 y la cuenta regresiva del `Retry-After`.
- **E2E:**

  ```bash
  cd tests/e2e && pnpm install && pnpm exec playwright install chromium && pnpm test
  ```

- **Token de aplicación** (el que usará JMeter; rol Facturador en el tenant de ejemplo):

  ```bash
  export TOKEN=$(curl -s --cacert infra/certs/gateway.pem -X POST https://localhost:8080/connect/token \
    -d grant_type=client_credentials -d client_id=jmeter-load -d scope=secunitec-billing \
    --data-urlencode "client_secret=$(grep '^IDENTITY_JMETER_CLIENT_SECRET=' .env | cut -d= -f2-)" \
    | python3 -c "import json, sys; print(json.load(sys.stdin)['access_token'])")
  curl -i --cacert infra/certs/gateway.pem -H "Authorization: Bearer $TOKEN" https://localhost:8080/api/billing/facturas
  ```

- **Facturación por API:** en Development, Billing crea un obligado con CAI vigente y el cliente `SEED_CLIENTE_ID` en el tenant de ejemplo.
  - Crear: `POST /api/billing/facturas` con `{"clienteId":"<SEED_CLIENTE_ID>","lineas":[{"descripcion":"Servicio","cantidad":1,"precioUnitario":100,"exento":false}]}`.
  - Después: `POST /api/billing/facturas/<id>/emitir`, `GET /api/billing/facturas?estado=Emitida&pagina=1` y `POST /api/billing/facturas/<id>/anular` con `{"motivo":"..."}`.
  - El ISV esperado es 15 y el total 115. Los errores traen un `code` estable en ProblemDetails (por ejemplo `cai.vencido`).

Notas:

- Billing solo se alcanza a través del gateway y con tokens de Identity. El modo de prueba de la etapa 2.2 (`BILLING_TEST_JWT_KEY` y `scripts/dev-token.py`) queda solo para correr Billing aislado en Development con `dotnet run`.
- En Windows, los scripts `.sh` se ejecutan desde Git Bash; en PowerShell se usa `curl.exe` con los mismos parámetros.
- `dotnet-tools.json` fija `dotnet-ef` 10.0.12: `dotnet tool restore` y luego `dotnet dotnet-ef ...`. La migración `InitialBilling` tiene un id de 12 dígitos: `database update` no la encuentra por nombre (ver el comentario en la migración).
- Si Mongo no llega a `healthy`, el gateway no arranca, gitleaks falla en CI, falta un usuario de base o `mongo-setup` termina con error, ver [`docs/problemas-conocidos.md`](docs/problemas-conocidos.md).
