import type { User } from 'oidc-client-ts';
import { createContext } from 'react';
import type { Role } from './roles';

export interface Sesion {
  readonly user: User | null;
  readonly cargando: boolean;
  readonly roles: readonly Role[];
  readonly email: string | null;
  readonly clienteId: string | null;
  readonly iniciarSesion: (volverA?: string) => Promise<void>;
  readonly cerrarSesion: () => Promise<void>;
  readonly accessToken: () => Promise<string | null>;
}

export const AuthContext = createContext<Sesion | null>(null);
