import { describe, expect, it } from 'vitest';
import { fieldError, problemMessage, toProblem } from './problem';

describe('toProblem', () => {
  it('conserva detail, code y errores por campo de Billing', () => {
    const problem = toProblem(400, {
      title: 'One or more validation errors occurred.',
      detail: 'La petición tiene datos inválidos.',
      code: 'validacion',
      errors: { 'lineas[0].precioUnitario': ['El precio debe ser mayor que cero.'], raro: 'no-es-arreglo' },
    });

    expect(problem.code).toBe('validacion');
    expect(fieldError(problem, 'lineas[0].precioUnitario')).toBe('El precio debe ser mayor que cero.');
    expect(problem.errors).not.toHaveProperty('raro');
    expect(problemMessage(problem)).toBe('Corrija los campos marcados.');
  });

  it('reconoce el cuerpo del 429 del gateway', () => {
    const problem = toProblem(429, { error: 'rate_limited', retry_after: 30 });

    expect(problem.code).toBe('rate_limited');
    expect(problemMessage(problem)).toContain('limitó');
  });

  it('usa el detalle del servidor y, si falta, un mensaje por estado', () => {
    expect(problemMessage(toProblem(400, { detail: 'El CAI venció: registre uno nuevo para seguir emitiendo.', code: 'cai.vencido' })))
      .toBe('El CAI venció: registre uno nuevo para seguir emitiendo.');
    expect(problemMessage(toProblem(403, null))).toBe('Su rol no tiene permiso para esta acción.');
    expect(problemMessage(toProblem(502, 'Bad gateway'))).toContain('no pudo completar');
  });
});
