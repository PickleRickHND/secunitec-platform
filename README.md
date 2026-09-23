# Secunitec Platform

Plataforma de facturación electrónica y auditoría de seguridad para **Secunitec Corp.**
Proyecto final de Arquitectura de Sistemas Informáticos (UNITEC, Q3-2026).

Este README es el tablero del proyecto: el plan por etapas, qué está hecho y qué falta. El detalle de arquitectura, requisitos (`R01`-`R22`), decisiones y controles de seguridad vive en [docs/PLAN.md](docs/PLAN.md).

## Estado general

| Etapa | Punto | Pista | Estado |
|---|---|---|---|
| 1 · Cimientos (sin Docker) | 1.1 Proyecto base, reglas comunes y contrato del token | A | **Completado** ([PR #1](https://github.com/PickleRickHND/secunitec-platform/pull/1)) |
| | 1.2 Reglas de facturación (`Billing.Domain`) | B | **Parcial** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2)); ver [pendientes](#pendientes-de-12-a-22) |
| | 1.3 Acciones de facturación (`Billing.Application`) | B | **Parcial** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2)); ver [pendientes](#pendientes-de-12-a-22) |
| 2 · Bases de datos (se enciende Docker) | 2.1 Postgres, Mongo y Redis en contenedores | B | **Parcial** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2)); ver [pendientes](#pendientes-de-12-a-22) |
| | 2.2 Facturación conectada y expuesta (`Infrastructure` + `Api`) | B | **Parcial** ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2)); ver [pendientes](#pendientes-de-12-a-22) |
| 3 · Seguridad | 3.1 Identity (OAuth 2.0 / OIDC / JWT) | A | **Completado** ([PR #3](https://github.com/PickleRickHND/secunitec-platform/pull/3)); ver [pendientes](#pendientes-de-31-y-32) |
| | 3.2 Gateway (única entrada, 429, cabeceras) | A | **Completado** ([PR #3](https://github.com/PickleRickHND/secunitec-platform/pull/3)); ver [pendientes](#pendientes-de-31-y-32) |
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
| Core de facturación | .NET 10, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core + PostgreSQL | `src/Billing` | Parcial: flujo completo funcionando, faltan tests y funciones (1.2, 1.3, 2.2) |
| Persistencia multi-modelo | PostgreSQL 17 (transaccional), MongoDB 8 (auditoría), Redis 7 (caché; contadores en etapa 3.2) | `infra/`, `docker-compose.yml` | Parcial: tres bases `healthy` en redes internas (2.1) |
| Identity Provider | .NET 10 + OpenIddict (OAuth 2.0 / OIDC / JWT RS256) + ASP.NET Core Identity | `src/Identity` | Completado (3.1) |
| API Gateway / Ingress Edge | YARP sobre .NET 10, TLS, rate limiting L7 con Redis, validación JWT, hardening de cabeceras | `src/Gateway` | Completado (3.2) |
| Front-End | React 19 + Vite + TypeScript, SPA servida por nginx | `src/Frontend` | Pendiente (4.1) |
| Observabilidad (Fase 4) | OpenTelemetry Collector, Prometheus, Tempo, Loki, Grafana, cAdvisor | `infra/`, `docker-compose.observability.yml` | Pendiente (5.2) |

## Pendientes de 1.2 a 2.2

Mergeado como parcial el 2026-09-22 ([PR #2](https://github.com/PickleRickHND/secunitec-platform/pull/2)). Funciona y está verificado: CI verde (build, formato, 72 tests, compose y gitleaks); con el compose real, el flujo crear → emitir → anular, permisos por rol y tenant, auditoría en Mongo, caché con Redis caído y correlativo sin huecos bajo concurrencia. Falta cumplir estos puntos del criterio de done de cada etapa:

| Punto | Pendiente |
|---|---|
| 1.2 | Tests: `Cai` válido e inválido, redondeo explícito, emitir dos veces, cantidad no positiva. Value objects `Rtn`, `Cai` y `Money` (hoy son validadores). Motivo obligatorio al anular. Errores con códigos estables. `EventoAuditoria` como contrato del dominio |
| 1.3 | Tests: flujo crear → emitir → anular, líneas vacías y montos negativos, Facturador puede emitir. Auditar accesos denegados. FluentValidation. Agregar y quitar líneas; listar con paginación y filtros; actualizar y listar clientes; actualizar CAI y activar o desactivar obligados |
| 2.1 | Usuario de Mongo solo con inserción y lectura (hoy `readWrite`). Índices por `actor` y `correlationId`. Renombrar `FLUSHALL` y `CONFIG` en Redis |
| 2.2 | `tests/Secunitec.Billing.Api.Tests` con Testcontainers: crear → emitir → anular por HTTP, correlativo bajo concurrencia, otro tenant denegado, evento de auditoría escrito. Filtro global de EF por `tenant_id`. Migración con `ModelSnapshot`. Respuestas con DTOs. Campos `Resultado`, `Ip` y `CorrelationId` en la auditoría |

Decisiones pendientes del equipo: otro tenant recibe 404 en vez del 403 del plan (no revela qué facturas existen); un solo `IBillingStore` en vez de los puertos por agregado. Desde 3.2, Billing ya no publica puerto propio: solo se llega por el gateway.

## Pendientes de 3.1 y 3.2

Completados con el [PR #3](https://github.com/PickleRickHND/secunitec-platform/pull/3). El criterio de done se cumple: `Identity.Tests`, `Gateway.Tests` y `scripts/verify-hardening.sh` están en verde, y se probó el flujo completo contra el compose (PKCE, client credentials, Billing a través del gateway, 429, Redis caído). Quedan fuera de ese criterio:

| Punto | Pendiente |
|---|---|
| 3.1 | CORS en el gateway para que el SPA (4.1) llame a `/connect/token` desde el navegador. Persistir las llaves de Data Protection y de firma en Development, para no perder sesiones al reiniciar Identity ([problemas conocidos §6](docs/problemas-conocidos.md)). Pantalla de administración para asignar rol y `cliente_id` a los usuarios autoregistrados |
| 3.2 | Probar el rate limiting con varias instancias del gateway (hoy corre una) |

Detalle de cada etapa: [`docs/ETAPA_3_1_IDENTITY.md`](docs/ETAPA_3_1_IDENTITY.md) y [`docs/ETAPA_3_2_GATEWAY.md`](docs/ETAPA_3_2_GATEWAY.md).

## Ejecutar el sistema (etapas 1.2 a 3.2)

Requisitos: .NET SDK 10.0.400, Docker con Compose y Python 3. Solo el gateway publica un puerto en el host: `https://localhost:8080`.

```bash
dotnet test Secunitec.slnx -c Release       # Identity.Tests levanta Postgres y Mongo con Testcontainers
cp .env.example .env                         # y reemplazar cada valor CAMBIAR_*
scripts/dev-cert.sh                          # Windows: powershell -File scripts/dev-cert.ps1
dotnet dev-certs https --trust               # una vez, para que el navegador confíe en el gateway
docker compose --profile security up -d --build
docker compose --profile security ps         # postgres, mongo y redis en healthy
scripts/verify-hardening.sh                  # controles de 3.2 contra el compose: debe terminar con 0 FAIL
```

Con eso:

- **Login:** `https://localhost:8080/account/login`, con el admin de `.env`. Si se definió `IDENTITY_SEED_DEMO_PASSWORD`, también con `facturador@`, `auditor@` y `cliente@secunitec.local`.
- **Token de aplicación** (el que usará JMeter; rol Facturador en el tenant de ejemplo):

  ```bash
  export TOKEN=$(curl -s --cacert infra/certs/gateway.pem -X POST https://localhost:8080/connect/token \
    -d grant_type=client_credentials -d client_id=jmeter-load -d scope=secunitec-billing \
    --data-urlencode "client_secret=$(grep '^IDENTITY_JMETER_CLIENT_SECRET=' .env | cut -d= -f2-)" \
    | python3 -c "import json, sys; print(json.load(sys.stdin)['access_token'])")
  curl -i --cacert infra/certs/gateway.pem -H "Authorization: Bearer $TOKEN" https://localhost:8080/api/billing/facturas
  ```

- **Facturación de ejemplo:** en Development, Billing crea un obligado con CAI vigente y el cliente `SEED_CLIENTE_ID` en el tenant de ejemplo.
  - Crear: `POST /api/billing/facturas` con `{"clienteId":"<SEED_CLIENTE_ID>","lineas":[{"descripcion":"Servicio","cantidad":1,"precioUnitario":100,"exento":false}]}`.
  - Después: `POST /api/billing/facturas/<id>/emitir`, `GET /api/billing/facturas` y `POST /api/billing/facturas/<id>/anular`.
  - El ISV esperado es 15 y el total 115.

Notas:

- Billing solo se alcanza a través del gateway y con tokens de Identity. El modo de prueba de la etapa 2.2 (`BILLING_TEST_JWT_KEY` y `scripts/dev-token.py`) queda solo para correr Billing aislado en Development con `dotnet run`.
- En Windows, los scripts `.sh` se ejecutan desde Git Bash; en PowerShell se usa `curl.exe` con los mismos parámetros.
- Los scripts de inicialización de Postgres y Mongo solo corren con los volúmenes vacíos. Si Mongo no llega a `healthy`, el gateway no arranca, gitleaks falla en CI o falta un usuario de base, ver [`docs/problemas-conocidos.md`](docs/problemas-conocidos.md).
- La auditoría de Billing se escribe en Mongo después de la transacción de Postgres. Si Mongo falla justo en ese momento puede quedar una factura sin evento; fuera de una demostración haría falta un outbox transaccional.
