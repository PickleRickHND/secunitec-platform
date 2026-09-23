# Etapa 3.1 — Identity

## Objetivo

Implementar el proveedor de identidad de Secunitec para registro, login y emisión de tokens mediante OAuth 2.0 / OpenID Connect, manteniendo el contrato de claims definido en la etapa 1.1.

## Componentes

| Componente | Implementación |
|---|---|
| Usuarios | ASP.NET Core Identity con `ApplicationUser` basado en GUID |
| Persistencia | PostgreSQL `secunitec_identity` |
| Protocolo | OpenIddict 7.7.1 |
| Flujos | Authorization Code + PKCE, Refresh Token y Client Credentials |
| Tokens | Access token JWT con claims del contrato Secunitec |
| Discovery | `/.well-known/openid-configuration` |
| Auditoría | MongoDB, eventos de registro/login y fallos de autenticación |
| Contenedor | .NET 10 + imagen runtime chiseled |

## Controles de seguridad

`R03`: Identity gestiona OAuth 2.0 / OIDC, emisión y validación de tokens.

`R04`: la autorización es deny-by-default; únicamente los endpoints que el protocolo necesita exponer de forma anónima utilizan acceso anónimo explícito.

`R08`: los eventos de autenticación se registran en Mongo como evidencia de auditoría.

La política de contraseña exige longitud mínima de 12 caracteres, mayúsculas, minúsculas, dígitos y carácter no alfanumérico. El bloqueo se configura después de 5 intentos fallidos durante 15 minutos.

Los tokens de acceso tienen vida corta y los refresh tokens tienen una vigencia mayor para permitir renovación sin volver a solicitar credenciales.

## Claims

El `IdentityPrincipalFactory` mantiene los claims crudos del contrato:

`sub`, `role`, `tenant_id`, `cliente_id` y `client_id`.

Para Billing se establece la audiencia `secunitec-billing`.

## Clientes OAuth

Se registran:

- `spa-secunitec`: cliente público para Authorization Code + PKCE.
- `jmeter-load`: cliente confidencial para Client Credentials y pruebas de carga.

## Migraciones

La base de datos de Identity se versiona mediante EF Core. Se incluye:

- `IdentityInitialCreate`
- `ApplicationDbContextModelSnapshot`
- `ApplicationDbContextFactory` para operaciones de diseño y generación de migraciones.

La migración generada contiene una supresión local de diagnósticos de analizadores que no cambian el comportamiento de la migración.

## Validación realizada

- `dotnet build Secunitec.slnx -c Release --no-restore`: correcto.
- `dotnet test Secunitec.slnx -c Release --no-restore`: 72 pruebas correctas.
- `docker compose --profile security config --quiet`: correcto.

## Pendiente antes de considerar H2/H3 completamente cerrados

Todavía debe ejecutarse la prueba de integración real del compose para comprobar discovery, Client Credentials, login/lockout y auditoría de Identity contra los contenedores reales.
