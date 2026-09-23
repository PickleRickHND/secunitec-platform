# Etapa 4.1 — Front-End

## Objetivo

SPA desacoplado que consume los servicios protegidos por JWT (**R09**) y muestra en vivo cómo el gateway bloquea las ráfagas con 429 (**R10**). Para cubrir sus pantallas, la etapa también cerró los pendientes de 1.2 a 2.2 de Billing y los de 3.1 que el SPA necesitaba (CORS y llaves persistentes).

## Componentes

| Componente | Implementación |
|---|---|
| SPA | React 19.3 + Vite 8.3 + TypeScript 6.0 en `src/Frontend`, react-router-dom 7.18 |
| Login | oidc-client-ts 3.5: Authorization Code + PKCE contra Identity a través del gateway; renovación con refresh token, sin iframes |
| Tokens | `sessionStorage` (por pestaña, se borran al cerrarla). La CSP estricta mitiga el XSS |
| Contenedor | `nginxinc/nginx-unprivileged:alpine` (uid 101), multi-stage con Node 22; solo en la red `edge`, publicado en `127.0.0.1:3000` |
| Diseño | Dirección "institucional sobrio" (skill frontend-design): tokens en `src/styles/tokens.css`, Public Sans self-hosted con cifras tabulares. Las páginas de cuenta de Identity usan los mismos tokens |
| Gráfica | SVG propio, sin librería. Paleta categórica de referencia del skill dataviz, validada con `validate_palette.js` |

## Pantallas

La interfaz solo oculta lo que el rol no puede hacer: la autorización la decide siempre el servidor (T-Tampering del Front-End, A01).

| Ruta | Roles | Contenido |
|---|---|---|
| `/` | Público | Portada e inicio de sesión |
| `/inicio` | Todos | Borradores, emitidas y anuladas; últimas facturas |
| `/facturas` | Todos (Cliente: solo las suyas) | Listado paginado con filtros por estado y fecha de emisión |
| `/facturas/nueva` | Admin, Facturador | Cliente y líneas. El ISV y los totales los calcula el servidor |
| `/facturas/:id` | Todos | Documento fiscal (RTN, CAI, rango autorizado, fecha límite, correlativo); agregar o quitar líneas, emitir y anular con motivo |
| `/clientes` | Admin, Facturador | Buscar, registrar y editar |
| `/obligado` | Todos lo ven; Admin edita | CAI vigente, rango y números disponibles; registrar CAI nuevo, activar y desactivar la emisión |
| `/auditoria` | Admin, Auditor | Eventos de Billing e Identity del tenant, con filtros |
| `/resiliencia` | Todos | Panel de ráfagas (abajo) |

## Panel de resiliencia (R10)

- **Ráfaga:** N peticiones reales repartidas en M segundos (hasta 1000 en 120 s), con un tope de 24 en vuelo.
- **Escenarios:** con la sesión (200 y luego 429), sin token (401 y luego 429 por IP), token con la firma alterada (401), fuera del rol (403, auditado) y mixto. Para Admin no hay endpoint que devuelva 403: el escenario se deshabilita con la explicación.
- **En vivo:** KPIs de 200, 429, 401, 403 y otros (5xx o red); cuenta regresiva del `Retry-After`; gráfica de respuestas acumuladas por segundo con la línea del límite; últimas respuestas con su correlation id.
- **Accesibilidad de la gráfica:** leyenda, etiquetas directas al final de cada línea, crosshair con tooltip (también con las flechas del teclado) y vista de tabla. Tres colores de la paleta quedan bajo 3:1 sobre blanco: por eso los números siempre están también en texto.
- El panel usa `fetch` propio: los 401 y 429 de la ráfaga no redirigen al login ni muestran la barra de aviso global.

## Cambios en el backend para el SPA

| Servicio | Cambio |
|---|---|
| Gateway | CORS `spa` por ruta de YARP (`/connect/token`, `/connect/*`, `/.well-known/*`, `/api/billing/*`): solo `SECUNITEC_SPA_URL`, sin credenciales, expone `Retry-After` y `X-Correlation-Id`. `UseCors` va antes de autenticar y limitar: el preflight no gasta cupo y los 401 y 429 llevan `Access-Control-Allow-Origin` |
| BuildingBlocks | Las cabeceras de seguridad no pisan una CSP que ya trae la respuesta: el gateway le ponía `default-src 'none'` al login de Identity |
| Identity | `role`, `tenant_id` y `cliente_id` también en el id_token (con el scope `roles`); cierre de sesión OIDC (`/connect/endsession`); `prompt=none` sin sesión responde `login_required`; el cliente `spa-secunitec` se sincroniza en cada arranque; Data Protection en el volumen `identity_keys` y certificados PEM de firma y cifrado de `scripts/dev-cert.sh` |
| Identity (corrección) | En el canje del código los scopes salían del request de token, que no los trae: el access token del SPA quedaba sin audiencia `secunitec-billing` y Billing respondía 401 |
| Identity (corrección) | El `timestamp` de la auditoría se guardaba como documento BSON (no ordenable ni filtrable por rango); ahora es una fecha BSON |
| Billing | Todos los pendientes de 1.2 a 2.2 (ver abajo) |

## Pendientes de 1.2 a 2.2 cerrados

| Punto | Implementación |
|---|---|
| 1.2 | Value objects `Rtn`, `Cai` y `Money` (redondeo half away from zero); motivo obligatorio al anular; errores con códigos estables (`BillingErrorCodes`, en ProblemDetails como `code`); `EventoAuditoria` en el dominio; tests de `Cai`, redondeo, emitir dos veces, cantidades no positivas, líneas y CAI |
| 1.3 | Puertos del plan (`IFacturaRepository`, `IClienteRepository`, `IObligadoRepository`, `IUnitOfWork`, `INumeradorFacturas`, `IAuditoria`, `ICache`) y `IRequestContext`; FluentValidation; agregar y quitar líneas; listado con paginación y filtros; actualizar y listar clientes; actualizar CAI, activar y desactivar el obligado; accesos denegados auditados (en el caso de uso y en la política del endpoint) |
| 2.1 | Servicio `mongo-setup` idempotente: `billing_audit` solo con insert y find (antes `readWrite`), índices por `actor` y `correlationId`. Redis sin `CONFIG`, `FLUSHALL`, `FLUSHDB` ni `DEBUG` |
| 2.2 | Filtro global de EF por `tenant_id` (y por `cliente_id` para el rol Cliente); otro tenant recibe 403 y queda auditado; `ModelSnapshot` con la migración `AlinearEsquemaBilling`, que renombra restricciones e índices a los nombres de EF sin perder datos; DTOs y estados como texto; `tests/Secunitec.Billing.Api.Tests` con Testcontainers |

## Pruebas

| Proyecto | Qué cubre |
|---|---|
| `src/Frontend` (Vitest) | `Retry-After` en segundos, fecha HTTP, cuerpo del gateway e inválido; cuenta regresiva; clasificación de respuestas; calendario de la ráfaga; series acumuladas; escenarios por rol; normalización de roles; ProblemDetails; formatos de Honduras |
| `tests/e2e` (Playwright, contra el compose) | Facturador: crear cliente y facturas, agregar línea (el servidor recalcula), emitir y anular con motivo. Auditor: ve la auditoría y no escribe. Cliente: solo sus facturas. Admin: ráfaga de 120 peticiones en 10 s con 429, cero 5xx y la cuenta regresiva visible |
| `Secunitec.Gateway.Tests` | Preflight sin gastar cupo ni llegar al servicio, origen ajeno sin CORS, 401 y 429 con CORS y `Retry-After` expuesto |
| `Secunitec.Identity.Tests` | Authorization Code + PKCE con `role` y `tenant_id` en el id_token y audiencia en el access token; `login_required`; cierre de sesión; sincronización del cliente del SPA; estilos públicos con la CSP de Identity |
| `Secunitec.Billing.*.Tests` | Dominio, casos de uso con dobles en memoria y la API contra Postgres y Mongo reales (incluida la actualización de una base de la etapa 2.2) |

Los E2E comparten el límite de `/connect/token` (5 canjes por minuto por IP): corren en serie, con un inicio de sesión por rol, y si el gateway limita el canje esperan la cuenta regresiva de la pantalla en vez de fallar.

## Cómo ejecutarlo

```bash
docker compose --profile security up -d --build
cd tests/e2e && pnpm install && pnpm exec playwright install chromium && pnpm test
```

El SPA queda en `http://localhost:3000`. Usar `localhost` y no `127.0.0.1`: el navegador trata a localhost como contexto seguro (PKCE necesita `crypto.subtle`) y CORS compara el origen como texto.
