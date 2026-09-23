# Etapa 3.2 — Gateway

## Objetivo

Convertir el Gateway en el único punto de entrada publicado y proteger el acceso a Billing mediante validación JWT, rate limiting y hardening HTTP.

## Componentes

| Componente | Implementación |
|---|---|
| Gateway | YARP sobre .NET 10 |
| Entrada publicada | `127.0.0.1:8080` |
| Identity upstream | `/connect/*`, `/account/*`, discovery |
| Billing upstream | `/api/billing/*` |
| Autenticación | JWT Bearer contra Identity |
| Rate limiting | Redis |
| Correlation | ID propagado hacia los servicios |
| Hardening | Supresión de cabeceras y límites de petición |

## Enrutamiento

El Gateway expone las rutas necesarias para el flujo OIDC y Billing. Billing deja de publicar su puerto directamente al host dentro del perfil `security`.

La topología queda:

`cliente → Gateway → Identity/Billing`

Las redes `backend` y `data` siguen aisladas y el único puerto externo de esta etapa es el del Gateway.

## Rate limiting

`R02` se implementa mediante contadores en Redis.

Reglas principales:

- Endpoint de token: 5 solicitudes por minuto por IP.
- Tráfico autenticado: 60 solicitudes por minuto por `sub`; cuando no existe identidad, se utiliza la partición por IP.
- Exceso de límite: HTTP `429`.
- La respuesta incluye `Retry-After` y JSON con `error = rate_limited`.

El rechazo ocurre en Gateway antes de alcanzar Billing, evitando consumir recursos internos durante una ráfaga.

## Validación JWT

El Gateway valida:

- emisor (`iss`);
- audiencia (`aud`);
- firma mediante discovery/JWKS de Identity;
- vigencia del token.

Billing mantiene la validación propia para defensa en profundidad.

## Hardening

`R06` se aplica eliminando cabeceras de fingerprinting como `Server` y `X-Powered-By`.

El Gateway también conserva los controles comunes de correlation ID, ProblemDetails, límites de cuerpo y timeouts establecidos por el proyecto.

## Validación realizada

- `dotnet build Secunitec.slnx -c Release --no-restore`: correcto.
- `dotnet test Secunitec.slnx -c Release --no-restore`: 72 pruebas correctas.
- `docker compose --profile security config --quiet`: correcto.

## Pendiente antes de considerar H4 completamente cerrado

Debe probarse con el compose real:

1. `401` sin JWT.
2. Acceso correcto con JWT válido.
3. `429` después de superar el límite.
4. Presencia correcta de `Retry-After`.
5. Ausencia de `Server` / `X-Powered-By`.
6. Proxy correcto hacia Identity y Billing.

Las pruebas específicas de Gateway (`401`, `429`, headers) previstas en el plan todavía no forman parte de los 72 tests existentes.
