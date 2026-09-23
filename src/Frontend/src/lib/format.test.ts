import { describe, expect, it } from 'vitest';
import { formatCantidad, formatFecha, formatFechaHora, formatLempiras, porcentaje } from './format';

describe('formatos de Honduras', () => {
  it('muestra lempiras con separador de miles y dos decimales', () => {
    expect(formatLempiras(1234.5)).toBe('L 1,234.50');
    expect(formatLempiras(0)).toBe('L 0.00');
    expect(formatLempiras(165)).toBe('L 165.00');
  });

  it('muestra cantidades con hasta cuatro decimales', () => {
    expect(formatCantidad(2)).toBe('2');
    expect(formatCantidad(0.125)).toBe('0.125');
  });

  it('convierte fechas ISO a dd/mm/aaaa sin correrlas por la zona horaria', () => {
    expect(formatFecha('2026-09-22')).toBe('22/09/2026');
    expect(formatFecha(null)).toBe('');
    expect(formatFecha('22/09/2026')).toBe('');
  });

  it('muestra instantes en hora de Tegucigalpa (UTC-6)', () => {
    expect(formatFechaHora('2026-09-23T02:30:05Z')).toBe('22/09/2026 20:30:05');
    expect(formatFechaHora('no-es-fecha')).toBe('');
  });

  it('calcula porcentajes enteros sin dividir entre cero', () => {
    expect(porcentaje(1, 3)).toBe(33);
    expect(porcentaje(5, 0)).toBe(0);
  });
});
