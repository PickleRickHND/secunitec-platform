// Contrato del token (docs/PLAN.md §7.1). El SPA solo oculta lo que el rol no puede hacer: la autorización real la
// decide siempre el servidor (T-Tampering del Front-End, A01).

export const Roles = {
  Admin: 'Admin',
  Facturador: 'Facturador',
  Auditor: 'Auditor',
  Cliente: 'Cliente',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];

const conocidos = new Set<string>(Object.values(Roles));

/** El claim role llega como texto (un rol) o como arreglo (varios); se ignoran valores desconocidos. */
export function normalizeRoles(claim: unknown): Role[] {
  const values = Array.isArray(claim) ? claim : [claim];
  return [...new Set(values.filter((value): value is Role => typeof value === 'string' && conocidos.has(value)))];
}

/** Misma matriz que SecunitecPolicies.RolesByPolicy (BuildingBlocks). */
export const Permisos = {
  gestionarObligado: [Roles.Admin],
  gestionarClientes: [Roles.Admin, Roles.Facturador],
  gestionarFacturas: [Roles.Admin, Roles.Facturador],
  leerFacturas: [Roles.Admin, Roles.Facturador, Roles.Auditor, Roles.Cliente],
  leerAuditoria: [Roles.Admin, Roles.Auditor],
} as const satisfies Record<string, readonly Role[]>;

export type Permiso = keyof typeof Permisos;

export function puede(roles: readonly Role[], permiso: Permiso): boolean {
  return Permisos[permiso].some((role) => roles.includes(role));
}

export const nombreRol: Record<Role, string> = {
  Admin: 'Administración',
  Facturador: 'Facturación',
  Auditor: 'Auditoría',
  Cliente: 'Cliente',
};
