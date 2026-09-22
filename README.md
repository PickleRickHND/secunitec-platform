# Secunitec Platform

Plataforma de facturación electrónica y auditoría de seguridad para **Secunitec Corp.**
Proyecto final de Arquitectura de Sistemas Informáticos (UNITEC, Q3-2026).

Este README es el tablero del proyecto: el plan por etapas, qué está hecho, qué falta y cómo se trabaja. El detalle de arquitectura, requisitos (`R01`-`R22`), decisiones y controles de seguridad vive en [docs/PLAN.md](docs/PLAN.md).

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

Pistas: **A** = Identity, Gateway, hardening · **B** = Billing y bases de datos · **C** = Frontend, telemetría, estrés. Las tres pistas avanzan en paralelo; el orden de dependencias está en cada punto.

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

## Cómo trabajar en el repo

### Desarrollo local (Etapa 1, sin Docker)

Requisito: [.NET SDK 10.0.400](https://dotnet.microsoft.com/download/dotnet/10.0) o superior dentro de 10.0.x (`global.json`).

```bash
dotnet build Secunitec.slnx -c Release
dotnet test Secunitec.slnx -c Release
dotnet format Secunitec.slnx --verify-no-changes
```

- Las advertencias del compilador y de los analizadores se tratan como errores.
- `dotnet test` corre en modo Microsoft.Testing.Platform (configurado en `global.json`). Para un reporte TRX: `dotnet test Secunitec.slnx --results-directory TestResults -- --report-xunit-trx`.

### Ramas, PRs y protección de `main`

- Una rama por punto del plan: `feature/<etapa>-<nombre>` (ejemplo: `feature/1.2-billing-domain`).
- Commits con [Conventional Commits](https://www.conventionalcommits.org/): `feat(billing): ...`, `test(gateway): ...`, `docs(stride): ...`.
- `main` está protegida con un ruleset: no se puede pushear directo, hacer force-push ni borrarla. Todo cambio entra por **pull request** con los dos checks del CI en verde (`.NET build + test` y `Secretos (gitleaks)`) y con las conversaciones del review resueltas. Los administradores del repo pueden saltarse la regla solo para cambios de documentación.
- Antes de cada commit: `gitleaks git --staged --no-banner .` (el CI lo repite sobre todo el historial).

### Ejecución con Docker (a partir de la Etapa 2)

> El instructivo completo se publica al cerrar el punto 2.1. Requisitos previstos: Docker Desktop con Compose v2+.

```bash
cp .env.example .env
docker compose up --build
```

- Front-End: http://localhost:3000
- API Gateway: http://localhost:8080
- Grafana (Fase 4): http://localhost:3001

---

## Plan de trabajo detallado

### Etapa 1 · Cimientos (sin Docker)

Todo lo de esta etapa se construye y prueba solo con el SDK de .NET. Deja listos el contrato que comparten los servicios y el dominio de facturación, para que las etapas 2 y 3 puedan avanzar en paralelo.

#### 1.1 Proyecto base: solución .NET, reglas comunes y contrato del token — Completado (Pista A)

**Objetivo.** Tener una solución con reglas de calidad uniformes y un único lugar donde se define qué claims trae un token, qué roles existen y qué puede hacer cada uno, para que Identity los emita y Gateway, Billing y el Front los consuman sin ambigüedad.

**Qué se entregó.**

- Solución y reglas de build:
  - `global.json`: SDK 10.0.400 y `dotnet test` en modo Microsoft.Testing.Platform.
  - `Directory.Build.props`: `net10.0`, `Nullable`, `ImplicitUsings`, advertencias como errores, analizadores en nivel `latest-recommended` y estilo (`.editorconfig`) verificado en el build.
  - `Directory.Packages.props`: versiones de NuGet centralizadas (Central Package Management) con pinning transitivo.
  - `Secunitec.slnx` con carpetas `src/BuildingBlocks` y `tests`.
- `src/BuildingBlocks/Secunitec.BuildingBlocks` (librería pura, sin ASP.NET, para que `Billing.Application` pueda referenciarla):
  - `SecunitecClaims`: nombres crudos de los claims: `sub`, `role`, `tenant_id`, `cliente_id`, `client_id`, `name`, `email`. No se mapean a `ClaimTypes.*`.
  - `SecunitecRoles`: `Admin`, `Facturador`, `Auditor`, `Cliente`.
  - `SecunitecPolicies`: nombres de políticas (`secunitec:admin`, `secunitec:manage-obligados`, `secunitec:manage-clientes`, `secunitec:manage-invoices`, `secunitec:read-invoices`, `secunitec:read-audit`) y la matriz rol → política `RolesByPolicy`.
  - `SecunitecAudiences`: `secunitec-billing`, `secunitec-identity`.
  - `ICurrentUser`: abstracción del llamador (usuario, tenant, cliente, roles) que usará la capa de aplicación; el tenant nunca llega como parámetro del request.
  - `ClaimsPrincipalExtensions`: lectura tolerante de los claims (Guid inválido o ausente → `null`, nunca excepción); `cliente_id` solo se expone con rol `Cliente`.
  - `CorrelationId`: nombre de cabecera `X-Correlation-Id`, validación (1-64 caracteres `[A-Za-z0-9._-]`) y generador.
- `src/BuildingBlocks/Secunitec.BuildingBlocks.AspNetCore` (cross-cutting para los tres servicios web):
  - `AddSecunitecAuthorization()`: registra las políticas y fija `FallbackPolicy`/`DefaultPolicy` = autenticado con `tenant_id` (**R04**, deny-by-default: un endpoint sin metadatos exige token; los públicos deben declarar `[AllowAnonymous]`).
  - `SecurityHeadersMiddleware`: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, `Content-Security-Policy` (configurable por servicio; el default bloquea todo, apto para APIs JSON), `Cache-Control: no-store`, y elimina `X-Powered-By` y `Server` (**R06**).
  - `CorrelationIdMiddleware`: respeta un `X-Correlation-Id` válido, genera uno si falta o es inválido, lo devuelve en la respuesta y lo agrega al scope de logs.
  - `AddSecunitecProblemDetails()`: errores en formato RFC 9457 sin mensaje ni stack de la excepción (OWASP **A05**) y con `correlationId` para cruzar con logs y auditoría.
  - `ConfigureSecunitecKestrel()`: `AddServerHeader = false` (**R06**), body máximo 1 MiB, cabeceras máximas 32 KiB, timeouts de cabeceras y keep-alive, tasa mínima de lectura del body.
  - `AddSecunitecDefaults()` / `UseSecunitecDefaults()`: composición en el orden correcto (handler de excepciones → status code pages → correlation id → cabeceras). Los rechazos de Kestrel (413, 400) conservan su código real en vez de convertirse en 500.
- Tests (`tests/Secunitec.BuildingBlocks.Tests`, xUnit v3, 58 casos): matriz RBAC completa contra el `IAuthorizationService` real, 401 anónimo y 403 por política, claims inválidos, `cliente_id` con y sin rol `Cliente`, correlation id (generado, respetado, reemplazado, demasiado largo), cabeceras por defecto y CSP configurable, ProblemDetails sin fugas en `Production`, y Kestrel real en puerto efímero sin cabecera `Server` y con 413 por body excesivo.
- CI (`.github/workflows/ci.yml`): `dotnet restore` → `build` Release → `dotnet format --verify-no-changes` → `dotnet test` con TRX como artifact, más `gitleaks` sobre todo el historial.
- Protección de `main` (ruleset): PR obligatorio, checks obligatorios, sin force-push ni borrado.

**Decisiones tomadas (confirmar en equipo si alguien discrepa).**

- `tenant_id` es obligatorio en **todo** token de usuario, incluido `Admin`, que administra dentro de su obligado. Si más adelante hace falta un administrador de plataforma cross-tenant, se agrega como rol aparte sin tocar el contrato.
- Con `FallbackPolicy`, una ruta inexistente sin token responde 401 y no 404: sondear rutas no revela cuáles existen.
- Tests con xUnit v3 en modo Microsoft.Testing.Platform (el modo VSTest ya no está soportado en el SDK 10).

**Cómo verificarlo.** `dotnet build`, `dotnet test` y `dotnet format --verify-no-changes` en verde (ver sección de desarrollo local); CI verde en el [run del PR #1](https://github.com/PickleRickHND/secunitec-platform/actions/runs/35686600821).

#### 1.2 Reglas de facturación (`Billing.Domain`) — Pendiente (Pista B)

**Objetivo.** Modelar el dominio de facturación electrónica (contexto Honduras) con todas las reglas de negocio en código puro, probadas con tests unitarios, sin base de datos ni HTTP.

**Qué incluye.**

- Proyecto `src/Billing/Secunitec.Billing.Domain` (sin dependencias de infraestructura; puede referenciar `Secunitec.BuildingBlocks` para roles y `ICurrentUser`).
- Value objects con validación en el constructor:
  - `Rtn`: 14 dígitos.
  - `Cai`: código de autorización de impresión, con rango autorizado (desde, hasta), fecha límite de emisión y prefijo `000-001-01`.
  - `Money`: montos con dos decimales (`decimal`), redondeo definido.
- Entidades y agregados:
  - `ObligadoTributario` (tenant): RTN, razón social, CAI vigente, activo. Regla: un CAI vigente por obligado; no emite fuera del rango ni pasada la fecha límite.
  - `Cliente`: pertenece a un solo obligado; RTN o identidad, nombre, email.
  - `Factura` (agregado raíz): obligado, cliente, número correlativo `000-001-01-00000001`, fecha, estado, subtotal, ISV, total, creada por, líneas.
  - `LineaFactura`: descripción, cantidad y precio unitario positivos, exento, importe.
- Reglas de negocio:
  - Totales calculados en el dominio, nunca recibidos del cliente; ISV 15 % sobre líneas no exentas.
  - Correlativo consecutivo por CAI sin huecos (el bloqueo de fila se implementa en 2.2; el dominio expone la operación `AsignarNumero`).
  - Transiciones de estado `Borrador → Emitida → Anulada`; no se edita una factura emitida; no se anula dos veces; anular exige motivo.
  - Errores de dominio tipados (`DomainException` o `Result`) con códigos estables para que la API los traduzca a ProblemDetails.
- `EventoAuditoria` como contrato del dominio (actor, acción, recurso, resultado, IP, correlation id, detalle), que Infrastructure persistirá en Mongo.

**Depende de.** 1.1.

**Criterio de done.** `tests/Secunitec.Billing.Domain.Tests` cubre: `Rtn` y `Cai` válidos e inválidos; cálculo de ISV y totales con líneas exentas y no exentas; redondeo; correlativo y rango del CAI; fecha límite; cada transición de estado válida e inválida; líneas con cantidad o precio no positivos. CI verde.

#### 1.3 Acciones de facturación (`Billing.Application`) — Pendiente (Pista B)

**Objetivo.** Implementar los casos de uso sobre el dominio, con permisos por rol y aislamiento por tenant, probados con dobles (repositorios en memoria) y sin base de datos.

**Qué incluye.**

- Proyecto `src/Billing/Secunitec.Billing.Application` con casos de uso (commands/queries) y sus validadores (FluentValidation):
  - Facturas: crear (borrador), agregar/quitar líneas, emitir, anular, obtener por id, listar con paginación y filtros (estado, fechas, cliente).
  - Clientes: crear, actualizar, listar.
  - Obligados: crear, actualizar CAI, activar/desactivar.
- Puertos (interfaces) que Infrastructure implementará en 2.2: `IFacturaRepository`, `IClienteRepository`, `IObligadoRepository`, `IUnitOfWork`, `INumeradorFacturas` (correlativo atómico), `IAuditoria` (Mongo), `ICache` (Redis).
- Autorización en la capa de aplicación:
  - Cada caso de uso declara la política requerida de `SecunitecPolicies`.
  - Filtro ABAC: todo caso de uso toma el tenant de `ICurrentUser.TenantId`, nunca del request; el rol `Cliente` solo ve facturas con su `cliente_id`.
  - Intento de acceso a otro tenant → error de autorización (la API lo traduce a 403) y evento de auditoría.
- Registro de eventos de auditoría en emisión, anulación y accesos denegados.

**Depende de.** 1.1, 1.2.

**Criterio de done.** `tests/Secunitec.Billing.Application.Tests` con repositorios en memoria: flujo crear → emitir → anular; validaciones (líneas vacías, montos negativos); Facturador puede emitir, Auditor no; Cliente ve solo lo suyo; usuario de otro tenant recibe denegación y queda auditado. CI verde.

### Etapa 2 · Bases de datos (se enciende Docker)

#### 2.1 Postgres, Mongo y Redis en contenedores con redes aisladas — Pendiente (Pista B)

**Objetivo.** Levantar la persistencia multi-modelo con `docker compose`, con redes aisladas y credenciales por servicio, lista para que 2.2 se conecte.

**Qué incluye.**

- `docker-compose.yml` en la raíz (**R11**) con servicios `postgres` (17-alpine), `mongo` (8), `redis` (7-alpine) y las cuatro redes `edge`, `backend`, `data` y `observability` (**R12**); `backend` y `data` con `internal: true`. Ninguna base publica puertos al host.
- `infra/postgres/init/01-databases.sql`: bases `secunitec_identity` y `secunitec_billing` con un usuario distinto por base.
- `infra/mongo/init/01-audit.js`: base de auditoría, usuario con permisos solo de inserción y lectura, colección con índices por `timestamp`, `actor` y `correlationId`.
- `infra/redis/redis.conf`: `requirepass`, sin comandos peligrosos (`FLUSHALL`, `CONFIG` renombrados).
- Healthchecks en los tres servicios; `.env.example` con todas las variables y sin valores reales; `.env` git-ignored.
- CI: `docker compose config` valida el archivo.

**Depende de.** Docker Desktop encendido. No depende de código.

**Criterio de done.** `docker compose up` deja las tres bases en estado `healthy`; `docker network inspect` muestra que `data` es interna; ningún puerto de base de datos responde desde el host.

#### 2.2 Facturación conectada y expuesta (`Billing.Infrastructure` + `Billing.Api`) — Pendiente (Pista B)

**Objetivo.** Persistir el dominio en Postgres, auditar en Mongo, cachear en Redis y exponer los casos de uso como API HTTP protegida por JWT, probada con un token de prueba (Identity llega en 3.1).

**Qué incluye.**

- `Secunitec.Billing.Infrastructure`:
  - EF Core 10 + Npgsql: `DbContext`, configuraciones (`NUMERIC(18,2)` para montos, índices únicos en `numero` por obligado, constraints), migraciones.
  - Filtro global de EF Core por `tenant_id` tomado de `ICurrentUser` (**aislamiento de tenant**).
  - `INumeradorFacturas` con transacción y bloqueo de fila (`SELECT ... FOR UPDATE`) para el correlativo atómico.
  - Repositorio de auditoría en Mongo (solo inserción).
  - Caché en Redis para lecturas frecuentes (lista de facturas por tenant) con invalidación al emitir o anular.
- `Secunitec.Billing.Api`:
  - Minimal APIs bajo `/api/billing/...` (facturas, clientes, obligados), `RequireAuthorization` con las políticas de `SecunitecPolicies`.
  - `JwtBearer` con `Authority` = Identity, validación de `iss`, `aud` (`secunitec-billing`), `exp`, `nbf`; `MapInboundClaims = false`. Hasta que exista Identity, los tests usan un firmante de prueba.
  - `AddSecunitecDefaults()` / `UseSecunitecDefaults()` de 1.1; `Dockerfile` multi-stage sobre `aspnet:10.0-noble-chiseled` (non-root).
  - Servicio `billing` en el compose, en las redes `backend` y `data`.

**Depende de.** 1.3, 2.1.

**Criterio de done.** `tests/Secunitec.Billing.Api.Tests` con Testcontainers (Postgres + Mongo): crear → emitir → anular por HTTP; correlativo consecutivo bajo concurrencia; usuario de otro tenant recibe 403; el evento de auditoría queda escrito; `curl` con token de prueba desde el compose responde 200 y sin token 401.

### Etapa 3 · Seguridad

#### 3.1 Identity: registro de usuarios, login y emisión de tokens JWT con OAuth 2.0 / OIDC — Pendiente (Pista A)

**Objetivo.** Un proveedor de identidad propio en .NET 10 que emite JWT RS256 con los claims del contrato de 1.1 (**R03**).

**Qué incluye.**

- `src/Identity/Secunitec.Identity` con ASP.NET Core Identity (usuarios, roles, lockout tras 5 intentos, hash de contraseñas) sobre Postgres (`secunitec_identity`).
- OpenIddict 7: discovery (`/.well-known/openid-configuration`), JWKS, flujos Authorization Code + PKCE (SPA), Refresh Token rotativo y Client Credentials (`jmeter-load`, cliente confidencial).
- Tokens de acceso de vida corta (15 min), sin cifrar, con `sub`, `role`, `tenant_id`, `cliente_id` y `aud` = `secunitec-billing`.
- Páginas Razor mínimas de login y consentimiento; CSP propia (permite `self`).
- Seed de datos: obligado de prueba, usuarios por rol, clientes OAuth `spa-secunitec` (público) y `jmeter-load` (confidencial).
- Auditoría en Mongo de logins exitosos y fallidos con IP y correlation id (**A07**, **A09**).
- `AddSecunitecDefaults()` de 1.1, `Dockerfile` chiseled, servicio `identity` en el compose.

**Depende de.** 1.1, 2.1.

**Criterio de done.** `tests/Secunitec.Identity.Tests`: discovery 200; token por client credentials con los claims esperados; login fallido auditado; lockout al sexto intento. `curl` al discovery desde el compose.

#### 3.2 Gateway: única entrada, verificación del token, rate limiting con 429 y supresión de cabeceras — Pendiente (Pista A)

**Objetivo.** Un único punto de entrada que enruta a Identity y Billing (**R01**), rechaza tráfico anónimo, corta ráfagas con 429 (**R02**) y no revela tecnología (**R06**).

**Qué incluye.**

- `src/Gateway/Secunitec.Gateway` con YARP: rutas `/connect/*` y `/.well-known/*` → Identity; `/api/billing/*` → Billing.
- `JwtBearer` contra Identity; solo las rutas de OIDC son anónimas.
- Rate limiting L7 con `Microsoft.AspNetCore.RateLimiting` y contadores en Redis:
  - `anon-by-ip` para rutas anónimas.
  - `user-by-sub` (o `client_id`) para rutas autenticadas.
  - `token-endpoint` estricta para `/connect/token` (5 req/min por IP).
  - `OnRejected` → 429 + `Retry-After` + JSON `{ "error": "rate_limited" }`; la petición nunca llega al servicio interno.
- Transforms que eliminan `Server` y `X-Powered-By` de las respuestas proxied; límites de tamaño de body y timeouts; propagación de `X-Correlation-Id`; `X-Forwarded-*` controlados.
- Único servicio (junto al front) que publica puerto en el host (8080), en las redes `edge` y `backend`.

**Depende de.** 1.1, 3.1, 2.2.

**Criterio de done.** `tests/Secunitec.Gateway.Tests`: 401 sin token; 429 con `Retry-After` tras N peticiones; cabeceras correctas. `scripts/verify-hardening.sh` verde.

### Etapa 4 · Fachada

#### 4.1 Frontend: login, pantallas de facturas y panel que muestra los bloqueos 429 en vivo — Pendiente (Pista C)

**Objetivo.** Un SPA desacoplado que consume los servicios protegidos por JWT (**R09**) y muestra en tiempo real cómo el gateway bloquea ráfagas (**R10**).

**Qué incluye.**

- `src/Frontend` con React 19 + Vite 8 + TypeScript; `oidc-client-ts` con Authorization Code + PKCE contra Identity a través del gateway.
- Rutas protegidas por rol (`Admin`, `Facturador`, `Auditor`, `Cliente`); el front solo oculta UI, la autorización se decide en servidor.
- Pantallas: login, lista y detalle de facturas, crear (líneas, cálculo de totales mostrado desde la respuesta del servidor), emitir, anular; clientes; auditoría (solo `Auditor`/`Admin`).
- Panel de resiliencia: ráfaga configurable (N peticiones en M segundos), contadores en vivo de 200/401/403/429, countdown de `Retry-After` y gráfica de la serie.
- nginx non-root (`nginx-unprivileged`) con CSP estricta; `Dockerfile` multi-stage; servicio `frontend` en el compose (puerto 3000, red `edge`).

**Depende de.** 3.1, 3.2 (para probar contra servicios reales; el desarrollo puede empezar con mocks).

**Criterio de done.** Vitest: parsing de 429 y `Retry-After`. Playwright contra el compose: login, crear factura, emitir, y el panel muestra 429 durante una ráfaga.

#### 4.2 Endurecer y verificar: contenedores sin root y con límites; script de verificación con evidencia — Pendiente (Pista A)

**Objetivo.** Cerrar la Fase 2 con evidencia reproducible de cada política de seguridad del compose.

**Qué incluye.**

- En cada servicio del compose: usuario non-root, `cap_drop: [ALL]`, `read_only: true` con `tmpfs` donde haga falta, `security_opt: no-new-privileges`, `deploy.resources.limits` (CPU y memoria), secretos solo por `.env`.
- Límites bajos y deliberados en `billing` (por ejemplo 0.5 CPU, 256 MB) para poder alcanzar saturación en la Etapa 5.3.
- `scripts/verify-hardening.sh`: `curl` que comprueba cabeceras, 401, 403 y 429; `docker network inspect` de las redes internas; `docker inspect` de usuario, capabilities y límites; guarda la salida en `docs/04-hardening-verificacion.md`.
- CI: gitleaks (ya activo), `dotnet list package --vulnerable`, `pnpm audit` del front, `docker compose config`.

**Depende de.** 2.1 a 4.1.

**Criterio de done.** Checklist en `docs/04-hardening-verificacion.md` con la salida real de cada comando.

### Etapa 5 · Evidencia e informe

#### 5.1 Documentar amenazas (Fase 1): STRIDE, diagramas SecurUML, mapeo OWASP, trazabilidad al código — Pendiente (Todos, cada uno sus componentes)

**Qué incluye.**

- `docs/01-modelado-amenazas-stride.md`: por componente (Gateway, Identity, Billing, datos, Front), amenazas con ID `T-xx`, control que la mitiga y dónde está en el código.
- `docs/02-securuml/`: diagramas en Mermaid y PlantUML: despliegue con trust boundaries TB0-TB3, clases con estereotipos de roles y permisos, casos de uso con actores, secuencias de autenticación y de bloqueo 429. Exportados a PNG/SVG con `scripts/export-diagrams.sh`.
- `docs/03-owasp-top10-mapeo.md`: cada riesgo mapeado a OWASP Top 10 (2021) con el control correspondiente.
- `docs/trazabilidad.md`: tabla requisito → amenaza → OWASP → `archivo:línea` → prueba que lo demuestra. Los controles ya llevan comentarios `// R04`, `// R06`, `// A05` en el código para facilitarlo.

**Depende de.** Código real de las etapas 1 a 4 para citar `archivo:línea`.

**Criterio de done.** Revisión cruzada: cada amenaza tiene control y evidencia.

#### 5.2 Telemetría (Fase 4): OpenTelemetry y Grafana con paneles de consumo y de peticiones aceptadas o bloqueadas — Pendiente (Pista C)

**Qué incluye.**

- SDK de OpenTelemetry en los tres servicios: trazas (gateway → billing → Postgres), métricas (incluidas las propias `secunitec_gateway_rate_limited_total` y `secunitec_billing_invoices_emitted_total`) y logs, exportados por OTLP.
- `docker-compose.observability.yml` (perfil opcional): OTel Collector, Prometheus, Tempo, Loki, cAdvisor y Grafana (puerto 3001, red `observability`).
- Dashboards provisionados: "USE Overview" (CPU, memoria, red por contenedor), "Resiliencia del gateway" (200 vs 429 por segundo, `Retry-After`) y "Trazas".
- `docs/06-innovacion-opentelemetry.md`: justificación como tecnología no vista en clase y ventaja competitiva (**R19**).

**Depende de.** 3.2, 4.1.

**Criterio de done.** Los dashboards muestran tráfico real del compose y una traza cruza gateway → billing → Postgres.

#### 5.3 Pruebas de estrés (Fase 3): JMeter, `docker stats`, Método USE; meta: 429 controlados y cero 500 — Pendiente (Pista C)

**Qué incluye.**

- `tests/jmeter/`: `01-baseline.jmx` (50 usuarios), `02-ramp-saturation.jmx` (0 → 500) y `03-spike.jmx` (1000), con un setup thread group que obtiene el token por client credentials (`jmeter-load`) y varios usuarios para no chocar con la partición por `sub`.
- `scripts/stress/run-jmeter.sh` y `scripts/stress/capture-docker-stats.sh` (`docker stats` a CSV cada segundo).
- Ejecución real y `docs/05-pruebas-estres-use.md`: tabla USE por recurso (utilización, saturación, errores) para CPU, memoria, red, conexiones a Postgres y thread pool; gráficas 429 vs 500; conclusiones.

**Depende de.** 4.2 (límites de recursos) y 5.2 (dashboards como evidencia).

**Criterio de done.** Reporte HTML de JMeter, CSV de `docker stats` y capturas de Grafana; durante la saturación el gateway responde 429 y el total de 5xx es cero (**R18**).

#### 5.4 Informe técnico y presentación ejecutiva — Pendiente (Todos)

**Qué incluye.**

- `docs/informe/`: informe técnico DOCX con la estructura del enunciado: portada, índices, objetivos, introducción, marco teórico, desarrollo por fases con evidencias (diagramas, capturas, tablas USE), conclusiones, recomendaciones y bibliografía (**R20**).
- `docs/presentacion/`: presentación PPTX de 30 minutos con demo del panel de resiliencia (**R21**).
- README final con el instructivo de ejecución completo (**R22**) y tag `v1.0`.

**Depende de.** Todo lo anterior.

**Criterio de done.** Revisión final del equipo y tag publicado.

---

## Documentación

| Documento | Contenido | Estado |
|---|---|---|
| [docs/PLAN.md](docs/PLAN.md) | Requisitos `R01`-`R22`, decisiones de arquitectura, diagramas, dominio, controles STRIDE → OWASP, hitos y mapeo con las etapas | Vigente |
| `docs/01-modelado-amenazas-stride.md` | Fase 1: STRIDE por componente | Pendiente (5.1) |
| `docs/02-securuml/` | Fase 1: diagramas con RBAC/ABAC y trust boundaries | Pendiente (5.1) |
| `docs/03-owasp-top10-mapeo.md` | Fase 1: mapeo a OWASP Top 10 | Pendiente (5.1) |
| `docs/trazabilidad.md` | Requisito → amenaza → OWASP → código → prueba | Pendiente (5.1) |
| `docs/04-hardening-verificacion.md` | Fase 2: evidencia de políticas de seguridad | Pendiente (4.2) |
| `docs/05-pruebas-estres-use.md` | Fase 3: JMeter y Método USE | Pendiente (5.3) |
| `docs/06-innovacion-opentelemetry.md` | Fase 4: telemetría centralizada | Pendiente (5.2) |
| `docs/informe/`, `docs/presentacion/` | Entregables finales | Pendiente (5.4) |
