import { describe, expect, it } from 'vitest';
import { Roles } from '../../auth/roles';
import {
  alterarToken,
  calendario,
  clasificar,
  contar,
  escenariosDisponibles,
  objetivoFueraDeRol,
  peticionPara,
  type Resultado,
  serieAcumulada,
  validarConfiguracion,
} from './burst';

const resultado = (indice: number, status: number | null, respuestaMs: number): Resultado => ({
  indice,
  clase: clasificar(status),
  status,
  respuestaMs,
  latenciaMs: 5,
  correlationId: `r-${indice}`,
  retryAfter: status === 429 ? 30 : null,
});

describe('clasificar', () => {
  it('agrupa por el significado de la respuesta del gateway', () => {
    expect(clasificar(200)).toBe('aceptadas');
    expect(clasificar(204)).toBe('aceptadas');
    expect(clasificar(429)).toBe('bloqueadas');
    expect(clasificar(401)).toBe('sinAutenticar');
    expect(clasificar(403)).toBe('sinPermiso');
    expect(clasificar(500)).toBe('otros');
    expect(clasificar(null)).toBe('otros');
  });
});

describe('calendario', () => {
  it('reparte N peticiones de forma uniforme en M segundos', () => {
    expect(calendario(4, 2)).toEqual([0, 500, 1000, 1500]);
    expect(calendario(120, 10)).toHaveLength(120);
    expect(calendario(120, 10).at(-1)).toBeLessThan(10_000);
  });

  it('valida los límites de la configuración', () => {
    expect(validarConfiguracion(120, 10)).toBeNull();
    expect(validarConfiguracion(0, 10)).toContain('peticiones');
    expect(validarConfiguracion(1001, 10)).toContain('peticiones');
    expect(validarConfiguracion(10, 0)).toContain('duración');
    expect(validarConfiguracion(10.5, 10)).toContain('peticiones');
  });
});

describe('series', () => {
  const resultados = [resultado(0, 200, 100), resultado(1, 200, 900), resultado(2, 429, 1200), resultado(3, 401, 2500)];

  it('cuenta por clase', () => {
    expect(contar(resultados)).toEqual({ aceptadas: 2, bloqueadas: 1, sinAutenticar: 1, sinPermiso: 0, otros: 0 });
  });

  it('acumula por segundo según el momento de la respuesta', () => {
    const serie = serieAcumulada(resultados, 3);

    expect(serie.map((punto) => punto.segundo)).toEqual([0, 1, 2, 3]);
    expect(serie[0]!.acumulado.aceptadas).toBe(0);
    expect(serie[1]!.acumulado.aceptadas).toBe(2);
    expect(serie[2]!.acumulado.bloqueadas).toBe(1);
    expect(serie[3]!.acumulado).toEqual({ aceptadas: 2, bloqueadas: 1, sinAutenticar: 1, sinPermiso: 0, otros: 0 });
  });
});

describe('escenarios', () => {
  it('el 403 apunta a un endpoint que el rol no tiene', () => {
    expect(objetivoFueraDeRol([Roles.Facturador])).toContain('/auditoria');
    expect(objetivoFueraDeRol([Roles.Cliente])).toContain('/auditoria');
    expect(objetivoFueraDeRol([Roles.Auditor])).toContain('/clientes');
    expect(objetivoFueraDeRol([Roles.Admin])).toBeNull();
    expect(escenariosDisponibles([Roles.Admin])).not.toContain('fueraDeRol');
  });

  it('el modo mixto rota por los escenarios disponibles', () => {
    const autenticaciones = [0, 1, 2, 3, 4].map((indice) => peticionPara('mixto', indice, [Roles.Facturador]));

    expect(autenticaciones.map((peticion) => peticion.autenticacion)).toEqual(['valida', 'ninguna', 'alterada', 'valida', 'valida']);
    expect(autenticaciones[3]!.path).toContain('/auditoria');
  });

  it('alterar el token cambia solo el último carácter de la firma', () => {
    expect(alterarToken('aaa.bbb.ccA')).toBe('aaa.bbb.ccB');
    expect(alterarToken('aaa.bbb.ccc')).toBe('aaa.bbb.ccA');
  });
});
