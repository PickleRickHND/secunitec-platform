# CLAUDE.md — Secunitec Platform

Instrucciones de proyecto. Responder en español. Detalle de requisitos (R01-R22), decisiones y fronteras de confianza en `docs/PLAN.md`; procedimientos de entorno en `docs/problemas-conocidos.md`.

## Qué es

Prototipo de facturación electrónica (contexto Honduras: CAI, RTN, ISV) con auditoría de seguridad para Secunitec Corp. Proyecto final de Arquitectura de Sistemas Informáticos (UNITEC, Q3-2026). Microservicios endurecidos con Docker Compose en redes aisladas: el gateway responde 429 controlados (con `Retry-After`) en vez de fallar bajo saturación.

Estado: etapas 1 a 5 completas (PRs #1 a #6). Pendiente la 5.4 (informe y presentación).

## Stack (versiones fijadas en el repo)

- .NET SDK 10.0.400 (`global.json`, `rollForward: latestFeature`), `net10.0`, Central Package Management (`Directory.Packages.props`).
- Gateway: YARP 2.3.0, JWT Bearer, RedisRateLimiting 1.2.1. Identity: OpenIddict 7.7.1 + ASP.NET Identity (Razor). Billing: Clean Architecture, EF Core 10.0.12 + Npgsql 10.0.3, FluentValidation 12.1.1.
- Datos: PostgreSQL 17 (facturas, usuarios), MongoDB 8 (auditoría solo inserción, driver 3.12.0), Redis 7 (rate limiting y caché).
- Front-End: React 19.3, Vite 8.3, TypeScript 6.0.3, react-router-dom 7.18, oidc-client-ts 3.5, Vitest 5, ESLint 10; servido por `nginx-unprivileged`. pnpm 11.24.0, Node >= 22.
- Tests: xUnit v3 en modo Microsoft.Testing.Platform, Testcontainers 4.15.0, Playwright 1.63.0, JMeter 5.6.
- Observabilidad (Fase 4, opcional): OpenTelemetry 1.19, collector 0.161.0, Prometheus 3.14, Tempo 3.0.3, Loki 3.7.8, Grafana 13.2.2, cAdvisor 0.60.6.
- Herramienta local: `dotnet-ef` 10.0.12 (`dotnet-tools.json`).

## Estructura

```
src/BuildingBlocks/   Secunitec.BuildingBlocks (claims, roles, políticas) y .AspNetCore (middlewares, ProblemDetails, Kestrel)
src/Gateway/          Secunitec.Gateway: única entrada, TLS, JWT, rate limiting L7, CORS, cabeceras
src/Identity/         Secunitec.Identity: OAuth 2.0 / OIDC, login Razor en wwwroot/account, Migrations/
src/Billing/          Domain · Application · Infrastructure (EF, Mongo, Redis, Migrations/) · Api
src/Frontend/         SPA (src/api, auth, features, components, styles); Dockerfile multi-stage
tests/                un proyecto xUnit por proyecto de src; e2e/ (Playwright); jmeter/ (planes .jmx)
infra/                postgres/init, mongo/setup, redis.conf, nginx, otel, prometheus, tempo, loki, grafana, certs/ (ignorado)
scripts/              dev-cert.sh|.ps1, dev-token.py, verify-hardening.sh, verify-observability.sh, stress/
docs/                 PLAN.md, ETAPA_*.md, 04/05/06 (evidencia), evidencia/5.2 y 5.3, problemas-conocidos.md
```

Solución: `Secunitec.slnx`. Los Dockerfiles de los servicios usan la raíz del repo como contexto (`context: .` en el compose).

## Comandos

Sistema completo (desde la raíz; requiere `.env` y certificados):

```bash
cp .env.example .env                 # luego reemplazar cada CAMBIAR_* (ver README)
scripts/dev-cert.sh                  # una vez; Windows: powershell -File scripts/dev-cert.ps1
dotnet dev-certs https --trust
docker compose up -d --build         # COMPOSE_PROFILES=security en .env levanta todo
docker compose ps                    # todo healthy; mongo-setup termina con código 0
```

- SPA en <http://localhost:3000>, gateway en <https://localhost:8080>, Grafana en <http://localhost:3001> (solo con observabilidad). Solo publican en `127.0.0.1`.
- Perfiles: `security` (sistema completo), `billing` (solo Billing y las bases), `observability` (con `docker-compose.observability.yml`; ver las líneas comentadas de `COMPOSE_FILE`/`COMPOSE_PROFILES` en `.env.example`).

.NET:

```bash
dotnet restore Secunitec.slnx && dotnet tool restore
dotnet build Secunitec.slnx -c Release                          # warnings como errores
dotnet format Secunitec.slnx --verify-no-changes                # lo que exige CI; sin --verify-no-changes corrige
dotnet test Secunitec.slnx -c Release                           # requiere Docker (Testcontainers)
dotnet test --project tests/Secunitec.Billing.Domain.Tests      # un solo proyecto (también --filter-class, --filter-method)
dotnet dotnet-ef migrations has-pending-model-changes -p src/Billing/Secunitec.Billing.Infrastructure -s src/Billing/Secunitec.Billing.Infrastructure
dotnet dotnet-ef migrations has-pending-model-changes -p src/Identity/Secunitec.Identity -s src/Identity/Secunitec.Identity
```

Nueva migración: `dotnet dotnet-ef migrations add <Nombre>` con los mismos `-p`/`-s` (hay `BillingDbContextFactory` y `ApplicationDbContextFactory` de diseño).

Front-End (`cd src/Frontend`):

```bash
pnpm install --frozen-lockfile
pnpm lint && pnpm typecheck && pnpm test && pnpm build
pnpm dev                             # Vite en :3000 con strictPort
```

E2E (`cd tests/e2e`, contra el compose levantado; lee usuarios de `.env`):

```bash
pnpm install && pnpm exec playwright install chromium && pnpm test
pnpm exec tsc --noEmit -p .          # lo único de E2E que corre en CI
```

Verificación y estrés (desde la raíz, con el compose arriba; requieren curl, python3 y docker):

```bash
scripts/verify-hardening.sh          # PASS/FAIL por control; debe terminar con 0 FAIL
scripts/verify-observability.sh      # requiere el perfil observability
scripts/stress/run-jmeter.sh 01-baseline|02-ramp-saturation|03-spike
python3 scripts/stress/analizar.py <carpeta>   # dependencias: scripts/stress/requirements.txt (.venv-stress/ ignorado)
```

- `--report` en los scripts de verificación sobrescribe la evidencia versionada (`docs/04-hardening-verificacion.md`, `docs/evidencia/5.2/`). Usarlo solo cuando se quiera actualizar esa evidencia.
- `run-jmeter.sh` exige `IDENTITY_JMETER_LOAD_CLIENTS > 0` en `.env` y la observabilidad arriba; resultados en `tests/jmeter/results/` (ignorado).
- `python3 scripts/dev-token.py --tenant <uuid> --role Facturador` genera un JWT HS256 solo para Billing con `dotnet run` en Development (`BILLING_TEST_JWT_KEY` = `Billing__TestJwtKey`). El compose no usa ese modo.

## Convenciones

Código:
- Comentarios y documentación en español. Identificadores técnicos en inglés; el dominio de facturación usa español (`Factura`, `Cliente`, `ObligadoTributario`, `Cai`, `Rtn`, `FacturasService`). Seguir el estilo del área que se toque.
- Cada control de seguridad lleva su ID en un comentario (`// R04: ...`, `// TB0: ...`) para la trazabilidad con `docs/PLAN.md`.
- `.editorconfig`: namespaces file-scoped, `using` fuera del namespace, llaves obligatorias, campos privados `_camelCase`, LF. `Nullable`, `TreatWarningsAsErrors`, analizadores `latest-recommended` y `EnforceCodeStyleInBuild` activos: un problema de estilo rompe el build.
- DTOs como `record`. Paquetes NuGet nuevos: versión en `Directory.Packages.props`, nunca en el `.csproj`; solo versiones estables.

Tests:
- Nombres `Metodo_Escenario_Resultado` (CA1707 desactivado en `tests/`). `TestContext.Current.CancellationToken` en llamadas async.
- Unit: dominio y casos de uso con dobles en memoria. Integración: Billing.Api, Billing.Infrastructure e Identity con Testcontainers + `WebApplicationFactory`. Contrato del gateway: 401/403/429 + `Retry-After` y cabeceras. Front: Vitest (`src/**/*.test.ts`). E2E: Playwright, un worker, en orden.

Git:
- Conventional Commits en español: `tipo(scope): descripción` en minúscula, sin punto final. Tipos usados: `feat`, `fix`, `docs`, `test`, `ci`, `style`, `refactor`, `chore`. Scopes frecuentes: `security`, `infra`, `frontend`, `gateway`, `identity`, `billing`, `billing-api`, `billing-infrastructure`, `building-blocks`, `observabilidad`, `scripts`, `tests`, `e2e`, `estres`, `readme`.
- Cuerpo del commit: el porqué del cambio, en prosa, con la evidencia si aplica (p. ej. "129 PASS, 0 FAIL").
- Ramas `feature/<etapa>-<nombre>` (p. ej. `feature/5-cierre-repo`); PR a `main` con CI verde, integrado con merge commit.
- `main` no tiene protección en GitHub (verificado con la API), aunque `docs/PLAN.md` §9 la da por protegida: la convención de PR se respeta a mano.

## CI (`.github/workflows/ci.yml`, en push a `main` y en cada PR)

- `.NET build + test`: restore, build Release, `dotnet format --verify-no-changes`, migraciones al día (Billing e Identity), tests con TRX.
- `Front-End lint + test + build`: lint, typecheck, test, build; compilación de E2E; `tokens.css` del SPA idéntico al de Identity.
- `Dependencias vulnerables`: NuGet (directas y transitivas, parseando el JSON) y `pnpm audit --audit-level=high` en Front-End y E2E.
- `Secretos (gitleaks)`: historial completo con `.gitleaks.toml`.
- `Docker Compose config`: sintaxis con todos los perfiles, que `up` sin flags incluya los 7 servicios, overlay de observabilidad y configs de collector, Prometheus y Loki.

## Seguridad

- Secretos solo en `.env` (ignorado). No leer, imprimir ni copiar sus valores; `.env.example` lleva placeholders `CAMBIAR_*` e Identity/Billing no arrancan si un secreto conserva ese prefijo.
- `infra/certs/*`, `*.pem`, `*.key`, `*.pfx` están ignorados y excluidos de las imágenes (`.dockerignore`).
- Antes de cada commit: `gitleaks git --staged --no-banner .`. La única excepción de `.gitleaks.toml` es la regex de `CAMBIAR_*`; antes de agregar otra, leer `docs/problemas-conocidos.md` §2 (gitleaks-action corre la 8.24.3 e ignora `[[allowlists]]`).
- Dejar `Billing__TestJwtKey` vacío en `.env`: con la clave, Billing aceptaría tokens HS256 de secreto compartido.
- No bajar los límites de rate limiting para que pasen pruebas: son controles verificados (R02: 5 canjes de `/connect/token` por minuto por IP; 60 por minuto por usuario o anónimo, `src/Gateway/Secunitec.Gateway/appsettings.json`).

## Gotchas

- Usar `localhost`, no `127.0.0.1`: CORS y las redirect URIs de OIDC comparan texto, y PKCE necesita contexto seguro.
- El SPA fija `SECUNITEC_PUBLIC_URL` y `SECUNITEC_SPA_URL` al construir la imagen: si cambian, `docker compose up -d --build`.
- `pnpm dev` usa el puerto 3000 con `strictPort`: choca con el contenedor `frontend`.
- Billing e Identity aplican migraciones EF al arrancar (`MigrateAsync`): un cambio de modelo sin migración rompe CI y tumba el servicio.
- `src/Frontend/src/styles/tokens.css` y `src/Identity/Secunitec.Identity/wwwroot/account/assets/tokens.css` deben ser idénticos (CI los compara con `diff`).
- Los tests con Testcontainers necesitan Docker encendido.
- Sin los PEM de `scripts/dev-cert.sh`, reiniciar Identity invalida sesiones y refresh tokens.
- MongoDB 8 con kernel Linux 6.19+ (Docker Desktop): `MONGO_GLIBC_TUNABLES` en `.env`; procedimiento en `docs/problemas-conocidos.md` §1.
- Grafana guarda su contraseña en el volumen `grafana_data` al primer arranque; cambiarla en `.env` después no tiene efecto.
- Navegador, Playwright, curl y JMeter comparten la IP de Docker Desktop y el cupo de `/connect/token`: los E2E y los scripts esperan el `Retry-After` solos.
- `dotnet list package --vulnerable` termina en 0 aunque encuentre vulnerables; CI revisa el JSON.
