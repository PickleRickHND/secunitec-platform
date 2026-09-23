/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** URL pública del gateway, sin barra final (la misma SECUNITEC_PUBLIC_URL del compose). */
  readonly VITE_GATEWAY_URL?: string;
  /** URL pública del SPA, sin barra final (SECUNITEC_SPA_URL). */
  readonly VITE_SPA_URL?: string;
  /** Límite por usuario del gateway (RateLimiting:UserPermitLimit) para dibujar la línea del panel. */
  readonly VITE_RATE_LIMIT_PER_MINUTE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
