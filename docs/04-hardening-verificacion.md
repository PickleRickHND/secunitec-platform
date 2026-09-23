# 4.2 · Verificación del hardening

Generado por `scripts/verify-hardening.sh --report`: no editar a mano. Cada control muestra la salida real del comando contra el compose levantado. Los secretos y tokens nunca se imprimen.

| Campo | Valor |
|---|---|
| Fecha | 2026-09-23 19:44 UTC |
| Commit | `e6fa4a8` |
| Docker | 29.8.0, Compose 5.5.1 |
| Gateway / SPA | https://localhost:8080 / http://localhost:3000 |
| Archivos de compose | `docker-compose.yml:docker-compose.observability.yml` |
| Resultado | **129 PASS, 0 FAIL** |

## Contenedores

| Servicio | Usuario | uid de los procesos | cap_drop | Solo lectura | security_opt | CPU | Memoria | pids |
|---|---|---|---|---|---|---|---|---|
| billing | 1654 | 1654  | [ALL] | true | [no-new-privileges:true] | 0.5 | 256 MiB | 200 |
| cadvisor | 65534:0 | 65534  | [ALL] | true | [no-new-privileges:true label=disable] | 0.25 | 256 MiB | 100 |
| frontend | 101 | 101  | [ALL] | true | [no-new-privileges:true] | 0.25 | 64 MiB | 100 |
| gateway | 1654 | 1654  | [ALL] | true | [no-new-privileges:true] | 1.0 | 256 MiB | 200 |
| grafana | 472 | 472  | [ALL] | true | [no-new-privileges:true] | 0.5 | 512 MiB | 300 |
| identity | 1654 | 1654  | [ALL] | true | [no-new-privileges:true] | 1.0 | 384 MiB | 200 |
| loki | 10001 | 10001  | [ALL] | true | [no-new-privileges:true] | 0.5 | 512 MiB | 200 |
| mongo | 999:999 | 999  | [ALL] | true | [no-new-privileges:true] | 1.0 | 768 MiB | 300 |
| otel-collector | 10001:10001 | 10001  | [ALL] | true | [no-new-privileges:true] | 0.5 | 256 MiB | 200 |
| postgres | 70:70 | 70  | [ALL] | true | [no-new-privileges:true] | 1.0 | 512 MiB | 200 |
| prometheus | nobody | 65534  | [ALL] | true | [no-new-privileges:true] | 0.5 | 512 MiB | 200 |
| redis | 999:1000 | 999  | [ALL] | true | [no-new-privileges:true] | 0.25 | 192 MiB | 100 |
| tempo | 10001:10001 | 10001  | [ALL] | true | [no-new-privileges:true] | 0.5 | 512 MiB | 200 |

## Checklist

| Sección | Control | Resultado |
|---|---|---|
| TB0 y R06: TLS y cabeceras del gateway | TLS: HTTPS con el certificado del gateway | PASS |
| TB0 y R06: TLS y cabeceras del gateway | R06: sin cabecera Server | PASS |
| TB0 y R06: TLS y cabeceras del gateway | R06: sin cabecera X-Powered-By | PASS |
| TB0 y R06: TLS y cabeceras del gateway | A05: X-Content-Type-Options nosniff | PASS |
| TB0 y R06: TLS y cabeceras del gateway | A05: X-Frame-Options DENY | PASS |
| TB0 y R06: TLS y cabeceras del gateway | A05: Content-Security-Policy presente | PASS |
| TB0 y R06: TLS y cabeceras del gateway | Correlation id en la respuesta | PASS |
| TB0 y R06: TLS y cabeceras del gateway | HSTS: no se envía para localhost (UseHsts excluye localhost; ver docs/problemas-conocidos.md §4) | INFO |
| TB0 y R06: TLS y cabeceras del gateway | Identity conserva su CSP propia a través del gateway (estilos 'self') | PASS |
| R03: discovery | R03: discovery 200 | PASS |
| R03: discovery | R03: issuer público | PASS |
| R03: discovery | R03: authorization_endpoint público | PASS |
| R03: discovery | R09: end_session_endpoint para el cierre de sesión del SPA | PASS |
| R04 y R01: autenticación y autorización a través del gateway | R04: /api/billing sin token → 401 | PASS |
| R04 y R01: autenticación y autorización a través del gateway | R03: token por client credentials 200 | PASS |
| R04 y R01: autenticación y autorización a través del gateway | R01: Billing a través del gateway con token → 200 | PASS |
| R04 y R01: autenticación y autorización a través del gateway | R04: rol Facturador en endpoint de Admin → 403 | PASS |
| R04 y R01: autenticación y autorización a través del gateway | R04: rol Facturador en la auditoría → 403 | PASS |
| R09: CORS para el SPA | CORS: preflight del SPA → 204 con Access-Control-Allow-Origin | PASS |
| R09: CORS para el SPA | CORS: un origen ajeno no recibe Access-Control-Allow-Origin | PASS |
| R09: CORS para el SPA | CORS: el 401 lleva Access-Control-Allow-Origin (el SPA puede leer el código) | PASS |
| Front-End: nginx sin privilegios con CSP | Front-End responde 200 | PASS |
| Front-End: nginx sin privilegios con CSP | Front-End: CSP con connect-src limitado al gateway | PASS |
| Front-End: nginx sin privilegios con CSP | Front-End: CSP sin 'unsafe-inline' ni 'unsafe-eval' | PASS |
| Front-End: nginx sin privilegios con CSP | Front-End: X-Content-Type-Options nosniff | PASS |
| Front-End: nginx sin privilegios con CSP | Front-End: X-Frame-Options DENY | PASS |
| Front-End: nginx sin privilegios con CSP | R06 (limitación de nginx OSS): Server sin versión | PASS |
| Front-End: nginx sin privilegios con CSP | Front-End: cabeceras de seguridad también en errores | PASS |
| 4.2: contenedores | billing: usuario sin privilegios (1654) | PASS |
| 4.2: contenedores | billing: ningún proceso con uid 0 (docker top: 1654 ) | PASS |
| 4.2: contenedores | billing: cap_drop ALL | PASS |
| 4.2: contenedores | billing: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | billing: no-new-privileges | PASS |
| 4.2: contenedores | billing: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | cadvisor: usuario sin privilegios (65534:0) | PASS |
| 4.2: contenedores | cadvisor: ningún proceso con uid 0 (docker top: 65534 ) | PASS |
| 4.2: contenedores | cadvisor: cap_drop ALL | PASS |
| 4.2: contenedores | cadvisor: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | cadvisor: no-new-privileges | PASS |
| 4.2: contenedores | cadvisor: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | frontend: usuario sin privilegios (101) | PASS |
| 4.2: contenedores | frontend: ningún proceso con uid 0 (docker top: 101 ) | PASS |
| 4.2: contenedores | frontend: cap_drop ALL | PASS |
| 4.2: contenedores | frontend: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | frontend: no-new-privileges | PASS |
| 4.2: contenedores | frontend: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | gateway: usuario sin privilegios (1654) | PASS |
| 4.2: contenedores | gateway: ningún proceso con uid 0 (docker top: 1654 ) | PASS |
| 4.2: contenedores | gateway: cap_drop ALL | PASS |
| 4.2: contenedores | gateway: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | gateway: no-new-privileges | PASS |
| 4.2: contenedores | gateway: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | grafana: usuario sin privilegios (472) | PASS |
| 4.2: contenedores | grafana: ningún proceso con uid 0 (docker top: 472 ) | PASS |
| 4.2: contenedores | grafana: cap_drop ALL | PASS |
| 4.2: contenedores | grafana: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | grafana: no-new-privileges | PASS |
| 4.2: contenedores | grafana: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | identity: usuario sin privilegios (1654) | PASS |
| 4.2: contenedores | identity: ningún proceso con uid 0 (docker top: 1654 ) | PASS |
| 4.2: contenedores | identity: cap_drop ALL | PASS |
| 4.2: contenedores | identity: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | identity: no-new-privileges | PASS |
| 4.2: contenedores | identity: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | loki: usuario sin privilegios (10001) | PASS |
| 4.2: contenedores | loki: ningún proceso con uid 0 (docker top: 10001 ) | PASS |
| 4.2: contenedores | loki: cap_drop ALL | PASS |
| 4.2: contenedores | loki: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | loki: no-new-privileges | PASS |
| 4.2: contenedores | loki: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | mongo: usuario sin privilegios (999:999) | PASS |
| 4.2: contenedores | mongo: ningún proceso con uid 0 (docker top: 999 ) | PASS |
| 4.2: contenedores | mongo: cap_drop ALL | PASS |
| 4.2: contenedores | mongo: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | mongo: no-new-privileges | PASS |
| 4.2: contenedores | mongo: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | otel-collector: usuario sin privilegios (10001:10001) | PASS |
| 4.2: contenedores | otel-collector: ningún proceso con uid 0 (docker top: 10001 ) | PASS |
| 4.2: contenedores | otel-collector: cap_drop ALL | PASS |
| 4.2: contenedores | otel-collector: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | otel-collector: no-new-privileges | PASS |
| 4.2: contenedores | otel-collector: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | postgres: usuario sin privilegios (70:70) | PASS |
| 4.2: contenedores | postgres: ningún proceso con uid 0 (docker top: 70 ) | PASS |
| 4.2: contenedores | postgres: cap_drop ALL | PASS |
| 4.2: contenedores | postgres: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | postgres: no-new-privileges | PASS |
| 4.2: contenedores | postgres: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | prometheus: usuario sin privilegios (nobody) | PASS |
| 4.2: contenedores | prometheus: ningún proceso con uid 0 (docker top: 65534 ) | PASS |
| 4.2: contenedores | prometheus: cap_drop ALL | PASS |
| 4.2: contenedores | prometheus: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | prometheus: no-new-privileges | PASS |
| 4.2: contenedores | prometheus: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | redis: usuario sin privilegios (999:1000) | PASS |
| 4.2: contenedores | redis: ningún proceso con uid 0 (docker top: 999 ) | PASS |
| 4.2: contenedores | redis: cap_drop ALL | PASS |
| 4.2: contenedores | redis: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | redis: no-new-privileges | PASS |
| 4.2: contenedores | redis: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | tempo: usuario sin privilegios (10001:10001) | PASS |
| 4.2: contenedores | tempo: ningún proceso con uid 0 (docker top: 10001 ) | PASS |
| 4.2: contenedores | tempo: cap_drop ALL | PASS |
| 4.2: contenedores | tempo: sistema de archivos de solo lectura | PASS |
| 4.2: contenedores | tempo: no-new-privileges | PASS |
| 4.2: contenedores | tempo: límites de CPU, memoria y procesos | PASS |
| 4.2: contenedores | Billing con límites bajos para la etapa 5.3 (0.5 CPU, 256 MiB) | PASS |
| 4.2: contenedores | Secretos solo por .env: ningún valor literal en los compose | PASS |
| R12 y TB2: redes y puertos | R12: solo el gateway, el Front-End y Grafana publican puertos en el host | PASS |
| R12 y TB2: redes y puertos | R12: los puertos publicados escuchan solo en 127.0.0.1 | PASS |
| R12 y TB2: redes y puertos | R12: red backend interna | PASS |
| R12 y TB2: redes y puertos | R12: red data interna | PASS |
| R12 y TB2: redes y puertos | R12: red observability interna | PASS |
| R12 y TB2: redes y puertos | Solo cAdvisor monta el socket de Docker, y en solo lectura | PASS |
| R12 y TB2: redes y puertos | Solo cAdvisor usa el espacio de PIDs del host | PASS |
| TB3: observabilidad (etapa 5.2) | TB3: identity y billing no están en la red observability | PASS |
| TB3: observabilidad (etapa 5.2) | TB3: el collector es el único en backend y observability a la vez | PASS |
| TB3: observabilidad (etapa 5.2) | TB3: Grafana exige login (401 sin credenciales) | PASS |
| TB3: observabilidad (etapa 5.2) | TB3: la contraseña de Grafana no es el placeholder de .env.example | PASS |
| TB3: observabilidad (etapa 5.2) | TB3: Grafana publica solo 127.0.0.1:3001 | PASS |
| 2.1: Redis y Mongo | Redis: CONFIG deshabilitado | PASS |
| 2.1: Redis y Mongo | Redis: FLUSHALL deshabilitado | PASS |
| 2.1: Redis y Mongo | Mongo: billing_audit no puede borrar la auditoría | PASS |
| 2.1: Redis y Mongo | R18: los pools de Npgsql caben en max_connections de Postgres | PASS |
| R02: rate limiting (va al final: agota el cupo de /connect/token) | R02: ráfaga en /connect/token → 429 | PASS |
| R02: rate limiting (va al final: agota el cupo de /connect/token) | R02: 429 con Retry-After | PASS |
| R02: rate limiting (va al final: agota el cupo de /connect/token) | R02: 429 con {"error":"rate_limited"} | PASS |
| R02: rate limiting (va al final: agota el cupo de /connect/token) | R10: el 429 lleva CORS y expone Retry-After al SPA | PASS |
| R02: rate limiting (va al final: agota el cupo de /connect/token) | R02: los contadores viven en Redis (no en el respaldo en memoria) | PASS |
| R02: rate limiting (va al final: agota el cupo de /connect/token) | Gateway sin avisos de respaldo en memoria (Redis alcanzable) | PASS |

## Evidencia

### TB0 y R06: TLS y cabeceras del gateway

```console
$ curl -sD - -o /dev/null https://localhost:8080/.well-known/openid-configuration
cache-control: no-store
x-content-type-options: nosniff
x-frame-options: DENY
referrer-policy: no-referrer
permissions-policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
content-security-policy: default-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'
x-correlation-id: d73698d5526a2940e314d9505a7f87fc
```

### R03: discovery

```console
$ curl -s https://localhost:8080/.well-known/openid-configuration | jq '{issuer, authorization_endpoint, token_endpoint, end_session_endpoint}'
{
  "issuer": "https://localhost:8080/",
  "authorization_endpoint": "https://localhost:8080/connect/authorize",
  "token_endpoint": "https://localhost:8080/connect/token",
  "end_session_endpoint": "https://localhost:8080/connect/endsession"
}
```

### R04 y R01: autenticación y autorización a través del gateway

```console
$ curl -s -o /dev/null -w '%{http_code}' .../api/billing/{facturas,obligados,auditoria}   # sin token, Facturador y Facturador
401 (sin token), 200 (facturas), 403 (obligados), 403 (auditoria)
```

### R09: CORS para el SPA

```console
$ curl -sD - -X OPTIONS https://localhost:8080/api/billing/facturas -H 'Origin: http://localhost:3000' -H 'Access-Control-Request-Method: GET' -H 'Access-Control-Request-Headers: authorization,x-correlation-id'
access-control-allow-headers: Authorization,Content-Type,X-Correlation-Id
access-control-allow-methods: GET,POST,PUT,DELETE
access-control-allow-origin: http://localhost:3000
access-control-max-age: 600
cache-control: no-store
x-content-type-options: nosniff
x-frame-options: DENY
referrer-policy: no-referrer
permissions-policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
content-security-policy: default-src 'none'; frame-ancestors 'none'
x-correlation-id: 9d8972ea886bcf16534195ac8d6a2af9
```

### Front-End: nginx sin privilegios con CSP

```console
$ curl -sD - -o /dev/null http://localhost:3000/
Server: nginx
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self'; connect-src 'self' https://localhost:8080; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
Cache-Control: no-store
```

### 4.2: contenedores

```console
$ grep -nE '(PASSWORD|SECRET|Password=|password=)' docker-compose.yml docker-compose.observability.yml | grep -v '${'
(sin coincidencias)
```

### R12 y TB2: redes y puertos

```console
$ docker compose ps --format '{{.Service}} {{.Ports}}'
billing 
cadvisor 8080/tcp
frontend 127.0.0.1:3000->8080/tcp
gateway 127.0.0.1:8080->8443/tcp
grafana 127.0.0.1:3001->3000/tcp
identity 
loki 3100/tcp
mongo 27017/tcp
otel-collector 4317-4318/tcp, 55679/tcp
postgres 5432/tcp
prometheus 9090/tcp
redis 6379/tcp
tempo 
```

```console
$ docker inspect <cada contenedor> --format 'pid={{.HostConfig.PidMode}} socket=<montaje de docker.sock>'
cadvisor pid=host socket=
```

### TB3: observabilidad (etapa 5.2)

```console
$ docker inspect <servicio> --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}'
gateway: secunitec-platform_backend secunitec-platform_data secunitec-platform_edge 
identity: secunitec-platform_backend secunitec-platform_data 
billing: secunitec-platform_backend secunitec-platform_data 
otel-collector: secunitec-platform_backend secunitec-platform_observability 
prometheus: secunitec-platform_observability 
tempo: secunitec-platform_observability 
loki: secunitec-platform_observability 
cadvisor: secunitec-platform_observability 
grafana: secunitec-platform_edge secunitec-platform_observability 
```

```console
$ curl -s -o /dev/null -w '%{http_code}' http://localhost:3001/api/search   # sin credenciales
401
```

### 2.1: Redis y Mongo

```console
$ redis-cli CONFIG GET maxmemory; redis-cli FLUSHALL   # dentro del contenedor, con la contraseña de .env
ERR unknown command 'CONFIG', with args beginning with: 'GET' 'maxmemory' 
ERR unknown command 'FLUSHALL', with args beginning with: 
```

```console
$ mongosh 'mongodb://billing_audit:***@localhost/secunitec_audit' --eval 'db.eventos.deleteMany({})'
not authorized on secunitec_audit to execute command { delete: 
```

```console
$ Maximum Pool Size de Billing e Identity (contenedores) contra max_connections - superuser_reserved_connections (Postgres)
billing 50
identity 20
total 70 / disponibles 97
```

### R02: rate limiting (va al final: agota el cupo de /connect/token)

```console
$ for i in 1..6: curl -sD - -X POST -H 'Origin: http://localhost:3000' https://localhost:8080/connect/token -d grant_type=client_credentials
access-control-allow-origin: http://localhost:3000
access-control-expose-headers: Retry-After,X-Correlation-Id
cache-control: no-store
retry-after: 60
x-content-type-options: nosniff
x-frame-options: DENY
referrer-policy: no-referrer
permissions-policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
content-security-policy: default-src 'none'; frame-ancestors 'none'
x-correlation-id: 02628392cdeb2ede72abd6d1c243c0fc
{"error":"rate_limited","retry_after":60}
```

```console
$ redis-cli --scan --pattern '*secunitec:rl:*' | head -5
rl:fw:{secunitec:rl:user-by-sub:sub:01a0ce69-6e46-7786-93b7-17348532816d}
rl:fw:{secunitec:rl:user-by-sub:ip:::ffff:172.22.0.1}
rl:fw:{secunitec:rl:token-endpoint:ip:::ffff:172.22.0.1}
rl:fw:{secunitec:rl:anon-by-ip:ip:::ffff:172.22.0.1}:exp
rl:fw:{secunitec:rl:user-by-sub:ip:::ffff:172.22.0.1}:exp
```

## Limitaciones conocidas

- **Server: nginx.** nginx OSS no puede quitar la cabecera `Server`; `server_tokens off` oculta la versión. R06 se cumple en el gateway, que es la única entrada a los servicios.
- **HSTS en localhost.** `UseHsts` excluye localhost a propósito (docs/problemas-conocidos.md §4); con otro host el control se verifica.
- **Preflight de CORS sin rate limiting.** Se responde en el gateway antes del limitador y nunca llega a un servicio interno.
- **cAdvisor (5.2).** Corre sin root, pero monta el socket de Docker (aunque sea `:ro`, da acceso a la API de Docker, que equivale a controlar el host) y usa `pid: host` para medir la red por contenedor. Es un riesgo aceptado del entorno de desarrollo: está en una red internal, sin puertos, y queda documentado en docs/06 y en STRIDE.
