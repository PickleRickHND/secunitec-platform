# Etapa 3.1 â€” Identity

## Objetivo

Implementar el proveedor de identidad de Secunitec para registro, login y emisiÃ³n de tokens mediante OAuth 2.0 / OpenID Connect, manteniendo el contrato de claims definido en la etapa 1.1.

## Componentes

| Componente | ImplementaciÃ³n |
|---|---|
| Usuarios | ASP.NET Core Identity con `ApplicationUser` basado en GUID |
| Persistencia | PostgreSQL `secunitec_identity` |
| Protocolo | OpenIddict 7.7.1 |
| Flujos | Authorization Code + PKCE, Refresh Token y Client Credentials |
| Tokens | Access token JWT con claims del contrato Secunitec |
| Discovery | `/.well-known/openid-configuration` |
| AuditorÃ­a | MongoDB, eventos de registro/login y fallos de autenticaciÃ³n |
| Contenedor | .NET 10 + imagen runtime chiseled |

## Controles de seguridad

`R03`: Identity gestiona OAuth 2.0 / OIDC, emisiÃ³n y validaciÃ³n de tokens.

`R04`: la autorizaciÃ³n es deny-by-default; Ãºnicamente los endpoints que el protocolo necesita exponer de forma anÃ³nima utilizan acceso anÃ³nimo explÃ­cito.

`R08`: los eventos de autenticaciÃ³n se registran en Mongo como evidencia de auditorÃ­a.

La polÃ­tica de contraseÃ±a exige longitud mÃ­nima de 12 caracteres, mayÃºsculas, minÃºsculas, dÃ­gitos y carÃ¡cter no alfanumÃ©rico. El bloqueo se configura despuÃ©s de 5 intentos fallidos durante 15 minutos.

Los tokens de acceso tienen vida corta y los refresh tokens tienen una vigencia mayor para permitir renovaciÃ³n sin volver a solicitar credenciales.

## Claims

El `IdentityPrincipalFactory` mantiene los claims crudos del contrato:

`sub`, `role`, `tenant_id`, `cliente_id` y `client_id`.

Para Billing se establece la audiencia `secunitec-billing`.

## Clientes OAuth

Se registran:

- `spa-secunitec`: cliente pÃºblico para Authorization Code + PKCE.
- `jmeter-load`: cliente confidencial para Client Credentials y pruebas de carga.

## Migraciones

La base de datos de Identity se versiona mediante EF Core. Se incluye:

- `IdentityInitialCreate`
- `ApplicationDbContextModelSnapshot`
- `ApplicationDbContextFactory` para operaciones de diseÃ±o y generaciÃ³n de migraciones.

La migraciÃ³n generada contiene una supresiÃ³n local de diagnÃ³sticos de analizadores que no cambian el comportamiento de la migraciÃ³n.

## ValidaciÃ³n realizada

- `dotnet build Secunitec.slnx -c Release --no-restore`: correcto.
- `dotnet test Secunitec.slnx -c Release --no-restore`: 72 pruebas correctas.
- `docker compose --profile security config --quiet`: correcto.

## Pendiente antes de considerar H2/H3 completamente cerrados

TodavÃ­a debe ejecutarse la prueba de integraciÃ³n real del compose para comprobar discovery, Client Credentials, login/lockout y auditorÃ­a de Identity contra los contenedores reales.
