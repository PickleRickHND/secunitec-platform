# Problemas conocidos

Procedimientos verificados para problemas del entorno, no del código. Cada entrada indica el síntoma, la causa, la solución, cómo comprobarla y cuándo quitarla.

| # | Síntoma | Solución corta |
|---|---|---|
| 1 | `mongo` queda en `Exited (1)` con "Linux kernel versions 6.19 and newer..." | `MONGO_GLIBC_TUNABLES=glibc.pthread.rseq=1` en `.env` |
| 2 | El job "Secretos (gitleaks)" falla por un valor `CAMBIAR_...` de `.env.example` | Placeholder con formato `CAMBIAR_[A-Z0-9_]+`; ya exceptuado en `.gitleaks.toml` |
| 3 | El gateway no arranca o el navegador rechaza `https://localhost:8080` | `scripts/dev-cert.sh` (o `.ps1`) y `dotnet dev-certs https --trust` |
| 4 | La respuesta no trae `Strict-Transport-Security` | Es a propósito en `localhost`; se envía con cualquier otro host |
| 5 | Identity o Billing no escriben auditoría por el usuario de Mongo | Desde la etapa 4.2 lo corrige `mongo-setup` en cada `up`; si falla, ver sus logs |
| 6 | Tras reiniciar Identity, las sesiones y los refresh tokens dejan de valer | Generar los PEM con `scripts/dev-cert.sh`; sin ellos, volver a iniciar sesión |
| 7 | El login del SPA falla o CORS rechaza las llamadas | Abrir `http://localhost:3000`, no `127.0.0.1` ni la IP de la máquina |
| 8 | "Demasiados inicios de sesión seguidos" o 429 en `/connect/token` | Esperado: 5 canjes por minuto por IP. Esperar el `Retry-After` |
| 9 | La auditoría muestra la IP `172.x.0.1` en todos los eventos | Es la puerta de enlace de Docker Desktop; con clientes remotos se ve la IP real |
| 10 | `mongo` o `postgres` no arrancan con "Permission denied" tras la etapa 4.2 | Un volumen con archivos de root: devolverlos al usuario del servicio (abajo) |
| 11 | El Front-End responde `Server: nginx` | Limitación de nginx OSS; la versión no se revela |

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

En macOS, el export pide permiso al llavero para leer la clave privada: hay que aceptar el diálogo (el script queda esperando hasta entonces). `curl` no usa el llavero; a `curl` y a `scripts/verify-hardening.sh` se les pasa el certificado explícitamente (`--cacert infra/certs/gateway.pem`).

El script deja el certificado y las claves con permiso 644: los contenedores corren con un usuario distinto al tuyo y `dotnet dev-certs` puede exportar el `.pem` con 600, que el gateway no podría leer. Desde la etapa 4.2 también genera, una sola vez, los certificados de firma y cifrado de Identity (§6).

**Comprobación.** `curl --cacert infra/certs/gateway.pem https://localhost:8080/health` responde 200.

---

## 4. HSTS no aparece en `localhost`

**Síntoma.** `scripts/verify-hardening.sh` informa "HSTS no se envía para localhost".

**Causa.** Es a propósito. `UseHsts()` excluye `localhost`, `127.0.0.1` y `[::1]`: HSTS vale para todo el host, no para un puerto. Si el navegador lo guardara para `localhost`, forzaría HTTPS también en el SPA (`http://localhost:3000`) y en cualquier otro proyecto local.

**Comprobación.** Con cualquier otro host (`SECUNITEC_PUBLIC_URL` distinto de `localhost`), la respuesta HTTPS trae `Strict-Transport-Security: max-age=15552000`.

---

## 5. Usuarios de Mongo que no existen en un volumen viejo

**Síntoma.** Identity o Billing registran `No se pudo registrar el evento de auditoría` con un error de autenticación contra Mongo, o Identity y Billing no arrancan porque `mongo-setup` terminó con error.

**Causa.** Hasta la etapa 3.2, los usuarios se creaban con scripts de `/docker-entrypoint-initdb.d`, que solo corren con el volumen vacío. Desde la etapa 4.2 lo hace el servicio `mongo-setup` (`infra/mongo/setup/secunitec-setup.js`) en cada `up`: crea lo que falta y actualiza lo que existe, incluido el rol de `billing_audit`, que antes era `readWrite`.

**Solución.** Revisar el motivo en `docker compose logs mongo-setup`. Lo habitual es una contraseña de `.env` distinta a la del volumen (`MONGO_ADMIN_PASSWORD` solo se aplica al crear el volumen). Si se pueden perder los datos de prueba:

```bash
docker compose --profile security down -v
docker compose --profile security up -d --build
```

---

## 6. Reiniciar Identity invalida las sesiones y los refresh tokens

**Síntoma.** Después de `docker compose restart identity` (o de reconstruirlo), el SPA vuelve a pedir el login y los refresh tokens anteriores fallan.

**Causa.** Sin los certificados `infra/certs/identity-signing.*` e `identity-encryption.*`, Identity firma y cifra en Development con llaves efímeras en memoria, que no dependen del almacén de certificados de cada equipo. Al reiniciar se generan llaves nuevas. Desde la etapa 4.2, `scripts/dev-cert.sh` genera esos certificados (una sola vez) y las llaves de Data Protection de las cookies viven en el volumen `identity_keys`: con ambos, las sesiones sobreviven al reinicio. En Windows, `dev-cert.ps1` usa el `openssl` de Git para Windows; si no lo encuentra, Identity sigue con llaves efímeras.

- Los tokens nuevos se aceptan como máximo 30 s después, porque Billing y el Gateway vuelven a pedir el JWKS ante un `kid` desconocido.
- Los access tokens emitidos antes del reinicio pueden seguir aceptándose hasta su vencimiento (15 min).

**Solución.** Iniciar sesión otra vez. Fuera de Development, Identity usa los certificados configurados en `Identity:SigningCertificate` e `Identity:EncryptionCertificate`, que no cambian al reiniciar.

---

## 7. Usar `localhost`, no `127.0.0.1`

**Síntoma.** El botón "Iniciar sesión" del SPA no hace nada o el navegador muestra errores de CORS al llamar al gateway.

**Causa.** El SPA fija su origen al construir la imagen (`SECUNITEC_SPA_URL`, por defecto `http://localhost:3000`):
- El gateway solo admite ese origen en CORS y Identity solo acepta esas URIs de redirección, comparadas como texto.
- Además, PKCE usa `crypto.subtle`, que el navegador solo habilita en contextos seguros: `https` o `localhost`. Una IP de la red local no lo es.

**Solución.** Abrir `http://localhost:3000`. Para servir el sistema con otro host, cambiar `SECUNITEC_PUBLIC_URL` y `SECUNITEC_SPA_URL` en `.env` (el SPA con `https`) y levantar con `--build`.

---

## 8. 429 en `/connect/token` al repetir inicios de sesión

**Síntoma.** El SPA muestra "Demasiados inicios de sesión seguidos", o `scripts/verify-hardening.sh` informa que espera el `Retry-After`.

**Causa.** Es el control R02: el gateway admite 5 canjes de token por minuto por IP. Desde el host, el navegador, Playwright, `curl` y JMeter salen por la misma IP de Docker Desktop y comparten ese cupo.

**Solución.** Esperar la cuenta regresiva: el SPA ofrece "Reintentar" al terminar y, con la sesión de Identity abierta, no vuelve a pedir la contraseña. Los E2E y el script esperan solos. No bajar el límite para las pruebas.

---

## 9. La IP de la auditoría es la de Docker Desktop

**Síntoma.** En la pantalla de auditoría todos los eventos muestran la misma IP, por ejemplo `172.22.0.1`.

**Causa.** Docker Desktop publica los puertos a través de su propia red: el gateway ve como cliente la puerta de enlace de la red `edge`, y esa es la IP que reenvía en `X-Forwarded-For`. Identity y Billing confían en ese encabezado solo si viene de una red privada, así que registran la IP que ve el gateway.

**Comprobación.** Con un cliente en otra máquina (con Docker en Linux o con el puerto publicado en la red), la auditoría registra la IP real.

---

## 10. `mongo` o `postgres` no arrancan con "Permission denied" después de la etapa 4.2

**Síntoma.** El contenedor queda en `Exited` y sus logs mencionan `Permission denied` sobre `/data/db` o `/var/lib/postgresql/data`.

**Causa.** Desde la etapa 4.2 ambos corren con su usuario (`999` y `70`) y sin capabilities, así que ya no pueden corregir permisos al arrancar. Un volumen con archivos creados por root (por ejemplo, tras ejecutar `mongosh` como root dentro del contenedor) queda inaccesible. Se probó la actualización desde volúmenes de la etapa 3.2 y no ocurrió, porque los entrypoints de las imágenes oficiales ya les daban el dueño correcto.

**Solución.** Devolver los archivos al usuario del servicio con un contenedor de un solo uso, que sí es root:

```bash
docker run --rm -v secunitec-platform_mongo_data:/data alpine chown -R 999:999 /data
docker run --rm -v secunitec-platform_postgres_data:/data alpine chown -R 70:70 /data
```

---

## 11. El Front-End responde `Server: nginx`

**Síntoma.** `curl -I http://localhost:3000` muestra `Server: nginx`.

**Causa.** nginx OSS no permite quitar la cabecera `Server`; `server_tokens off` solo oculta la versión. Quitarla exige el módulo `headers-more`, que la imagen `nginx-unprivileged` no incluye.

**Alcance.** R06 exige suprimir `Server` y `X-Powered-By` en el core y en el gateway, que es la única entrada a los servicios; allí se cumple. El Front-End solo sirve archivos estáticos. Queda documentado en `docs/04-hardening-verificacion.md`.

