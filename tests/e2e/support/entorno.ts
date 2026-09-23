import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Credenciales de .env (el mismo del compose). Nunca se imprimen.
const raiz = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const archivo = process.env.ENV_FILE ?? resolve(raiz, '.env');

const valores = Object.fromEntries(
  readFileSync(archivo, 'utf8')
    .split(/\r?\n/)
    .filter((linea) => /^[A-Z0-9_]+=/.test(linea))
    .map((linea) => [linea.slice(0, linea.indexOf('=')), linea.slice(linea.indexOf('=') + 1)]),
);

function requerido(clave: string): string {
  const valor = valores[clave];
  if (!valor) {
    throw new Error(`Defina ${clave} en ${archivo} (los E2E usan los usuarios de demostración).`);
  }

  return valor;
}

export const usuarios = {
  admin: { email: requerido('IDENTITY_SEED_ADMIN_EMAIL'), password: requerido('IDENTITY_SEED_ADMIN_PASSWORD') },
  facturador: { email: 'facturador@secunitec.local', password: requerido('IDENTITY_SEED_DEMO_PASSWORD') },
  auditor: { email: 'auditor@secunitec.local', password: requerido('IDENTITY_SEED_DEMO_PASSWORD') },
  cliente: { email: 'cliente@secunitec.local', password: requerido('IDENTITY_SEED_DEMO_PASSWORD') },
} as const;
