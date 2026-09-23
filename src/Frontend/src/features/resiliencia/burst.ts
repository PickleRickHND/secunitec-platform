// R10: lógica pura del panel de resiliencia (calendario de la ráfaga, clasificación y series). Sin React ni red,
// para probarla con Vitest.

import { Roles, type Role } from '../../auth/roles';

/** Clases de respuesta, en el orden fijo de los colores de la gráfica (no cambiar: es la asignación de la paleta). */
export const CLASES = ['aceptadas', 'bloqueadas', 'sinAutenticar', 'sinPermiso', 'otros'] as const;
export type Clase = (typeof CLASES)[number];

export const etiquetaClase: Record<Clase, { nombre: string; corto: string; codigo: string }> = {
  aceptadas: { nombre: 'Aceptadas', corto: 'aceptadas', codigo: '200' },
  bloqueadas: { nombre: 'Bloqueadas por el gateway', corto: 'bloqueadas', codigo: '429' },
  sinAutenticar: { nombre: 'Sin autenticar', corto: 'sin autenticar', codigo: '401' },
  sinPermiso: { nombre: 'Sin permiso', corto: 'sin permiso', codigo: '403' },
  otros: { nombre: 'Otros errores', corto: 'otros errores', codigo: '5xx / red' },
};

/** status null = la petición no obtuvo respuesta (error de red o CORS). */
export function clasificar(status: number | null): Clase {
  if (status === null) {
    return 'otros';
  }

  if (status >= 200 && status < 300) {
    return 'aceptadas';
  }

  switch (status) {
    case 429:
      return 'bloqueadas';
    case 401:
      return 'sinAutenticar';
    case 403:
      return 'sinPermiso';
    default:
      return 'otros';
  }
}

export const LIMITES = { peticiones: { min: 1, max: 1000 }, segundos: { min: 1, max: 120 } } as const;

/** Mensaje de error de la configuración, o null si es válida. */
export function validarConfiguracion(peticiones: number, segundos: number): string | null {
  if (!Number.isInteger(peticiones) || peticiones < LIMITES.peticiones.min || peticiones > LIMITES.peticiones.max) {
    return `Las peticiones van de ${LIMITES.peticiones.min} a ${LIMITES.peticiones.max}.`;
  }

  if (!Number.isInteger(segundos) || segundos < LIMITES.segundos.min || segundos > LIMITES.segundos.max) {
    return `La duración va de ${LIMITES.segundos.min} a ${LIMITES.segundos.max} segundos.`;
  }

  return null;
}

/** N peticiones repartidas de forma uniforme en M segundos: desfase en ms de cada una, la primera en 0. */
export function calendario(peticiones: number, segundos: number): number[] {
  const paso = (segundos * 1000) / peticiones;
  return Array.from({ length: peticiones }, (_, indice) => Math.round(indice * paso));
}

export interface Resultado {
  readonly indice: number;
  readonly clase: Clase;
  readonly status: number | null;
  /** Momento en que llegó la respuesta, en ms desde el inicio de la ráfaga. */
  readonly respuestaMs: number;
  readonly latenciaMs: number;
  readonly correlationId: string;
  readonly retryAfter: number | null;
}

export type Conteo = Record<Clase, number>;

export const conteoVacio = (): Conteo => ({ aceptadas: 0, bloqueadas: 0, sinAutenticar: 0, sinPermiso: 0, otros: 0 });

export function contar(resultados: readonly Resultado[]): Conteo {
  const conteo = conteoVacio();
  for (const resultado of resultados) {
    conteo[resultado.clase] += 1;
  }

  return conteo;
}

export interface Punto {
  readonly segundo: number;
  readonly acumulado: Conteo;
}

/** Respuestas acumuladas por clase al final de cada segundo, de 0 a hasta (incluido). */
export function serieAcumulada(resultados: readonly Resultado[], hastaSegundo: number): Punto[] {
  const ordenados = [...resultados].sort((a, b) => a.respuestaMs - b.respuestaMs);
  const puntos: Punto[] = [];
  const acumulado = conteoVacio();
  let siguiente = 0;
  for (let segundo = 0; segundo <= hastaSegundo; segundo++) {
    for (let actual = ordenados[siguiente]; actual && actual.respuestaMs <= segundo * 1000; actual = ordenados[siguiente]) {
      acumulado[actual.clase] += 1;
      siguiente++;
    }

    puntos.push({ segundo, acumulado: { ...acumulado } });
  }

  return puntos;
}

export type Escenario = 'sesion' | 'sinToken' | 'tokenAlterado' | 'fueraDeRol' | 'mixto';
export type Autenticacion = 'valida' | 'ninguna' | 'alterada';

export interface Peticion {
  readonly path: string;
  readonly autenticacion: Autenticacion;
}

const LISTAR_FACTURAS = '/api/billing/facturas?tamano=1';

/** Endpoint que el rol no puede usar (403 decidido por la política del servidor), o null si no hay ninguno. */
export function objetivoFueraDeRol(roles: readonly Role[]): string | null {
  if (roles.includes(Roles.Admin)) {
    return null;
  }

  return roles.includes(Roles.Auditor) ? '/api/billing/clientes?tamano=1' : '/api/billing/auditoria?tamano=1';
}

export function escenariosDisponibles(roles: readonly Role[]): Escenario[] {
  const base: Escenario[] = ['sesion', 'sinToken', 'tokenAlterado'];
  return objetivoFueraDeRol(roles) ? [...base, 'fueraDeRol', 'mixto'] : [...base, 'mixto'];
}

export function peticionPara(escenario: Escenario, indice: number, roles: readonly Role[]): Peticion {
  switch (escenario) {
    case 'sesion':
      return { path: LISTAR_FACTURAS, autenticacion: 'valida' };
    case 'sinToken':
      return { path: LISTAR_FACTURAS, autenticacion: 'ninguna' };
    case 'tokenAlterado':
      return { path: LISTAR_FACTURAS, autenticacion: 'alterada' };
    case 'fueraDeRol':
      return { path: objetivoFueraDeRol(roles) ?? LISTAR_FACTURAS, autenticacion: 'valida' };
    case 'mixto': {
      const rotacion = escenariosDisponibles(roles).filter((valor) => valor !== 'mixto');
      return peticionPara(rotacion[indice % rotacion.length] ?? 'sesion', indice, roles);
    }
  }
}

/** Cambia un carácter de la firma del JWT: el gateway lo rechaza aunque encabezado y claims sean válidos. */
export function alterarToken(token: string): string {
  const ultimo = token.at(-1);
  return token.slice(0, -1) + (ultimo === 'A' ? 'B' : 'A');
}
