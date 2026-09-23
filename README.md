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
| Core de facturación | .NET 10, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core + PostgreSQL | `src/Billing` | Parcial: flujo completo funcionando, faltan tests y funciones (1.2, 1.3, 2.2) |
| Persistencia multi-modelo | PostgreSQL 17 (transaccional), MongoDB 8 (auditoría), Redis 7 (caché; contadores en etapa 3.2) | `infra/`, `docker-compose.yml` | Parcial: tres bases `healthy` en redes internas (2.1) |
| Identity Provider | .NET 10 + OpenIddict (OAuth 2.0 / OIDC / JWT RS256) + ASP.NET Core Identity | `src/Identity` | Pendiente (3.1) |
| API Gateway / Ingress Edge | YARP sobre .NET 10, rate limiting L7 con Redis, validación JWT, hardening de cabeceras | `src/Gateway` | Pendiente (3.2) |
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

Decisiones pendientes del equipo: otro tenant recibe 404 en vez del 403 del plan (no revela qué facturas existen); un solo `IBillingStore` en vez de los puertos por agregado; Billing publica `127.0.0.1:8082` en la red `edge` hasta que exista el gateway (3.2).

## Ejecutar y comprobar las etapas 1.2–2.2

Requisitos: .NET SDK 10.0.400 y Docker con Compose. Las bases de datos no publican puertos al host; la API provisional de Billing escucha **solo en `127.0.0.1:8082`** hasta que exista el gateway de la etapa 3.2.

```bash
dotnet restore Secunitec.slnx
dotnet build Secunitec.slnx -c Release --no-restore
dotnet test Secunitec.slnx -c Release --no-build
cp .env.example .env
# Cambiar TODOS los valores CAMBIAR_* de .env por claves propias largas.
docker compose --profile billing config --quiet
docker compose up -d                   # únicamente PostgreSQL, MongoDB y Redis (etapa 2.1)
docker compose ps                     # esperar los tres estados healthy
docker compose --profile billing up -d --build billing  # etapa 2.2
```

Si Mongo no llega a `healthy` o gitleaks falla en CI, revisar [`docs/problemas-conocidos.md`](docs/problemas-conocidos.md).

El script de inicialización de PostgreSQL crea `secunitec_billing` y `secunitec_identity` con usuarios diferentes; Mongo crea `secunitec_audit`. Los scripts de inicialización solo se ejecutan cuando el volumen está vacío: si cambiaste las contraseñas después del primer arranque, actualiza las credenciales en la base o crea un volumen nuevo **solo si puedes perder los datos de prueba**.

En 2.2 la clave `BILLING_TEST_JWT_KEY` de `.env` activa tokens HMAC **solo** bajo `ASPNETCORE_ENVIRONMENT=Development`. Sirve para probar mientras no existe Identity; en Production se usa `Jwt:Authority` con firma y audiencia OIDC. Para generar un token de prueba con los mismos bytes de la clave (sin imprimirla):

```bash
export BILLING_TEST_JWT_KEY='la-misma-clave-de-al-menos-32-caracteres-del-env'
export TENANT_ID='7d8e03f4-c0a3-4b80-b33a-5b012bf2cb33'
export TOKEN=$(python3 scripts/dev-token.py --tenant "$TENANT_ID" --role Admin)
curl -i http://127.0.0.1:8082/api/billing/facturas     # 401 sin token
curl -i -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8082/api/billing/facturas
curl -i -X POST -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"rtn":"08011999123456","razonSocial":"Secunitec","cai":"AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF","prefijo":"000-001-01","rangoDesde":1,"rangoHasta":100,"fechaLimiteEmision":"2026-12-31"}' \
  http://127.0.0.1:8082/api/billing/obligados
```

Después: `POST /api/billing/clientes` con `{"nombre":"Cliente de prueba","rtn":null,"email":null}`; tomar su `id` y crear una factura con `POST /api/billing/facturas` y `{"clienteId":"<id>","lineas":[{"descripcion":"Servicio","cantidad":1,"precioUnitario":100,"exento":false}]}`; luego `POST /api/billing/facturas/<id>/emitir`, `GET /api/billing/facturas` y `POST /api/billing/facturas/<id>/anular`. El ISV esperado del ejemplo es 15 y el total 115. Para comprobar los permisos, generar un token `--role Auditor` y verificar que el `GET` responde 200 y el `POST` responde 403. Con `--role Cliente --cliente <id>` solo aparecen sus facturas.

### Prueba manual en Windows PowerShell

Con los contenedores iniciados, ejecuta desde la raíz del proyecto (requiere Python 3 para generar tokens):

```powershell
$env:BILLING_TEST_JWT_KEY = ((Get-Content .env | Where-Object { $_ -match '^BILLING_TEST_JWT_KEY=' }) -split '=', 2)[1]
$tenant = [guid]::NewGuid().ToString()
$token = py -3 scripts/dev-token.py --tenant $tenant --role Admin
curl.exe -i --noproxy "*" http://127.0.0.1:8082/api/billing/facturas
curl.exe -i --noproxy "*" -H "Authorization: Bearer $token" http://127.0.0.1:8082/api/billing/facturas
```

La primera petición debe responder 401 y la segunda 200. El token dura 15 minutos; para probar otras acciones, utiliza el mismo valor de `$tenant` al generar uno nuevo con `py -3 scripts/dev-token.py --tenant $tenant --role Admin` y envíalo en `Authorization: Bearer <token>`.

Si la emisión devuelve HTTP 500, consulta el error del servidor con `docker compose --profile billing logs --tail=100 billing`. La imagen runtime `aspnet:10.0-noble-chiseled-extra` incluye los datos de zona horaria necesarios para calcular la fecha de emisión en `America/Tegucigalpa`.

Si no recibes HTTP 401 al comenzar, verifica que la API esté en ejecución con `docker compose --profile billing ps` y comprueba el acceso local con `curl.exe -i --noproxy "*" http://127.0.0.1:8082/api/billing/facturas`. Revisa también `docker compose --profile billing logs --tail=100 billing`.

El registro Mongo se escribe después de la transacción PostgreSQL. Si Mongo falla justo en ese momento, puede quedar una factura confirmada sin evento; antes de usarlo fuera de una demostración debe agregarse un outbox transaccional y un reintento de auditoría.
