# Problemas conocidos

Procedimientos verificados para problemas del entorno, no del código. Cada entrada indica el síntoma, la causa, la solución, cómo comprobarla y cuándo quitarla.

| # | Síntoma | Solución corta |
|---|---|---|
| 1 | `mongo` queda en `Exited (1)` con "Linux kernel versions 6.19 and newer..." | `MONGO_GLIBC_TUNABLES=glibc.pthread.rseq=1` en `.env` |
| 2 | El job "Secretos (gitleaks)" falla por un valor `CAMBIAR_...` de `.env.example` | Placeholder con formato `CAMBIAR_[A-Z0-9_]+`; ya exceptuado en `.gitleaks.toml` |
| 3 | El gateway no arranca o el navegador rechaza `https://localhost:8080` | `scripts/dev-cert.sh` (o `.ps1`) y `dotnet dev-certs https --trust` |
| 4 | La respuesta no trae `Strict-Transport-Security` | Es a propósito en `localhost`; se envía con cualquier otro host |
| 5 | Identity no escribe auditoría o no arranca por el usuario de Mongo | `docker compose down -v` (borra los datos de prueba) o crear el usuario a mano |
| 6 | Tras reiniciar Identity, las sesiones y los refresh tokens dejan de valer | Esperado en Development (llaves efímeras): volver a iniciar sesión |

---

## 1. MongoDB 8 no arranca: "Linux kernel versions 6.19 and newer..."

**Síntoma.** `docker compose ps` muestra `mongo` como `Exited (1)` y `docker compose logs mongo` tiene una sola línea, fatal (`"s":"F"`):

```
MongoDB cannot start: Linux kernel versions 6.19 and newer has a known incompatibility with this version of MongoDB.
See https://jira.mongodb.org/browse/SERVER-121912 for more information.
```

**Quién lo ve.** Cualquier máquina donde el kernel de Docker esté entre 6.19 y 7.0.13. Al 2026-09-22, Docker Desktop 4.92.0 (la última versión) trae el 7.0.12. Para comprobarlo:

```bash
docker run --rm alpine:3 uname -r
```

**Causa.** La imagen oficial `mongo:8` fija `GLIBC_TUNABLES=glibc.pthread.rseq=0` para que TCMalloc use rseq (cachés por CPU). Con esos kernels ese camino falla ([SERVER-121912](https://jira.mongodb.org/browse/SERVER-121912)), y mongod 8.x prefiere no arrancar a caerse después.

**Solución.** Solo en la máquina afectada, agregar a `.env` (no se cambia el compose, para no quitar rendimiento a las máquinas sanas):

```bash
MONGO_GLIBC_TUNABLES=glibc.pthread.rseq=1
```

Después, recrear el contenedor:

```bash
docker compose up -d mongo
```

Con `rseq=1`, glibc conserva rseq para sí, TCMalloc usa su modo sin rseq y mongod arranca. El costo es algo menos de rendimiento del asignador de memoria de Mongo, que en este proyecto solo guarda la auditoría.

`glibc.pthread.rseq=0` **no** sirve: es el valor que ya trae la imagen.

**Comprobación.** Probado el 2026-09-22 con Docker Desktop 4.92.0, kernel 7.0.12 y mongo 8.3.11:

```bash
docker compose exec mongo sh -c 'echo "$GLIBC_TUNABLES"'   # glibc.pthread.rseq=1
docker compose ps mongo                                   # Up ... (healthy)
```

En esa prueba aguantó 8 clientes en paralelo con 320.000 inserts sin errores fatales, y el flujo completo de Billing escribió sus eventos de auditoría.

**Cuándo quitarlo.** Cuando `uname -r` muestre 7.0.14 o posterior (Docker Desktop actualizado): borrar la línea de `.env` y volver a correr `docker compose up -d mongo`.

**Advertencia.** MongoDB no documenta este ajuste; su única solución oficial es un kernel 7.0.14 o posterior ([notas de versión de MongoDB 8.0](https://www.mongodb.com/docs/v8.0/release-notes/8.0/)). El problema de Docker Desktop está en [docker/desktop-feedback#682](https://github.com/docker/desktop-feedback/issues/682). Si el ajuste deja de funcionar, la alternativa es `mongo:7` (MongoDB 7.0 no está afectado), pero cambia una decisión del plan (§0) y exige borrar el volumen `mongo_data` si ya lo inició una versión 8.

---

## 2. gitleaks falla en CI por un placeholder de `.env.example`

**Síntoma.** El job "Secretos (gitleaks)" termina con `leaks found` y el log muestra algo como:

```
Finding:     BILLING_TEST_JWT_KEY=REDACTED
RuleID:      generic-api-key
File:        .env.example
```

El aviso `Resource not accessible by integration` del mismo job es inofensivo: el action no puede comentar en el PR porque el workflow tiene `permissions: contents: read`.

**Causa.** La regla `generic-api-key` detecta `ALGO_KEY=valor-largo` por entropía, aunque el valor sea un ejemplo. En CI, `gitleaks/gitleaks-action@v3` (gitleaks 8.24.3) revisa **todos los commits del PR**, así que cambiar el valor en un commit nuevo no alcanza: el hallazgo sigue en el commit viejo.

**Solución (ya aplicada).** `.gitleaks.toml`, en la raíz, extiende las reglas por defecto y exceptúa solo los valores que cumplen `^CAMBIAR_[A-Z0-9_]+$`. Para que un placeholder nuevo no dispare el falso positivo, debe empezar con `CAMBIAR_` y usar solo mayúsculas, dígitos y `_`.

Reglas para no abrir un hueco:

- No exceptuar por ruta (`paths`). En la 8.24.3, las condiciones de `[allowlist]` se combinan con OR: exceptuaría cualquier secreto real que alguien escriba en ese archivo.
- La 8.24.3 ignora la sintaxis nueva `[[allowlists]]`; hay que usar la tabla única `[allowlist]`.
- Para un falso positivo puntual que no sea un placeholder, agregar su `Fingerprint` (aparece en el log) a un archivo `.gitleaksignore`, en vez de ampliar la regex.
- Si el hallazgo es un secreto real, no agregar ninguna excepción: rotarlo primero y después limpiar la historia.

**Comprobación local con la misma versión que CI.** Desde la raíz del repo, en la rama del PR:

```bash
docker run --rm -v "$PWD":/repo:ro zricethezav/gitleaks:v8.24.3 detect --source /repo --redact --no-banner \
  --log-opts="--no-merges --first-parent origin/main..HEAD"
```

Debe terminar en `no leaks found`. Antes de cada commit sigue valiendo `gitleaks git --staged --no-banner .`, que lee el mismo `.gitleaks.toml`.

---

## 3. Certificado de desarrollo del gateway (TLS, TB0)

**Síntoma.** El gateway se reinicia con un error de Kestrel que no encuentra `/https/gateway.pem`. O bien el navegador o `curl` rechazan `https://localhost:8080` porque el certificado no es de confianza.

**Causa.** El gateway sirve HTTPS con el certificado de desarrollo de .NET, que no se versiona (`infra/certs` está en `.gitignore`); cada integrante lo genera en su máquina.

**Solución.**

```bash
scripts/dev-cert.sh                  # Windows: powershell -File scripts/dev-cert.ps1
dotnet dev-certs https --trust       # una vez, para que el navegador confíe en él
```

En macOS, el export pide permiso al llavero para leer la clave privada: hay que aceptar el diálogo. `curl` no usa el llavero; a `curl` y a `scripts/verify-hardening.sh` se les pasa el certificado explícitamente (`--cacert infra/certs/gateway.pem`).

**Comprobación.** `curl --cacert infra/certs/gateway.pem https://localhost:8080/health` responde 200.

---

## 4. HSTS no aparece en `localhost`

**Síntoma.** `scripts/verify-hardening.sh` informa "HSTS no se envía para localhost".

**Causa.** Es a propósito. `UseHsts()` excluye `localhost`, `127.0.0.1` y `[::1]`: HSTS vale para todo el host, no para un puerto. Si el navegador lo guardara para `localhost`, forzaría HTTPS también en el SPA (`http://localhost:3000`) y en cualquier otro proyecto local.

**Comprobación.** Con cualquier otro host (`SECUNITEC_PUBLIC_URL` distinto de `localhost`), la respuesta HTTPS trae `Strict-Transport-Security: max-age=15552000`.

---

## 5. Usuarios de Mongo que no existen en un volumen viejo

**Síntoma.** Identity o Billing registran `No se pudo registrar el evento de auditoría` con un error de autenticación contra Mongo.

**Causa.** Los scripts de `infra/mongo/init` (usuarios `billing_audit` e `identity_audit`, índices y roles) solo corren cuando el volumen `mongo_data` está vacío. Un volumen creado antes de la etapa 3.1 no tiene `identity_audit`.

**Solución.** Si se pueden perder los datos de prueba:

```bash
docker compose --profile security down -v
docker compose --profile security up -d --build
```

Si no, crear el rol y el usuario a mano con el contenido de `infra/mongo/init/02-identity.js`:

```bash
docker compose exec mongo mongosh -u secunitec_bootstrap -p "$MONGO_ADMIN_PASSWORD" --authenticationDatabase admin
```

---

## 6. Reiniciar Identity invalida las sesiones y los refresh tokens

**Síntoma.** Después de `docker compose restart identity` (o de reconstruirlo), el SPA vuelve a pedir el login y los refresh tokens anteriores fallan.

**Causa.** En Development, Identity firma y cifra con llaves efímeras en memoria, que no dependen del almacén de certificados de cada equipo. Al reiniciar se generan llaves nuevas.

- Los tokens nuevos se aceptan como máximo 30 s después, porque Billing y el Gateway vuelven a pedir el JWKS ante un `kid` desconocido.
- Los access tokens emitidos antes del reinicio pueden seguir aceptándose hasta su vencimiento (15 min).

**Solución.** Iniciar sesión otra vez. Fuera de Development, Identity usa los certificados configurados en `Identity:SigningCertificate` e `Identity:EncryptionCertificate`, que no cambian al reiniciar.
