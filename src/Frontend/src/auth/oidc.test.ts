import { describe, expect, it, vi } from 'vitest';

// oidc.ts y config.ts leen window al cargarse (sessionStorage y location); en el entorno node de Vitest no existe.
vi.stubGlobal('window', { sessionStorage: new Map(), location: { origin: 'http://localhost:3000' } });
const { configuracionOidc } = await import('./oidc');

/** Storage en memoria que registra qué claves se escriben. */
function almacenDePrueba(): Storage & { claves: string[] } {
  const datos = new Map<string, string>();
  return {
    claves: [],
    get length() {
      return datos.size;
    },
    clear: () => datos.clear(),
    getItem: (clave: string) => datos.get(clave) ?? null,
    key: (indice: number) => [...datos.keys()][indice] ?? null,
    removeItem: (clave: string) => void datos.delete(clave),
    setItem(clave: string, valor: string) {
      this.claves.push(clave);
      datos.set(clave, valor);
    },
  };
}

describe('configuracionOidc', () => {
  it('guarda el state y el code_verifier del login en el almacenamiento recibido, no en localStorage', async () => {
    const almacen = almacenDePrueba();
    const configuracion = configuracionOidc(almacen);

    await configuracion.stateStore!.set('estado-de-prueba', '{"code_verifier":"x"}');
    await configuracion.userStore!.set('usuario-de-prueba', '{}');

    expect(almacen.claves).toEqual(['oidc.estado-de-prueba', 'oidc.usuario-de-prueba']);
  });

  it('usa Authorization Code con PKCE', () => {
    const configuracion = configuracionOidc(almacenDePrueba());

    expect(configuracion.response_type).toBe('code');
    expect(configuracion.disablePKCE).not.toBe(true);
  });
});
