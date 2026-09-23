# Etapa 4.2 — Endurecer y verificar

## Objetivo

Cerrar la Fase 2 con evidencia reproducible de cada política de seguridad del compose. El criterio de done es el checklist de `docs/04-hardening-verificacion.md` con la salida real de cada comando; lo genera `scripts/verify-hardening.sh --report`.

## Contenedores

Todos los servicios comparten el ancla `x-hardening` de `docker-compose.yml`:

- `cap_drop: [ALL]`: ningún servicio necesita capabilities. Todos escuchan en puertos mayores a 1024 y corren sin root, así que no hace falta `NET_BIND_SERVICE`.
- `read_only: true`, con `tmpfs` solo donde el proceso escribe.
- `security_opt: [no-new-privileges:true]`.
- `deploy.resources.limits` de CPU, memoria y procesos (`pids`).

| Servicio | Usuario | Escritura | CPU / memoria | Notas |
|---|---|---|---|---|
| postgres | `70:70` | volumen `postgres_data`; tmpfs `/var/run/postgresql`, `/tmp` | 1 / 512M | Sin root, el entrypoint no intenta `chown` ni `gosu` |
| mongo | `999:999` | volúmenes `mongo_data` y `mongo_config`; tmpfs `/tmp` | 1 / 768M | `--wiredTigerCacheSizeGB 0.25`: por defecto la caché toma la mitad de la RAM del límite |
| mongo-setup | `999:999` | tmpfs `/tmp` (`HOME`) | 0.25 / 256M | Corre en cada `up` y termina (usuarios, roles e índices idempotentes) |
| redis | `999:1000` | ninguna (`save ""`, sin AOF) | 0.25 / 192M | Antes corría como root: con `sh -c`, el entrypoint no bajaba privilegios |
| identity | 1654 | volumen `identity_keys`; tmpfs `/tmp` | 1 / 384M | Certificados PEM de solo lectura |
| billing | 1654 | tmpfs `/tmp` | **0.5 / 256M** | Límites bajos a propósito, para saturarlo en la etapa 5.3 |
| gateway | 1654 | tmpfs `/tmp` | 1 / 256M | Certificado TLS de solo lectura |
| frontend | 101 | tmpfs `/tmp` | 0.25 / 64M | La configuración de nginx se fija al construir la imagen: no escribe en `conf.d` |

Otros cambios del hardening:

- **Dockerfile de Billing:** copia solo los proyectos que necesita, igual que Identity y el Gateway. Antes usaba `COPY . .`.
- **`.dockerignore`:** excluye `infra/certs`, así los certificados y claves de desarrollo nunca entran en el contexto de build.
- **Cadenas de conexión de Npgsql:** `GSS Encryption Mode=Disable`. La imagen chiseled no trae Kerberos y cada arranque dejaba un error falso en el log.

## Verificación

`scripts/verify-hardening.sh`, con el perfil `security` arriba:

| Sección | Controles |
|---|---|
| TB0 y R06 | TLS; sin `Server` ni `X-Powered-By`; `nosniff`, `DENY`, CSP y correlation id; HSTS fuera de localhost. Identity conserva su CSP al pasar por el gateway |
| R03 | Discovery con issuer y endpoints públicos, incluido `end_session_endpoint` |
| R04 y R01 | 401 sin token; 200 con token de `jmeter-load`; 403 del rol Facturador en endpoints de Admin y de la auditoría |
| R09 | Preflight del SPA 204 con `Access-Control-Allow-Origin`; origen ajeno sin CORS; el 401 lleva CORS |
| Front-End | CSP con `connect-src` limitado al gateway, sin `unsafe-*`; cabeceras también en errores; `Server` sin versión |
| 4.2 | Por contenedor: usuario, uid real de cada proceso (`docker top`), `cap_drop`, solo lectura, `no-new-privileges` y límites. Billing con 0.5 CPU y 256 MiB. Ningún secreto literal en el compose |
| R12 y TB2 | Solo el gateway y el Front-End publican puertos, y solo en 127.0.0.1; `backend` y `data` internas |
| 2.1 | Redis sin `CONFIG` ni `FLUSHALL`; `billing_audit` no puede borrar la auditoría |
| R02 y R10 | 429 con `Retry-After` y JSON en `/connect/token`; el 429 lleva CORS y expone `Retry-After`; contadores en Redis y ningún aviso del respaldo en memoria |

- Con `--report` escribe `docs/04-hardening-verificacion.md`: fecha, commit, versiones, tabla de contenedores, checklist y la salida de cada comando.
- Nunca imprime secretos ni tokens.
- Si encuentra `/connect/token` limitado, espera el `Retry-After` en vez de fallar.

## CI

| Job | Qué agrega en 4.2 |
|---|---|
| `.NET build + test` | `dotnet tool restore` y `ef migrations has-pending-model-changes` para Billing e Identity |
| `Front-End lint + test + build` | ESLint, typecheck, Vitest y build del SPA; compila los E2E; compara los tokens de diseño del SPA con los de Identity |
| `Dependencias vulnerables` | `dotnet list package --vulnerable --include-transitive` evaluado con `jq`, porque el comando termina en 0 aunque encuentre vulnerables. `pnpm audit --audit-level=high` del SPA y de los E2E |
| `Docker Compose config` | Todos los perfiles |
| `Secretos (gitleaks)` | Sin cambios |

## Limitaciones conocidas

- **`Server: nginx`:** nginx OSS no permite quitar la cabecera; `server_tokens off` oculta la versión. R06 se exige al gateway, que es la única entrada a los servicios.
- **IP en la auditoría:** desde el host, todo llega al gateway desde la puerta de enlace de la red de Docker Desktop (por ejemplo `172.22.0.1`). Con clientes en otra máquina, la IP registrada es la real (docs/problemas-conocidos.md §9).
- **Healthchecks de .NET:** las imágenes chiseled no tienen shell ni `curl`, así que Identity, Billing y el Gateway no declaran healthcheck. Su salud la verifican los E2E y el script.
