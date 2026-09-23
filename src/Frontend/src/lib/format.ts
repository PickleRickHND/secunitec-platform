// Formatos de Honduras: lempiras con dos decimales, fechas dd/mm/aaaa y hora de Tegucigalpa.

const lempiras = new Intl.NumberFormat('es-HN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const entero = new Intl.NumberFormat('es-HN', { maximumFractionDigits: 0 });
const cantidad = new Intl.NumberFormat('es-HN', { maximumFractionDigits: 4 });
const fechaHora = new Intl.DateTimeFormat('es-HN', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
  timeZone: 'America/Tegucigalpa',
});

/** Monto calculado por el servidor, con el símbolo del lempira: L 1,234.50. */
export function formatLempiras(value: number): string {
  return `L ${lempiras.format(value)}`;
}

export function formatEntero(value: number): string {
  return entero.format(value);
}

export function formatCantidad(value: number): string {
  return cantidad.format(value);
}

/** Fecha ISO sin hora (2026-09-22) → 22/09/2026, sin conversión de zona horaria. */
export function formatFecha(iso: string | null | undefined): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso ?? '');
  return match ? `${match[3]}/${match[2]}/${match[1]}` : '';
}

/** Instante (ISO con zona) en hora de Honduras. */
export function formatFechaHora(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  // Mismo orden que formatFecha: 22/09/2026 20:30:00.
  const partes = Object.fromEntries(fechaHora.formatToParts(date).map((parte) => [parte.type, parte.value]));
  return `${partes.day ?? ''}/${partes.month ?? ''}/${partes.year ?? ''} ${partes.hour ?? ''}:${partes.minute ?? ''}:${partes.second ?? ''}`;
}

/** Porcentaje entero de una parte sobre el total; 0 si no hay total. */
export function porcentaje(parte: number, total: number): number {
  return total > 0 ? Math.round((parte / total) * 100) : 0;
}

/** Texto de un campo numérico a número (acepta separador de miles); el servidor valida rangos y decimales. */
export function aNumero(valor: string): number {
  return Number(valor.replace(/,/g, '').trim());
}
