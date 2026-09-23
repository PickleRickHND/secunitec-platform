// R10: el gateway responde 429 con Retry-After (segundos) y {"error":"rate_limited","retry_after":N}.
// RFC 9110 §10.2.3 también admite una fecha HTTP; se aceptan ambas formas y el cuerpo como respaldo.

const HTTP_DATE = /^[A-Za-z]{3,9},? .+ GMT$|^[A-Za-z]{3} [A-Za-z]{3} +\d{1,2} \d{2}:\d{2}:\d{2} \d{4}$/;

/** Segundos que faltan para reintentar, o null si la respuesta no lo indica. Nunca negativo. */
export function parseRetryAfter(header: string | null | undefined, body: unknown, now: Date = new Date()): number | null {
  const value = header?.trim();
  if (value) {
    if (/^\d+$/.test(value)) {
      return Number(value);
    }

    // Solo fechas HTTP (IMF-fixdate / RFC 850 con GMT, o asctime): Date.parse aceptaría "-3" como un año.
    const date = HTTP_DATE.test(value) ? Date.parse(value.endsWith('GMT') ? value : `${value} GMT`) : Number.NaN;
    if (!Number.isNaN(date)) {
      return Math.max(0, Math.ceil((date - now.getTime()) / 1000));
    }
  }

  if (typeof body === 'object' && body !== null && 'retry_after' in body) {
    const seconds = body.retry_after;
    if (typeof seconds === 'number' && Number.isFinite(seconds) && seconds >= 0) {
      return Math.ceil(seconds);
    }
  }

  return null;
}

/** Segundos enteros que quedan hasta el instante límite (redondeo hacia arriba), para la cuenta regresiva. */
export function remainingSeconds(deadlineMs: number, nowMs: number): number {
  return Math.max(0, Math.ceil((deadlineMs - nowMs) / 1000));
}
