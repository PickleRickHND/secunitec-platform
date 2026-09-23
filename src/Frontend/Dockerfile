# Front-End (4.1): build con Node 22 y runtime nginx sin privilegios. Contexto: la raíz del repo (usa infra/nginx).
FROM node:22-alpine AS build
WORKDIR /app
RUN npm install --global pnpm@11.24.0

COPY src/Frontend/package.json src/Frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY src/Frontend/ ./
COPY infra/nginx/ /nginx/

# Fijados al construir: la URL del gateway entra en el bundle y en la CSP. Cambiarla exige --build.
ARG VITE_GATEWAY_URL=https://localhost:8080
ARG VITE_SPA_URL=http://localhost:3000
ARG VITE_RATE_LIMIT_PER_MINUTE=60
ENV VITE_GATEWAY_URL=$VITE_GATEWAY_URL \
    VITE_SPA_URL=$VITE_SPA_URL \
    VITE_RATE_LIMIT_PER_MINUTE=$VITE_RATE_LIMIT_PER_MINUTE

# La URL termina dentro de la configuración de nginx: solo se acepta esquema, host y puerto.
RUN echo "$VITE_GATEWAY_URL" | grep -Eq '^https?://[A-Za-z0-9.-]+(:[0-9]+)?$' \
    || { echo "VITE_GATEWAY_URL debe ser esquema://host[:puerto] sin barra final: $VITE_GATEWAY_URL" >&2; exit 1; } \
 && pnpm build \
 && sed "s#__GATEWAY_URL__#$VITE_GATEWAY_URL#g" /nginx/default.conf.template > /nginx/default.conf \
 && sed "s#__GATEWAY_URL__#$VITE_GATEWAY_URL#g" /nginx/security-headers.conf.template > /nginx/security-headers.conf

FROM nginxinc/nginx-unprivileged:alpine AS runtime
COPY --from=build /nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
USER 101
EXPOSE 8080
