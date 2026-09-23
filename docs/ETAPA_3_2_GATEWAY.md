# Etapa 3.2 — Gateway

## Objetivo

Único punto de entrada publicado (**R01**). Enruta hacia Identity y Billing, rechaza el tráfico anónimo fuera del protocolo OIDC (**R04**), corta las ráfagas con 429 (**R02**) y no revela tecnología (**R06**).

## Componentes

| Componente | Implementación |
|---|---|
| Proxy | YARP 2.3 sobre .NET 10 |
| Entrada | `https://localhost:8080`, publicada solo en `127.0.0.1`. TLS con el certificado de desarrollo (TB0) |
| Rutas anónimas | `/connect/token` (política `token-endpoint`); `/connect/*`, `/.well-known/*` y `/account/*` (`anon-by-ip`) |
| Rutas protegidas | `/api/billing/*` (política `default`: autenticado y con `tenant_id`; rate limit `user-by-sub`) |
| Autenticación | JwtBearer contra Identity: issuer público, JWKS descargado por la red interna |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` con contadores en Redis (`RedisRateLimiting`), con respaldo en memoria |
| Redes | `edge` (publicada), `backend` (Identity y Billing) y `data` (Redis) |

## Pipeline

`UseSecunitecDefaults` (ProblemDetails, correlation id, cabeceras) → `UseHsts` → `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` → `MapReverseProxy`.

El rate limiting va entre la autenticación y la autorización por dos motivos:
- conoce el `sub` y puede particionar por usuario;
- un token inválido cae a la partición por IP y también recibe 429 antes que el 401.

## Rate limiting (R02)

| Política | Partición | Límite por defecto |
|---|---|---|
| `token-endpoint` | IP | 5 por minuto |
| `anon-by-ip` | IP | 60 por minuto |
| `user-by-sub` | `sub` (o `client_id`); sin token válido, la IP | 60 por minuto |

- Al pasar el límite, el gateway responde `429` con `Retry-After` y `{"error":"rate_limited","retry_after":N}`, y la petición no llega al servicio interno.
- Los límites se configuran en la sección `RateLimiting`, útil para las pruebas de carga de la Fase 3.
- Si Redis no responde, se cuenta en memoria por instancia (PLAN §10) y se registra un warning cada 30 s como máximo. Nunca se responde 500.

## Validación JWT

El gateway valida firma, `iss`, `aud = secunitec-billing` y vigencia con el helper común `UseSecunitecIdentity` de BuildingBlocks. Billing repite la misma validación, como defensa en profundidad.

- **Issuer y JWKS:** el issuer es la URL pública. El JWKS se descarga de `http://identity:8080` mediante `InternalAuthorityHandler`, que reescribe la URL y agrega `X-Forwarded-Host` y `X-Forwarded-Proto` públicos.
- **Rotación de llaves:** ante un `kid` desconocido, por ejemplo tras reiniciar Identity, el JWKS se vuelve a pedir; el intervalo mínimo entre refrescos es de 30 s.

## Hardening

- **R06:** sin `Server` ni `X-Powered-By`. Kestrel no los emite y los transforms de YARP los quitan de las respuestas reenviadas.
- **Cabeceras:** `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` y CSP.
- **HSTS:** en HTTPS, salvo en localhost; ver `docs/problemas-conocidos.md`.
- **Límites:** de body y de cabeceras en Kestrel (1.1), y timeout de 30 s por cluster.
- **Correlation id:** el `X-Correlation-Id` validado o generado viaja a Identity y Billing.
- **`X-Forwarded-*`:** YARP los reemplaza siempre, así que los que manda el cliente se descartan.

## Pruebas

`tests/Secunitec.Gateway.Tests` no necesita Docker: usa un Redis inalcanzable, que fuerza el respaldo en memoria, y un backend falso en Kestrel. Cubre:
- 401 sin token y 403 sin `tenant_id`;
- rutas OIDC anónimas;
- 429 con `Retry-After` y JSON en `/connect/token` y por `sub`, sin llegar al servicio;
- tokens inválidos limitados por IP;
- cabeceras;
- correlation id propagado;
- `/health` con Redis caído.

`scripts/verify-hardening.sh` verifica los mismos controles contra el compose real, además de las redes internas y que solo el gateway publique puertos.
