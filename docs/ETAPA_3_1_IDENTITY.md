# Etapa 3.1 — Identity

## Objetivo

Proveedor de identidad propio en .NET 10 que registra usuarios, autentica y emite JWT RS256 con OAuth 2.0 / OpenID Connect, con los claims del contrato de la etapa 1.1 (**R03**).

## Componentes

| Componente | Implementación |
|---|---|
| Usuarios | ASP.NET Core Identity con `ApplicationUser` (GUID, `TenantId`, `ClienteId`) sobre Postgres `secunitec_identity` |
| Protocolo | OpenIddict 7.7.1: discovery, JWKS, Authorization Code + PKCE, Refresh Token y Client Credentials |
| Páginas | Razor Pages `/account/login` y `/account/register` (antiforgery automático) |
| Tokens | Access token JWT RS256 sin cifrar, 15 min; refresh token 7 días |
| Issuer | URL pública del gateway (`SECUNITEC_PUBLIC_URL`, por defecto `https://localhost:8080/`) |
| Auditoría | Mongo `secunitec_audit.identity_events`, usuario con permisos solo de inserción y lectura |
| Contenedor | .NET 10 sobre `aspnet:10.0-noble-chiseled-extra`, sin privilegios, solo en las redes `backend` y `data` |

## Controles de seguridad

- **R04:** deny-by-default (`FallbackPolicy` de 1.1). Solo son anónimas las páginas de cuenta, `/connect/*` y el discovery.
- **A07:**
  - Contraseñas de 12 caracteres o más, con mayúscula, minúscula, dígito y símbolo.
  - Lockout de 15 minutos tras 5 intentos fallidos.
  - El mismo mensaje para un correo inexistente que para una contraseña incorrecta.
  - El registro de un correo que ya existe responde igual que un alta nueva.
- **A01:** después del login solo se redirige a URLs locales (`Url.IsLocalUrl`, que rechaza `//host` y `/\host`).
- **Cookies:** `__Host-Secunitec.Identity` y `__Host-Secunitec.Antiforgery`, con `Secure`, `HttpOnly` y `SameSite=Lax`. El TLS termina en el gateway e Identity ve el esquema https por `X-Forwarded-Proto`.
- **TB1:** Identity acepta `X-Forwarded-For`, `X-Forwarded-Proto` y `X-Forwarded-Host` solo de redes privadas, y el host solo si coincide con el del issuer. Así la auditoría registra la IP real del cliente y el discovery anuncia URLs públicas.
- **R08 / A09:**
  - Se auditan en Mongo el login exitoso y el fallido, el bloqueo y el registro, con actor, tenant, IP, correlation id y resultado.
  - La escritura nunca interrumpe el login: si Mongo falla, queda un warning en el log.
- **Secretos:** Identity no arranca si la contraseña del admin, la de demo o el secreto de `jmeter-load` conservan el valor `CAMBIAR_` de `.env.example`.
- **Llaves de firma:** efímeras en Development, que no tocan el almacén de certificados del equipo. Fuera de Development se exigen certificados PEM (`Identity:SigningCertificate:*` y `Identity:EncryptionCertificate:*`).

## Claims

`IdentityPrincipalFactory` emite los claims crudos del contrato: `sub`, `role`, `tenant_id`, `cliente_id` (solo con el rol Cliente), `client_id` y `aud = secunitec-billing`.

- **Usuarios:** `sub` es el GUID del usuario.
- **Aplicaciones (client credentials):** `sub` es el GUID de la aplicación en OpenIddict. El contrato exige un GUID y Billing lo registra como actor de cada escritura.

## Clientes OAuth y datos iniciales

| Cliente | Tipo | Uso |
|---|---|---|
| `spa-secunitec` | Público, PKCE obligatorio | SPA de la etapa 4.1 (`redirect_uri` `http://localhost:3000/auth/callback`) |
| `jmeter-load` | Confidencial | Client credentials para las pruebas de carga; rol Facturador en el tenant de ejemplo |

El consentimiento es implícito: ambos son clientes propios (decisión registrada en `docs/PLAN.md`).

Datos iniciales:
- los 4 roles y el administrador;
- si se define `IDENTITY_SEED_DEMO_PASSWORD`, `facturador@`, `auditor@` y `cliente@secunitec.local`, este último con el `cliente_id` que Billing siembra en Development.

Un usuario que se registra por su cuenta queda **sin rol**: Cliente exige un `cliente_id` que asigna un Admin.

## Pruebas

`tests/Secunitec.Identity.Tests` (WebApplicationFactory y Testcontainers con Postgres 17 y Mongo 8) cubre:
- discovery 200 con endpoints públicos;
- client credentials con los claims del contrato;
- login fallido auditado con IP y correlation id;
- lockout al sexto intento (423), auditado;
- cookie `__Host-` con `Secure`;
- redirecciones solo locales;
- 400 sin token antiforgery;
- registro duplicado sin enumeración y sin rol.

Sin Docker, estos tests se saltan en local; en CI (`CI=true`) fallan.
