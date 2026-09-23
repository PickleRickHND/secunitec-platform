# Etapa 3.2 â€” Gateway

## Objetivo

Convertir el Gateway en el Ãºnico punto de entrada publicado y proteger el acceso a Billing mediante validaciÃ³n JWT, rate limiting y hardening HTTP.

## Componentes

| Componente | ImplementaciÃ³n |
|---|---|
| Gateway | YARP sobre .NET 10 |
| Entrada publicada | `127.0.0.1:8080` |
| Identity upstream | `/connect/*`, `/account/*`, discovery |
| Billing upstream | `/api/billing/*` |
| AutenticaciÃ³n | JWT Bearer contra Identity |
| Rate limiting | Redis |
| Correlation | ID propagado hacia los servicios |
| Hardening | SupresiÃ³n de cabeceras y lÃ­mites de peticiÃ³n |

## Enrutamiento

El Gateway expone las rutas necesarias para el flujo OIDC y Billing. Billing deja de publicar su puerto directamente al host dentro del perfil `security`.

La topologÃ­a queda:

`cliente â†’ Gateway â†’ Identity/Billing`

Las redes `backend` y `data` siguen aisladas y el Ãºnico puerto externo de esta etapa es el del Gateway.

## Rate limiting

`R02` se implementa mediante contadores en Redis.

Reglas principales:

- Endpoint de token: 5 solicitudes por minuto por IP.
- TrÃ¡fico autenticado: 60 solicitudes por minuto por `sub`; cuando no existe identidad, se utiliza la particiÃ³n por IP.
- Exceso de lÃ­mite: HTTP `429`.
- La respuesta incluye `Retry-After` y JSON con `error = rate_limited`.

El rechazo ocurre en Gateway antes de alcanzar Billing, evitando consumir recursos internos durante una rÃ¡faga.

## ValidaciÃ³n JWT

El Gateway valida:

- emisor (`iss`);
- audiencia (`aud`);
- firma mediante discovery/JWKS de Identity;
- vigencia del token.

Billing mantiene la validaciÃ³n propia para defensa en profundidad.

## Hardening

`R06` se aplica eliminando cabeceras de fingerprinting como `Server` y `X-Powered-By`.

El Gateway tambiÃ©n conserva los controles comunes de correlation ID, ProblemDetails, lÃ­mites de cuerpo y timeouts establecidos por el proyecto.

## ValidaciÃ³n realizada

- `dotnet build Secunitec.slnx -c Release --no-restore`: correcto.
- `dotnet test Secunitec.slnx -c Release --no-restore`: 72 pruebas correctas.
- `docker compose --profile security config --quiet`: correcto.

## Pendiente antes de considerar H4 completamente cerrado

Debe probarse con el compose real:

1. `401` sin JWT.
2. Acceso correcto con JWT vÃ¡lido.
3. `429` despuÃ©s de superar el lÃ­mite.
4. Presencia correcta de `Retry-After`.
5. Ausencia de `Server` / `X-Powered-By`.
6. Proxy correcto hacia Identity y Billing.

Las pruebas especÃ­ficas de Gateway (`401`, `429`, headers) previstas en el plan todavÃ­a no forman parte de los 72 tests existentes.
