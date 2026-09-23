// Configuración fijada al construir la imagen (build args del compose): la misma URL alimenta la CSP de nginx.
const trimSlash = (value: string) => value.replace(/\/+$/, '');

export const config = {
  gatewayUrl: trimSlash(import.meta.env.VITE_GATEWAY_URL ?? 'https://localhost:8080'),
  spaUrl: trimSlash(import.meta.env.VITE_SPA_URL ?? window.location.origin),
  rateLimitPerMinute: Number(import.meta.env.VITE_RATE_LIMIT_PER_MINUTE ?? '60'),
} as const;
