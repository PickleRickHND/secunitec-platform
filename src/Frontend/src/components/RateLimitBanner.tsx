import { useRateLimitDeadline } from '../lib/rateLimitStore';
import { useCountdown } from '../lib/useCountdown';

/** R10 fuera del panel: si el gateway respondió 429 a la interfaz, se muestra cuánto falta para reintentar. */
export function RateLimitBanner() {
  const segundos = useCountdown(useRateLimitDeadline() || null);
  if (segundos <= 0) {
    return null;
  }

  return (
    <div className="alert alert-warn" role="status">
      <p className="alert-text">
        El gateway limitó sus peticiones para proteger el servicio. Podrá continuar en <strong>{segundos} s</strong>.
      </p>
    </div>
  );
}
