import { describe, expect, it } from 'vitest';
import { normalizeRoles, puede, Roles } from './roles';

describe('normalizeRoles', () => {
  it('acepta un rol como texto o varios como arreglo', () => {
    expect(normalizeRoles('Facturador')).toEqual(['Facturador']);
    expect(normalizeRoles(['Admin', 'Auditor'])).toEqual(['Admin', 'Auditor']);
  });

  it('ignora valores desconocidos, repetidos o de otro tipo', () => {
    expect(normalizeRoles(['Admin', 'admin', 'Root', 7, 'Admin'])).toEqual(['Admin']);
    expect(normalizeRoles(undefined)).toEqual([]);
  });
});

describe('puede', () => {
  it('sigue la matriz de políticas del servidor', () => {
    expect(puede([Roles.Facturador], 'gestionarFacturas')).toBe(true);
    expect(puede([Roles.Auditor], 'gestionarFacturas')).toBe(false);
    expect(puede([Roles.Auditor], 'leerAuditoria')).toBe(true);
    expect(puede([Roles.Cliente], 'leerFacturas')).toBe(true);
    expect(puede([Roles.Cliente], 'gestionarClientes')).toBe(false);
    expect(puede([], 'leerFacturas')).toBe(false);
  });
});
